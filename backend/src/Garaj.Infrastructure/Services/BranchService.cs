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
