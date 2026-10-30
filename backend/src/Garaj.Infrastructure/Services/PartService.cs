using FluentValidation;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Catálogo de repuestos. Es del taller entero: las existencias, que sí son por sucursal,
/// viven en <see cref="StockService"/>.
/// </summary>
public class PartService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IValidator<SavePartRequest> validador) : IPartService
{
    public async Task<PagedResult<PartDto>> ListAsync(PartQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        EnsureStaff(scope);

        var q = db.Parts.AsNoTracking();

        if (!query.IncludeInactive) q = q.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Category)) q = q.Where(p => p.Category == query.Category);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p =>
                EF.Functions.ILike(p.Sku, $"%{term}%")
                || EF.Functions.ILike(p.Name, $"%{term}%")
                || (p.Brand != null && EF.Functions.ILike(p.Brand, $"%{term}%")));
        }

        var total = await q.CountAsync(ct);

        // Ordenar sobre la entidad y proyectar después: EF no traduce el OrderBy sobre el DTO.
        var items = await Project(q.OrderBy(p => p.Name), scope)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<PartDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<PartDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        EnsureStaff(scope);

        return await Project(db.Parts.AsNoTracking().Where(p => p.Id == id), scope).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("El repuesto no existe.");
    }

    public async Task<PartDto> CreateAsync(SavePartRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        var sku = NormalizeSku(request.Sku);
        await EnsureSkuIsFreeAsync(sku, null, ct);

        var part = new Part { Sku = sku };
        Apply(part, request);

        db.Parts.Add(part);
        await db.SaveChangesAsync(ct);

        return await GetAsync(part.Id, ct);
    }

    public async Task<PartDto> UpdateAsync(Guid id, SavePartRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("El repuesto no existe.");

        var sku = NormalizeSku(request.Sku);
        await EnsureSkuIsFreeAsync(sku, id, ct);

        part.Sku = sku;
        Apply(part, request);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default)
    {
        EnsureStaff(AccessScope.From(tenantContext));

        return await db.Parts.AsNoTracking()
            .Where(p => p.Category != null)
            .Select(p => p.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
    }

    // ---------- Carga desde Excel ----------

    public byte[] ImportTemplate() => CatalogSheets.PlantillaDeRepuestos();

    public async Task<ImportResultDto> ImportAsync(
        Stream file, bool apply, Guid? branchId = null, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        // Primero que exista —el filtro global ya la acota al taller— y después si es suya:
        // una sucursal de otro taller tiene que responder 404 y no 403, que confirmaría que
        // ese id existe en alguna parte.
        if (branchId is { } sucursal)
        {
            if (!await db.Branches.AnyAsync(b => b.Id == sucursal, ct))
                throw new NotFoundException("La sucursal no existe.");

            scope.EnsureBranchAllowed(sucursal);
        }

        var filas = CatalogSheets.LeerRepuestos(file);
        Importaciones.EnsureCabe(filas.Count);

        // Lo que ya existe, por SKU: decide si cada fila crea o actualiza, y evita una
        // consulta por renglón en un archivo de trescientos.
        var skus = filas.Where(f => f.Dato is not null)
            .Select(f => NormalizeSku(f.Dato!.Parte.Sku))
            .ToHashSet();

        var existentes = await db.Parts
            .Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, ct);

        // Las existencias de esos repuestos en esa bodega, no las de toda la bodega: un taller
        // con diez mil renglones en stock no tiene por qué traerlos para cargar veinte.
        var conocidos = existentes.Values.Select(p => p.Id).ToList();

        var bodega = branchId is { } bodegaId && conocidos.Count > 0
            ? await db.StockItems
                .Where(i => i.BranchId == bodegaId && conocidos.Contains(i.PartId))
                .ToDictionaryAsync(i => i.PartId, ct)
            : [];

        var errores = new List<ImportRowDto>();
        var buenas = new List<ImportRowDto>();
        var vistos = new HashSet<string>();
        int creados = 0, actualizados = 0;

        // Con tres mil filas, EF revisa todo lo que lleva en memoria en cada Add y el tiempo
        // crece con el cuadrado de las filas. Aquí no hace falta: nada cambia fuera de este
        // bucle, y al salir se vuelve a encender para que el resto del servicio no se entere.
        var autodeteccion = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            foreach (var fila in filas)
            {
                var error = fila.Error;
                var sku = fila.Dato is null ? fila.Key : NormalizeSku(fila.Dato.Parte.Sku);

                // Las mismas reglas que la pantalla de Inventario: largos de columna, precios
                // que no sean negativos. Sin esto, una celda de mil caracteres revienta en
                // Npgsql a mitad del archivo y el taller recibe un 500 sin saber qué fila fue.
                if (error is null
                    && validador.Validate(fila.Dato!.Parte) is { IsValid: false } malo)
                    error = malo.Errors[0].ErrorMessage;

                // Lo de la bodega sin bodega a la que entrar: pasaría inadvertido y el taller
                // creería que ya quedó cargado.
                if (error is null && branchId is null && TieneDatosDeBodega(fila.Dato!))
                    error = "Elija a qué sucursal entran la existencia, el mínimo y la ubicación.";

                if (error is null && fila.Dato!.Ubicacion is { Length: > 80 })
                    error = "La ubicación no puede pasar de 80 letras.";

                // El mismo SKU dos veces en el archivo: la segunda pisaría a la primera sin que
                // nadie se entere, y casi siempre es que alguien copió un renglón y no lo cambió.
                if (error is null && !vistos.Add(sku))
                    error = $"El SKU {sku} está repetido en el archivo.";

                if (error is not null)
                {
                    errores.Add(new ImportRowDto(fila.Numero, sku, fila.Name, "error", error));
                    continue;
                }

                if (existentes.TryGetValue(sku, out var parte))
                {
                    actualizados++;
                    if (apply) Apply(parte, fila.Dato!.Parte);
                    buenas.Add(new ImportRowDto(fila.Numero, sku, fila.Dato!.Parte.Name, "actualiza", null));
                }
                else
                {
                    creados++;
                    if (apply)
                    {
                        parte = new Part { Sku = sku };
                        Apply(parte, fila.Dato!.Parte);
                        db.Parts.Add(parte);
                        existentes[sku] = parte;
                    }

                    buenas.Add(new ImportRowDto(fila.Numero, sku, fila.Dato!.Parte.Name, "nuevo", null));
                }

                if (apply && branchId is { } destino) Bodega(destino, parte!, fila.Dato!, bodega, scope);
            }

            if (apply)
            {
                // Con la autodetección apagada hay que pedirla una vez: lo que se agregó ya
                // está marcado, pero los cambios sobre lo que ya existía —precios, mínimos,
                // saldos— no se guardarían.
                db.ChangeTracker.DetectChanges();

                // Todo o nada: con un archivo a medio cargar nadie sabe por dónde iba.
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autodeteccion;
        }

        return Importaciones.Recortado(
            apply, filas.Count, creados, actualizados, errores, buenas);
    }

    private static bool TieneDatosDeBodega(CatalogSheets.RepuestoDeExcel fila) =>
        fila.Minimo is not null || fila.Ubicacion is not null || fila.Existencia is > 0;

    /// <summary>
    /// Lo que la fila dice de la bodega: dónde está, cuándo reponer y cuánto hay.
    /// </summary>
    /// <remarks>
    /// La existencia entra como un movimiento, no como un número escrito encima del saldo: el
    /// stock no se edita, se mueve, y una carga inicial sin su entrada en el kardex deja un
    /// saldo que nadie puede explicar después.
    /// </remarks>
    private void Bodega(
        Guid branchId,
        Part parte,
        CatalogSheets.RepuestoDeExcel fila,
        Dictionary<Guid, StockItem> bodega,
        AccessScope scope)
    {
        if (!TieneDatosDeBodega(fila)) return;

        if (!bodega.TryGetValue(parte.Id, out var item))
        {
            item = new StockItem { BranchId = branchId, PartId = parte.Id, Quantity = 0 };
            db.StockItems.Add(item);

            // El repuesto nuevo todavía no tiene Id —lo pone la base al guardar—, así que la
            // existencia se cuelga de la entidad y EF resuelve la relación.
            item.Part = parte;
            bodega[parte.Id] = item;
        }

        if (fila.Minimo is { } minimo) item.MinQuantity = minimo;
        if (fila.Ubicacion is { } ubicacion) item.Location = ubicacion;

        if (fila.Existencia is not > 0) return;

        item.Quantity += fila.Existencia.Value;

        db.StockMovements.Add(new StockMovement
        {
            BranchId = branchId,
            Part = parte,
            Type = StockMovementType.In,
            Quantity = fila.Existencia.Value,
            UnitCost = fila.Parte.CostPrice > 0 ? fila.Parte.CostPrice : null,
            ResultingQuantity = item.Quantity,
            Reference = "Carga inicial",
            Notes = "Entró con la carga del catálogo desde Excel.",
            MovedAt = clock.UtcNow,
            MovedByUserId = scope.UserId
        });
    }

    /// <summary>
    /// El total que se muestra suma solo las sucursales que el usuario ve. Al técnico de la
    /// Matriz no le sirve —ni le corresponde— un total que incluya la bodega de otra sucursal.
    /// </summary>
    private IQueryable<PartDto> Project(IQueryable<Part> q, AccessScope scope) =>
        q.Select(p => new PartDto(
            p.Id,
            p.Sku,
            p.Name,
            p.Description,
            p.Brand,
            p.Category,
            p.Unit,
            p.CostPrice,
            p.SalePrice,
            p.IsActive,
            p.StockItems
                .Where(s => scope.IsOwner || scope.BranchIds.Contains(s.BranchId))
                .Sum(s => (decimal?)s.Quantity) ?? 0m));

    private static void Apply(Part part, SavePartRequest request)
    {
        part.Name = request.Name.Trim();
        part.Description = request.Description?.Trim();
        part.Brand = request.Brand?.Trim();
        part.Category = request.Category?.Trim();
        part.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "u" : request.Unit.Trim();
        part.CostPrice = request.CostPrice;
        part.SalePrice = request.SalePrice;
        part.IsActive = request.IsActive;
    }

    /// <summary>El SKU es la referencia que la gente teclea y dicta: sin espacios y en mayúscula.</summary>
    private static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    private async Task EnsureSkuIsFreeAsync(string sku, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.Parts.AnyAsync(p => p.Sku == sku && (exceptId == null || p.Id != exceptId), ct);

        if (taken) throw new ConflictException($"Ya existe un repuesto con el SKU {sku}.");
    }

    /// <summary>El inventario es cosa del taller: el cliente no ve el catálogo ni los precios de costo.</summary>
    private static void EnsureStaff(AccessScope scope)
    {
        if (scope.IsCustomer) throw new ForbiddenException("El inventario es solo para el personal del taller.");
    }
}
