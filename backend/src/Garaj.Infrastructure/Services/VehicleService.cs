using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Customers;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

public class VehicleService(GarajDbContext db, ITenantContext tenantContext) : IVehicleService
{
    public async Task<PagedResult<VehicleDto>> ListAsync(VehicleQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (!query.IncludeInactive)
            q = q.Where(v => v.IsActive);

        if (query.CustomerId is { } customerId)
            q = q.Where(v => v.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var plate = PlateFormatter.Normalize(term);

            q = q.Where(v =>
                (v.Plate != null && v.Plate.Contains(plate))
                || EF.Functions.ILike(v.Brand, $"%{term}%")
                || EF.Functions.ILike(v.Model, $"%{term}%")
                || EF.Functions.ILike(v.Customer.FullName, $"%{term}%"));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(v => v.Customer.FullName).ThenBy(v => v.Brand)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(v => new VehicleDto(
                v.Id, v.CustomerId, v.Customer.FullName, v.Type, v.Brand, v.Model, v.Year,
                v.Plate, v.Vin, v.Color, v.Mileage, v.Notes, v.IsActive))
            .ToListAsync(ct);

        return new PagedResult<VehicleDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<VehicleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        return await Scoped(scope)
            .Where(v => v.Id == id)
            .Select(v => new VehicleDto(
                v.Id, v.CustomerId, v.Customer.FullName, v.Type, v.Brand, v.Model, v.Year,
                v.Plate, v.Vin, v.Color, v.Mileage, v.Notes, v.IsActive))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("El vehículo no existe.");
    }

    public async Task<VehicleDto> CreateAsync(SaveVehicleRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        // El Cliente puede registrar sus propios vehículos desde la app; el Dueño, los de
        // cualquiera. El Técnico no crea vehículos.
        if (scope.IsTechnician)
            throw new ForbiddenException("Un técnico no puede registrar vehículos.");

        if (scope.IsCustomer && request.CustomerId != scope.CustomerId)
            throw new ForbiddenException("Solo puede registrar vehículos a su nombre.");

        var customerExists = await db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists) throw new NotFoundException("El cliente no existe.");

        var vehicle = new Vehicle { CustomerId = request.CustomerId };
        await ApplyAsync(vehicle, request, ct);

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        return await GetAsync(vehicle.Id, ct);
    }

    public async Task<VehicleDto> UpdateAsync(Guid id, SaveVehicleRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var vehicle = await Scoped(scope).AsTracking().FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NotFoundException("El vehículo no existe.");

        if (scope.IsTechnician)
            throw new ForbiddenException("Un técnico no puede editar vehículos.");

        await ApplyAsync(vehicle, request, ct);
        await db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /// <summary>Un Cliente solo ve los vehículos que están a su nombre.</summary>
    private IQueryable<Vehicle> Scoped(AccessScope scope)
    {
        var q = db.Vehicles.AsNoTracking();
        return scope.IsCustomer ? q.Where(v => v.CustomerId == scope.CustomerId) : q;
    }

    private async Task ApplyAsync(Vehicle vehicle, SaveVehicleRequest request, CancellationToken ct)
    {
        var plate = PlateFormatter.Normalize(request.Plate);
        plate = string.IsNullOrEmpty(plate) ? null : plate;

        if (plate is not null)
        {
            var duplicate = await db.Vehicles
                .AnyAsync(v => v.Plate == plate && v.Id != vehicle.Id, ct);

            if (duplicate)
                throw new ConflictException($"Ya hay un vehículo registrado con la placa {plate}.");
        }

        vehicle.Type = request.Type;
        vehicle.Brand = request.Brand.Trim();
        vehicle.Model = request.Model.Trim();
        vehicle.Year = request.Year;
        vehicle.Plate = plate;
        vehicle.Vin = request.Vin?.Trim().ToUpperInvariant();
        vehicle.Color = request.Color?.Trim();
        vehicle.Notes = request.Notes?.Trim();
        vehicle.IsActive = request.IsActive;

        // El kilometraje solo avanza: si llega uno menor es un error de tecleo, y aceptarlo
        // rompería el historial de mantenimientos del vehículo.
        if (request.Mileage is { } mileage && mileage > (vehicle.Mileage ?? 0))
            vehicle.Mileage = mileage;
    }
}
