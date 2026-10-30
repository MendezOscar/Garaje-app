using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Catálogo de repuestos. Es del taller entero: las existencias, que sí son por sucursal,
/// viven en <see cref="StockService"/>.
/// </summary>
public class PartService(GarajDbContext db, ITenantContext tenantContext) : IPartService
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
        Stream file, bool apply, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureManagement();

        var filas = CatalogSheets.LeerRepuestos(file);

        // Lo que ya existe, por SKU: decide si cada fila crea o actualiza, y evita una
        // consulta por renglón en un archivo de trescientos.
        var skus = filas.Where(f => f.Dato is not null)
            .Select(f => NormalizeSku(f.Dato!.Sku))
            .ToHashSet();

        var existentes = await db.Parts
            .Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, ct);

        var renglones = new List<ImportRowDto>(filas.Count);
        var vistos = new HashSet<string>();
        int creados = 0, actualizados = 0, fallidos = 0;

        foreach (var fila in filas)
        {
            var error = fila.Error;
            var sku = fila.Dato is null ? fila.Key : NormalizeSku(fila.Dato.Sku);

            // El mismo SKU dos veces en el archivo: la segunda pisaría a la primera sin que
            // nadie se entere, y casi siempre es que alguien copió un renglón y no lo cambió.
            if (error is null && !vistos.Add(sku))
                error = $"El SKU {sku} está repetido en el archivo.";

            if (error is not null)
            {
                fallidos++;
                renglones.Add(new ImportRowDto(fila.Numero, sku, fila.Name, "error", error));
                continue;
            }

            if (existentes.TryGetValue(sku, out var parte))
            {
                actualizados++;
                if (apply) Apply(parte, fila.Dato!);
                renglones.Add(new ImportRowDto(fila.Numero, sku, fila.Dato!.Name, "actualiza", null));
            }
            else
            {
                creados++;
                if (apply)
                {
                    parte = new Part { Sku = sku };
                    Apply(parte, fila.Dato!);
                    db.Parts.Add(parte);
                    existentes[sku] = parte;
                }

                renglones.Add(new ImportRowDto(fila.Numero, sku, fila.Dato!.Name, "nuevo", null));
            }
        }

        // Todo o nada: con un archivo a medio cargar nadie sabe por dónde iba.
        if (apply) await db.SaveChangesAsync(ct);

        return new ImportResultDto(
            apply, filas.Count, creados, actualizados, fallidos, renglones);
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
