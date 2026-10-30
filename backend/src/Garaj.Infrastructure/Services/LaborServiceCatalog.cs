using FluentValidation;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Quotes;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Catálogo de mano de obra. Es lo que permite cotizar el trabajo sin teclear el precio cada
/// vez, y lo que separa el ingreso por mano de obra del de repuestos en los reportes.
/// </summary>
public class LaborServiceCatalog(
    GarajDbContext db,
    ITenantContext tenantContext,
    IValidator<SaveLaborServiceRequest> validador) : ILaborServiceCatalog
{
    public async Task<IReadOnlyList<LaborServiceDto>> ListAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer)
            throw new ForbiddenException("El catálogo de mano de obra es solo para el taller.");

        var q = db.LaborServices.AsNoTracking();
        if (!includeInactive) q = q.Where(s => s.IsActive);

        var items = await Project(q.OrderBy(s => s.Name)).ToListAsync(ct);

        // El técnico elige qué trabajo es; cuando el taller no quiere que vea precios, elige
        // por el nombre. Las tarifas se van aquí, en el servidor, no en la pantalla.
        if (scope.IsTechnician
            && await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantContext.TenantId)
                .Select(t => (bool?)t.TechniciansSeePrices)
                .FirstOrDefaultAsync(ct) == false)
        {
            return items
                .Select(s => s with { HourlyRate = 0, FixedPrice = 0, Price = 0 })
                .ToList();
        }

        return items;
    }

    public async Task<LaborServiceDto> CreateAsync(
        SaveLaborServiceRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeIsFreeAsync(code, null, ct);

        var service = new LaborService { Code = code };
        Apply(service, request);

        db.LaborServices.Add(service);
        await db.SaveChangesAsync(ct);

        return await GetAsync(service.Id, ct);
    }

    public async Task<LaborServiceDto> UpdateAsync(
        Guid id, SaveLaborServiceRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var service = await db.LaborServices.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("El servicio no existe.");

        var code = request.Code.Trim().ToUpperInvariant();
        await EnsureCodeIsFreeAsync(code, id, ct);

        service.Code = code;
        Apply(service, request);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------- Carga desde Excel ----------

    public byte[] ImportTemplate() => CatalogSheets.PlantillaDeManoDeObra();

    public async Task<ImportResultDto> ImportAsync(
        Stream file, bool apply, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var filas = CatalogSheets.LeerManoDeObra(file);
        Importaciones.EnsureCabe(filas.Count);

        var codigos = filas.Where(f => f.Dato is not null)
            .Select(f => Normalizar(f.Dato!.Code))
            .ToHashSet();

        var existentes = await db.LaborServices
            .Where(s => codigos.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, ct);

        var errores = new List<ImportRowDto>();
        var buenas = new List<ImportRowDto>();
        var vistos = new HashSet<string>();
        int creados = 0, actualizados = 0;

        // Igual que en repuestos: con la autodetección encendida, EF revisa en cada Add todo
        // lo que lleva en memoria y el tiempo crece con el cuadrado de las filas.
        var autodeteccion = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            foreach (var fila in filas)
            {
                var error = fila.Error;
                var codigo = fila.Dato is null ? fila.Key : Normalizar(fila.Dato.Code);

                // Las mismas reglas que la pantalla de Mano de obra: largos de columna y la
                // regla que hace que el precio exista. Sin esto, una celda larguísima revienta
                // en Npgsql a mitad del archivo y nadie sabe qué fila fue.
                if (error is null && validador.Validate(fila.Dato!) is { IsValid: false } malo)
                    error = malo.Errors[0].ErrorMessage;

                // El mismo código dos veces: la segunda pisaría a la primera sin que nadie se
                // entere, y casi siempre es que alguien copió un renglón y no lo cambió.
                if (error is null && !vistos.Add(codigo))
                    error = $"El código {codigo} está repetido en el archivo.";

                if (error is not null)
                {
                    errores.Add(new ImportRowDto(fila.Numero, codigo, fila.Name, "error", error));
                    continue;
                }

                if (existentes.TryGetValue(codigo, out var servicio))
                {
                    actualizados++;
                    if (apply) Apply(servicio, fila.Dato!);
                    buenas.Add(new ImportRowDto(fila.Numero, codigo, fila.Dato!.Name, "actualiza", null));
                }
                else
                {
                    creados++;
                    if (apply)
                    {
                        servicio = new LaborService { Code = codigo };
                        Apply(servicio, fila.Dato!);
                        db.LaborServices.Add(servicio);
                        existentes[codigo] = servicio;
                    }

                    buenas.Add(new ImportRowDto(fila.Numero, codigo, fila.Dato!.Name, "nuevo", null));
                }
            }

            if (apply)
            {
                // Con la autodetección apagada hay que pedirla una vez: los cambios sobre lo
                // que ya existía —precios, tarifas— no se guardarían.
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

    /// <summary>El código es la referencia que la gente teclea: sin espacios y en mayúscula.</summary>
    private static string Normalizar(string code) => code.Trim().ToUpperInvariant();

    private async Task<LaborServiceDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.LaborServices.AsNoTracking().Where(s => s.Id == id)).FirstAsync(ct);

    /// <summary>
    /// <c>Price</c> resuelve aquí la regla de precio fijo vs horas × tarifa, para que ni el
    /// web ni el móvil tengan que repetirla.
    /// </summary>
    private static IQueryable<LaborServiceDto> Project(IQueryable<LaborService> q) =>
        q.Select(s => new LaborServiceDto(
            s.Id, s.Code, s.Name, s.Description, s.Category,
            s.StandardHours, s.HourlyRate, s.IsFixedPrice, s.FixedPrice, s.IsActive,
            s.IsFixedPrice ? s.FixedPrice : s.StandardHours * s.HourlyRate));

    private static void Apply(LaborService service, SaveLaborServiceRequest request)
    {
        service.Name = request.Name.Trim();
        service.Description = request.Description?.Trim();
        service.Category = request.Category?.Trim();
        service.StandardHours = request.StandardHours;
        service.HourlyRate = request.HourlyRate;
        service.IsFixedPrice = request.IsFixedPrice;
        service.FixedPrice = request.FixedPrice;
        service.IsActive = request.IsActive;
    }

    private async Task EnsureCodeIsFreeAsync(string code, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.LaborServices
            .AnyAsync(s => s.Code == code && (exceptId == null || s.Id != exceptId), ct);

        if (taken) throw new ConflictException($"Ya existe un servicio con el código {code}.");
    }
}
