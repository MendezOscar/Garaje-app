using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Customers;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

public class CustomerService(GarajDbContext db, ITenantContext tenantContext) : ICustomerService
{
    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (!query.IncludeInactive)
            q = q.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var plate = PlateFormatter.Normalize(term);

            // Una sola búsqueda para los tres identificadores que el mostrador usa de verdad:
            // el nombre, el teléfono con el que llaman y la placa que traen a la vista.
            q = q.Where(c =>
                EF.Functions.ILike(c.FullName, $"%{term}%")
                || c.Phone.Contains(term)
                || c.Vehicles.Any(v => v.Plate != null && v.Plate.Contains(plate)));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(c => c.FullName)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(c => new CustomerDto(
                c.Id, c.FullName, c.Phone, c.Email, c.DocumentId, c.Address, c.Notes, c.IsActive,
                c.Vehicles.Count(v => v.IsActive)))
            .ToListAsync(ct);

        return new PagedResult<CustomerDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        return await Scoped(scope)
            .Where(c => c.Id == id)
            .Select(c => new CustomerDto(
                c.Id, c.FullName, c.Phone, c.Email, c.DocumentId, c.Address, c.Notes, c.IsActive,
                c.Vehicles.Count(v => v.IsActive)))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("El cliente no existe.");
    }

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var customer = new Customer();
        Apply(customer, request);

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        return Map(customer, 0);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("El cliente no existe.");

        Apply(customer, request);
        await db.SaveChangesAsync(ct);

        var vehicleCount = await db.Vehicles.CountAsync(v => v.CustomerId == id && v.IsActive, ct);
        return Map(customer, vehicleCount);
    }

    /// <summary>Un Cliente solo puede verse a sí mismo; el personal del taller ve todo el padrón.</summary>
    private IQueryable<Customer> Scoped(AccessScope scope)
    {
        var q = db.Customers.AsNoTracking();
        return scope.IsCustomer ? q.Where(c => c.Id == scope.CustomerId) : q;
    }

    private static void Apply(Customer customer, SaveCustomerRequest request)
    {
        customer.FullName = request.FullName.Trim();
        customer.Phone = PhoneFormatter.Normalize(request.Phone);
        customer.Email = request.Email?.Trim();
        customer.DocumentId = request.DocumentId?.Trim();
        customer.Address = request.Address?.Trim();
        customer.Notes = request.Notes?.Trim();
        customer.IsActive = request.IsActive;
    }

    private static CustomerDto Map(Customer c, int vehicleCount) =>
        new(c.Id, c.FullName, c.Phone, c.Email, c.DocumentId, c.Address, c.Notes, c.IsActive, vehicleCount);
}
