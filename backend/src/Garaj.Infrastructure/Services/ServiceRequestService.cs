using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Notifications;
using Garaj.Application.ServiceRequests;
using Garaj.Application.WorkOrders;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

public class ServiceRequestService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IWorkOrderService workOrders,
    INotificationPublisher notifications) : IServiceRequestService
{
    public async Task<PagedResult<ServiceRequestDto>> ListAsync(
        ServiceRequestQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (query.BranchId is { } branchId) q = q.Where(r => r.BranchId == branchId);
        if (query.Status is { } status) q = q.Where(r => r.Status == status);
        if (query.VehicleId is { } vehicleId) q = q.Where(r => r.VehicleId == vehicleId);
        if (query.From is { } from) q = q.Where(r => r.CreatedAt >= from);
        if (query.To is { } to) q = q.Where(r => r.CreatedAt <= to);

        var total = await q.CountAsync(ct);

        // Se ordena sobre la entidad y se proyecta después. Al revés, EF tiene que traducir el
        // OrderBy sobre el DTO ya construido, y con los joins que mete el alcance del Cliente
        // no lo consigue: la bandeja del Cliente respondía 500.
        var ordered = q
            // Los pendientes primero: es la bandeja de entrada del Dueño, no un histórico.
            .OrderBy(r => r.Status == ServiceRequestStatus.Pending ? 0 : 1)
            .ThenByDescending(r => r.CreatedAt);

        var items = await Project(ordered)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ServiceRequestDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<ServiceRequestDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        return await Project(Scoped(scope).Where(r => r.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("El requerimiento no existe.");
    }

    public async Task<ServiceRequestDto> CreateAsync(
        CreateServiceRequestRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        // El taller también los registra: en el mostrador el vehículo llega antes que el
        // cliente a la aplicación, y las primeras veces nadie la trae instalada. El técnico
        // solo puede hacerlo en las sucursales que tiene asignadas.
        if (scope.IsTechnician) scope.EnsureBranchAllowed(request.BranchId);

        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct)
            ?? throw new NotFoundException("El vehículo no existe.");

        // Sin esto, un cliente podría abrir un requerimiento sobre el vehículo de otro
        // pasando un id ajeno.
        if (scope.IsCustomer && vehicle.CustomerId != scope.CustomerId)
            throw new NotFoundException("El vehículo no existe.");

        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId && b.IsActive, ct))
            throw new NotFoundException("La sucursal no existe.");

        var serviceRequest = new ServiceRequest
        {
            BranchId = request.BranchId,
            VehicleId = vehicle.Id,
            Description = request.Description.Trim(),
            ReportedSymptoms = request.ReportedSymptoms?.Trim(),
            PreferredDate = request.PreferredDate,
            Mileage = request.Mileage,
            Status = ServiceRequestStatus.Pending
        };

        db.ServiceRequests.Add(serviceRequest);

        if (request.Mileage is { } mileage && mileage > (vehicle.Mileage ?? 0))
            vehicle.Mileage = mileage;

        await db.SaveChangesAsync(ct);

        // Se avisa salvo cuando lo registró el propio Dueño: avisarle de su propia captura
        // es ruido que enseña a ignorar la campana. Lo que abre un técnico sí se avisa,
        // porque el Dueño es quien tiene que aprobarlo.
        if (!scope.IsOwner)
            await notifications.NotifyOwnersAsync(serviceRequest.TenantId, new NotificationDraft(
                NotificationType.ServiceRequestCreated,
                scope.IsCustomer ? "Nuevo requerimiento de un cliente" : "Nuevo requerimiento en recepción",
                $"{vehicle.Brand} {vehicle.Model}"
                + (vehicle.Plate is null ? "" : $" · {vehicle.Plate}")
                + $": {Summarize(serviceRequest.Description)}",
                ServiceRequestId: serviceRequest.Id), ct);

        return await GetAsync(serviceRequest.Id, ct);
    }

    /// <summary>El cuerpo del aviso es una línea en una lista, no el motivo completo.</summary>
    private static string Summarize(string text) =>
        text.Length <= 120 ? text : text[..117].TrimEnd() + "…";

    public async Task<Guid> ApproveAsync(
        Guid id, ApproveServiceRequestRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var serviceRequest = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("El requerimiento no existe.");

        // Aprobar dos veces crearía dos órdenes para el mismo ingreso.
        if (serviceRequest.Status == ServiceRequestStatus.Converted)
            throw new ConflictException("El requerimiento ya se convirtió en orden de trabajo.");

        if (serviceRequest.Status == ServiceRequestStatus.Rejected)
            throw new ConflictException("El requerimiento fue rechazado y no puede aprobarse.");

        var order = await workOrders.CreateAsync(
            new CreateWorkOrderRequest(
                serviceRequest.BranchId,
                serviceRequest.VehicleId,
                serviceRequest.Description,
                request.AssignedTechnicianId,
                serviceRequest.Mileage,
                request.PromisedAt),
            ct);

        serviceRequest.Status = ServiceRequestStatus.Converted;
        serviceRequest.WorkOrderId = order.Id;

        var created = await db.WorkOrders.FirstAsync(w => w.Id == order.Id, ct);
        created.ServiceRequestId = serviceRequest.Id;

        await db.SaveChangesAsync(ct);

        return order.Id;
    }

    public async Task<ServiceRequestDto> RejectAsync(
        Guid id, RejectServiceRequestRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var serviceRequest = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("El requerimiento no existe.");

        if (serviceRequest.Status == ServiceRequestStatus.Converted)
            throw new ConflictException("El requerimiento ya se convirtió en orden de trabajo.");

        serviceRequest.Status = ServiceRequestStatus.Rejected;
        serviceRequest.RejectionReason = request.Reason.Trim();

        await db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// El Cliente solo ve los requerimientos de sus vehículos. El Técnico, los de las
    /// sucursales donde trabaja: necesita ver lo que él mismo acaba de recibir en el
    /// mostrador y qué hay en cola, pero no la bandeja de las otras sucursales.
    /// </summary>
    private IQueryable<ServiceRequest> Scoped(AccessScope scope)
    {
        var q = db.ServiceRequests.AsNoTracking();

        if (scope.IsCustomer)
            return q.Where(r => r.Vehicle.CustomerId == scope.CustomerId);

        if (scope.IsTechnician)
        {
            // Materializado: EF no traduce Contains sobre IReadOnlyCollection.
            var branchIds = scope.BranchIds.ToList();
            return q.Where(r => branchIds.Contains(r.BranchId));
        }

        return q;
    }

    private IQueryable<ServiceRequestDto> Project(IQueryable<ServiceRequest> q) =>
        q.Select(r => new ServiceRequestDto(
            r.Id,
            r.BranchId,
            r.Branch.Name,
            r.VehicleId,
            r.Vehicle.Brand + " " + r.Vehicle.Model + (r.Vehicle.Plate == null ? "" : " · " + r.Vehicle.Plate),
            r.Vehicle.CustomerId,
            r.Vehicle.Customer.FullName,
            r.Vehicle.Customer.Phone,
            r.Description,
            r.ReportedSymptoms,
            r.Status,
            r.PreferredDate,
            r.Mileage,
            r.RejectionReason,
            r.WorkOrderId,
            // Sin navegación a WorkOrder (la FK es opcional y unidireccional), así que el
            // número se trae por subconsulta.
            db.WorkOrders.Where(w => w.Id == r.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
            r.CreatedAt));
}
