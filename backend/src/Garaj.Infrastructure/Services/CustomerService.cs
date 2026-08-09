using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Customers;
using Garaj.Application.Users;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

public class CustomerService(
    GarajDbContext db, ITenantContext tenantContext, IUserService users) : ICustomerService
{
    /// <summary>
    /// Le da acceso a la app a un cliente del padrón. Es opcional a propósito: la mayoría de
    /// los clientes de un taller nunca lo va a usar, y crearle un usuario a cada uno llenaría
    /// la lista de accesos muertos. El usuario queda con perfil Cliente y solo ve lo suyo.
    /// </summary>
    public async Task<CustomerDto> GrantAppAccessAsync(
        Guid id, GrantAppAccessRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("El cliente no existe.");

        // El alta la valida IUserService: correo repetido, contraseña débil y el enlace con
        // el cliente. Aquí solo se decide a quién se le abre.
        await users.CreateAsync(new CreateUserRequest(
            request.Email,
            customer.FullName,
            AppRoles.Customer,
            request.Password,
            null,
            customer.Id), ct);

        return await GetAsync(id, ct);
    }

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
                c.Vehicles.Count(v => v.IsActive),
                c.AppUserId != null,
                db.Users.Where(u => u.Id == c.AppUserId).Select(u => u.Email).FirstOrDefault()))
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
                c.Vehicles.Count(v => v.IsActive),
                c.AppUserId != null,
                db.Users.Where(u => u.Id == c.AppUserId).Select(u => u.Email).FirstOrDefault()))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("El cliente no existe.");
    }

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default)
    {
        // Lo registra cualquiera del taller, no solo el Dueño: quien recibe la moto en el
        // mostrador es quien tiene delante al cliente. Editarlo sigue siendo del Dueño,
        // que es donde se pueden estropear datos de facturación ya emitidos.
        if (AccessScope.From(tenantContext).IsCustomer)
            throw new ForbiddenException("Un cliente no puede registrar clientes.");

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
        var email = await db.Users.Where(u => u.Id == customer.AppUserId)
            .Select(u => u.Email).FirstOrDefaultAsync(ct);

        return Map(customer, vehicleCount, email);
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

    private static CustomerDto Map(Customer c, int vehicleCount, string? appUserEmail = null) =>
        new(c.Id, c.FullName, c.Phone, c.Email, c.DocumentId, c.Address, c.Notes, c.IsActive,
            vehicleCount, c.AppUserId != null, appUserEmail);
}
