using Garaj.Application.Abstractions;
using Garaj.Application.Branches;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

public class BranchService(GarajDbContext db, ITenantContext tenantContext) : IBranchService
{
    public async Task<IReadOnlyList<BranchDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var query = db.Branches.AsNoTracking();

        if (!includeInactive)
            query = query.Where(b => b.IsActive);

        // El Técnico solo ve las suyas; el Cliente ve todas las del taller porque tiene que
        // poder elegir dónde deja el vehículo.
        if (scope.IsTechnician)
            query = query.Where(b => scope.BranchIds.Contains(b.Id));

        return await query
            .OrderBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Name, b.Code, b.Address, b.City, b.Phone, b.IsActive))
            .ToListAsync(ct);
    }

    public async Task<BranchDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("La sucursal no existe.");

        return Map(branch);
    }

    public async Task<BranchDto> CreateAsync(SaveBranchRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var code = Normalize(request.Code);
        await EnsureCodeIsFreeAsync(code, null, ct);

        if (request.IsActive) await EnsureCabeOtraAsync(null, ct);

        var branch = new Branch
        {
            Name = request.Name.Trim(),
            Code = code,
            Address = request.Address?.Trim(),
            City = request.City?.Trim(),
            Phone = request.Phone?.Trim(),
            IsActive = request.IsActive
        };

        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        return Map(branch);
    }

    public async Task<BranchDto> UpdateAsync(Guid id, SaveBranchRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("La sucursal no existe.");

        var code = Normalize(request.Code);
        await EnsureCodeIsFreeAsync(code, id, ct);

        // Volver a encender una dada de baja ocupa lugar igual que crearla: sin esto, el
        // tope se esquiva apagando una y encendiendo otra.
        if (request.IsActive && !branch.IsActive) await EnsureCabeOtraAsync(id, ct);

        branch.Name = request.Name.Trim();
        branch.Code = code;
        branch.Address = request.Address?.Trim();
        branch.City = request.City?.Trim();
        branch.Phone = request.Phone?.Trim();
        branch.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        return Map(branch);
    }

    /// <summary>
    /// Si al taller le cabe otra sucursal activa.
    /// </summary>
    /// <remarks>
    /// La cuota del plan Básico es por una sucursal, y la segunda se cobra aparte. El tope
    /// vive en el taller y no en el plan porque es lo que se negocia: al que paga por la
    /// segunda se le sube el número sin moverlo de plan.
    ///
    /// Se cuentan solo las activas, así que cerrar un local y abrir otro no pide permiso.
    /// El mensaje dice el número y qué hacer, porque el Dueño no puede resolverlo solo.
    /// </remarks>
    private async Task EnsureCabeOtraAsync(Guid? exceptId, CancellationToken ct)
    {
        var tope = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.BranchLimit)
            .FirstOrDefaultAsync(ct);

        if (tope is not { } limite) return;

        var activas = await db.Branches.CountAsync(b => b.IsActive && b.Id != exceptId, ct);

        if (activas >= limite)
            throw new ConflictException(
                limite == 1
                    ? "Su plan incluye una sucursal. Para abrir otra, escríbanos: se cobra aparte."
                    : $"Su plan incluye {limite} sucursales y ya tiene {activas}. " +
                      "Para abrir otra, escríbanos: se cobra aparte.");
    }

    /// <summary>
    /// El código prefija los correlativos de órdenes, cotizaciones y ventas, así que dos
    /// sucursales con el mismo código generarían números repetidos.
    /// </summary>
    private async Task EnsureCodeIsFreeAsync(string? code, Guid? exceptId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code)) return;

        var taken = await db.Branches.AnyAsync(b => b.Code == code && b.Id != exceptId, ct);
        if (taken) throw new ConflictException($"Ya existe una sucursal con el código {code}.");
    }

    private static string? Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private static BranchDto Map(Branch b) =>
        new(b.Id, b.Name, b.Code, b.Address, b.City, b.Phone, b.IsActive);
}
