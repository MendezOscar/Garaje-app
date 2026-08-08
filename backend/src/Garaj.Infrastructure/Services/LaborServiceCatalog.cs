using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Quotes;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Catálogo de mano de obra. Es lo que permite cotizar el trabajo sin teclear el precio cada
/// vez, y lo que separa el ingreso por mano de obra del de repuestos en los reportes.
/// </summary>
public class LaborServiceCatalog(GarajDbContext db, ITenantContext tenantContext) : ILaborServiceCatalog
{
    public async Task<IReadOnlyList<LaborServiceDto>> ListAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer)
            throw new ForbiddenException("El catálogo de mano de obra es solo para el taller.");

        var q = db.LaborServices.AsNoTracking();
        if (!includeInactive) q = q.Where(s => s.IsActive);

        return await Project(q.OrderBy(s => s.Name)).ToListAsync(ct);
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
