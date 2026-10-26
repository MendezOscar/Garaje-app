using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Customers;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Los equipos de los clientes —computadoras, teléfonos, aires— en los talleres que reparan
/// equipos. Es el espejo de <see cref="VehicleService"/>: mismas reglas de quién puede dar de
/// alta y editar, sobre otra ficha.
/// </summary>
public class EquipmentService(GarajDbContext db, ITenantContext tenantContext) : IEquipmentService
{
    public async Task<PagedResult<EquipmentDto>> ListAsync(
        EquipmentQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (!query.IncludeInactive) q = q.Where(e => e.IsActive);
        if (query.CustomerId is { } customerId) q = q.Where(e => e.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // El mostrador busca por el cliente; la serie y la etiqueta son el atajo de
            // bodega, para el aparato que ya está adentro y hay que saber de quién es.
            var term = query.Search.Trim();

            q = q.Where(e =>
                EF.Functions.ILike(e.Customer.FullName, $"%{term}%")
                || e.Customer.Phone.Contains(term)
                || EF.Functions.ILike(e.Brand, $"%{term}%")
                || EF.Functions.ILike(e.Model, $"%{term}%")
                || (e.SerialNumber != null && EF.Functions.ILike(e.SerialNumber, $"%{term}%"))
                || (e.Tag != null && EF.Functions.ILike(e.Tag, $"%{term}%")));
        }

        var total = await q.CountAsync(ct);

        var items = await Project(q.OrderBy(e => e.Customer.FullName).ThenBy(e => e.Brand))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<EquipmentDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<EquipmentDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        return await Project(Scoped(scope).Where(e => e.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("El equipo no existe.");
    }

    public async Task<EquipmentDto> CreateAsync(
        SaveEquipmentRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        await EnsureWorksWithEquipmentAsync(ct);

        // Igual que con los vehículos: el cliente da de alta los suyos desde la app, y el
        // taller los de cualquiera, porque el aparato llega al mostrador sin estar registrado.
        if (scope.IsCustomer && request.CustomerId != scope.CustomerId)
            throw new ForbiddenException("Solo puede registrar equipos a su nombre.");

        if (!await db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct))
            throw new NotFoundException("El cliente no existe.");

        var equipment = new Equipment { CustomerId = request.CustomerId };
        await ApplyAsync(equipment, request, ct);

        db.Equipment.Add(equipment);
        await db.SaveChangesAsync(ct);

        return await GetAsync(equipment.Id, ct);
    }

    public async Task<EquipmentDto> UpdateAsync(
        Guid id, SaveEquipmentRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var equipment = await Scoped(scope).AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("El equipo no existe.");

        if (scope.IsTechnician) throw new ForbiddenException("Un técnico no puede editar equipos.");

        await ApplyAsync(equipment, request, ct);
        await db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<EquipmentTypeDto>> TypesAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var q = db.EquipmentTypes.AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);

        return await q.OrderBy(t => t.Name)
            .Select(t => new EquipmentTypeDto(t.Id, t.Name, t.Icon, t.IsActive))
            .ToListAsync(ct);
    }

    public async Task<EquipmentTypeDto> CreateTypeAsync(
        SaveEquipmentTypeRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureManagement();
        await EnsureWorksWithEquipmentAsync(ct);

        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, null, ct);

        var type = new EquipmentType
        {
            Name = name,
            Icon = request.Icon?.Trim(),
            IsActive = request.IsActive,
        };

        db.EquipmentTypes.Add(type);
        await db.SaveChangesAsync(ct);

        return new EquipmentTypeDto(type.Id, type.Name, type.Icon, type.IsActive);
    }

    public async Task<EquipmentTypeDto> UpdateTypeAsync(
        Guid id, SaveEquipmentTypeRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureManagement();

        var type = await db.EquipmentTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("La categoría no existe.");

        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, id, ct);

        type.Name = name;
        type.Icon = request.Icon?.Trim();
        type.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        return new EquipmentTypeDto(type.Id, type.Name, type.Icon, type.IsActive);
    }

    /// <summary>Un Cliente solo ve los equipos que están a su nombre.</summary>
    private IQueryable<Equipment> Scoped(AccessScope scope)
    {
        var q = db.Equipment.AsNoTracking();
        return scope.IsCustomer ? q.Where(e => e.CustomerId == scope.CustomerId) : q;
    }

    private static IQueryable<EquipmentDto> Project(IQueryable<Equipment> q) =>
        q.Select(e => new EquipmentDto(
            e.Id, e.CustomerId, e.Customer.FullName, e.EquipmentTypeId, e.EquipmentType.Name,
            e.Brand, e.Model, e.SerialNumber, e.Tag, e.Color, e.Accessories, e.Attributes,
            e.Notes, e.IsActive));

    private async Task ApplyAsync(
        Equipment equipment, SaveEquipmentRequest request, CancellationToken ct)
    {
        if (!await db.EquipmentTypes.AnyAsync(t => t.Id == request.EquipmentTypeId, ct))
            throw new NotFoundException("La categoría de equipo no existe.");

        equipment.EquipmentTypeId = request.EquipmentTypeId;
        equipment.Brand = request.Brand.Trim();
        equipment.Model = request.Model.Trim();

        // La serie se guarda en mayúscula pero con sus guiones: a diferencia de una placa, es
        // un dato que el cliente compara letra por letra con la etiqueta del aparato.
        equipment.SerialNumber = Clean(request.SerialNumber)?.ToUpperInvariant();
        equipment.Tag = Clean(request.Tag)?.ToUpperInvariant();
        equipment.Color = Clean(request.Color);
        equipment.Accessories = Clean(request.Accessories);
        equipment.Attributes = Clean(request.Attributes);
        equipment.Notes = Clean(request.Notes);
        equipment.IsActive = request.IsActive;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task EnsureNameIsFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.EquipmentTypes
            .AnyAsync(t => t.Name.ToUpper() == name.ToUpper() && (exceptId == null || t.Id != exceptId), ct);

        if (taken) throw new ConflictException($"Ya existe una categoría llamada {name}.");
    }

    /// <summary>
    /// Un taller de vehículos no tiene equipos, y pedirlos no es un permiso que le falte sino
    /// algo que no existe para él: 404, no 403.
    /// </summary>
    private async Task EnsureWorksWithEquipmentAsync(CancellationToken ct)
    {
        var worksWith = await db.Tenants
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.WorksWith)
            .FirstOrDefaultAsync(ct);

        if (worksWith != WorkshopKind.Equipment)
            throw new NotFoundException("Este taller no trabaja con equipos.");
    }
}
