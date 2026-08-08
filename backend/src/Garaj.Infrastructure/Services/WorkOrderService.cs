using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Application.Notifications;
using Garaj.Application.WorkOrders;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Domain.Rules;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

public class WorkOrderService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    StockService stock,
    INotificationPublisher notifications) : IWorkOrderService
{
    public async Task<PagedResult<WorkOrderListItemDto>> ListAsync(
        WorkOrderQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (query.BranchId is { } branchId) q = q.Where(w => w.BranchId == branchId);
        if (query.Status is { } status) q = q.Where(w => w.Status == status);
        if (query.TechnicianId is { } techId) q = q.Where(w => w.AssignedTechnicianId == techId);
        if (query.VehicleId is { } vehicleId) q = q.Where(w => w.VehicleId == vehicleId);

        if (query.OnlyOpen)
            q = q.Where(w => w.Status != WorkOrderStatus.Delivered && w.Status != WorkOrderStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var plate = PlateFormatter.Normalize(term);

            q = q.Where(w =>
                EF.Functions.ILike(w.Number, $"%{term}%")
                || (w.Vehicle.Plate != null && w.Vehicle.Plate.Contains(plate))
                || EF.Functions.ILike(w.Vehicle.Customer.FullName, $"%{term}%"));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(w => w.OpenedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(w => new WorkOrderListItemDto(
                w.Id,
                w.Number,
                w.BranchId,
                w.Branch.Name,
                w.Status,
                w.VehicleId,
                w.Vehicle.Brand + " " + w.Vehicle.Model,
                w.Vehicle.Type,
                w.Vehicle.Plate,
                w.Vehicle.CustomerId,
                w.Vehicle.Customer.FullName,
                w.Vehicle.Customer.Phone,
                w.AssignedTechnicianId,
                // Sin FK a AppUser (vive en Infrastructure), así que se resuelve por subconsulta.
                db.Users.Where(u => u.Id == w.AssignedTechnicianId).Select(u => u.FullName).FirstOrDefault(),
                w.Description,
                w.OpenedAt,
                w.PromisedAt,
                w.Tasks.Count,
                w.Tasks.Count(t => t.IsDone)))
            .ToListAsync(ct);

        return new PagedResult<WorkOrderListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<WorkOrderDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var order = await Scoped(scope)
            .Include(w => w.Branch)
            .Include(w => w.Vehicle).ThenInclude(v => v.Customer)
            .Include(w => w.Tasks)
            .Include(w => w.StatusHistory)
            .FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        return await MapDetailAsync(order, scope, ct);
    }

    public async Task<WorkOrderDetailDto> CreateAsync(
        CreateWorkOrderRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (!scope.IsOwner)
            throw new ForbiddenException("Solo el Dueño puede abrir órdenes de trabajo.");

        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct)
            ?? throw new NotFoundException("El vehículo no existe.");

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId, ct)
            ?? throw new NotFoundException("La sucursal no existe.");

        if (request.AssignedTechnicianId is { } technicianId)
            await EnsureTechnicianAsync(technicianId, branch.Id, ct);

        var order = new WorkOrder
        {
            BranchId = branch.Id,
            VehicleId = vehicle.Id,
            Number = await NextNumberAsync(branch, ct),
            Status = WorkOrderStatus.Received,
            AssignedTechnicianId = request.AssignedTechnicianId,
            Description = request.Description.Trim(),
            MileageIn = request.MileageIn,
            OpenedAt = clock.UtcNow,
            PromisedAt = request.PromisedAt
        };

        db.WorkOrders.Add(order);

        db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
        {
            WorkOrderId = order.Id,
            FromStatus = null,
            ToStatus = WorkOrderStatus.Received,
            ChangedAt = order.OpenedAt,
            ChangedByUserId = scope.UserId,
            Note = $"Vehículo recibido en {branch.Name}."
        });

        if (request.MileageIn is { } mileage && mileage > (vehicle.Mileage ?? 0))
            vehicle.Mileage = mileage;

        await db.SaveChangesAsync(ct);

        if (order.AssignedTechnicianId is { } technician)
            await notifications.NotifyUserAsync(order.TenantId, technician, new NotificationDraft(
                NotificationType.WorkOrderAssigned,
                $"Nueva orden asignada · {order.Number}",
                await VehicleLabelAsync(order.VehicleId, ct),
                WorkOrderId: order.Id), ct);

        return await GetAsync(order.Id, ct);
    }

    public async Task<WorkOrderDetailDto> UpdateAsync(
        Guid id, UpdateWorkOrderRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var order = await FindEditableAsync(id, scope, ct);

        if (scope.IsCustomer)
            throw new ForbiddenException("Un cliente no puede editar la orden de trabajo.");

        order.Description = request.Description.Trim();
        order.Diagnosis = request.Diagnosis?.Trim();

        // La fecha prometida es un compromiso comercial: la mueve el Dueño, no el técnico.
        if (scope.IsOwner) order.PromisedAt = request.PromisedAt;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<WorkOrderDetailDto> AssignAsync(
        Guid id, AssignTechnicianRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var order = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        if (request.TechnicianId is { } technicianId)
            await EnsureTechnicianAsync(technicianId, order.BranchId, ct);

        order.AssignedTechnicianId = request.TechnicianId;
        await db.SaveChangesAsync(ct);

        if (request.TechnicianId is { } assigned)
            await notifications.NotifyUserAsync(order.TenantId, assigned, new NotificationDraft(
                NotificationType.WorkOrderAssigned,
                $"Nueva orden asignada · {order.Number}",
                await VehicleLabelAsync(order.VehicleId, ct),
                WorkOrderId: order.Id), ct);

        return await GetAsync(id, ct);
    }

    public async Task<WorkOrderDetailDto> ChangeStatusAsync(
        Guid id, ChangeStatusRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var order = await FindEditableAsync(id, scope, ct);

        if (scope.IsCustomer)
            throw new ForbiddenException("Un cliente no puede cambiar el estado de la orden.");

        if (order.Status == request.Status)
            return await GetAsync(id, ct);

        // La app móvil puede enviar este cambio desde la cola offline sobre una orden que
        // mientras tanto avanzó. Se rechaza con un mensaje que dice el estado actual, para
        // que el técnico entienda por qué no se aplicó.
        if (!WorkOrderStatusTransitions.CanTransition(order.Status, request.Status))
            throw new ConflictException(
                $"No se puede pasar de {Describe(order.Status)} a {Describe(request.Status)}. " +
                $"La orden está en {Describe(order.Status)}.");

        var previous = order.Status;
        order.Status = request.Status;

        if (request.Status is WorkOrderStatus.Delivered or WorkOrderStatus.Cancelled)
            order.ClosedAt = clock.UtcNow;

        db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
        {
            WorkOrderId = order.Id,
            FromStatus = previous,
            ToStatus = request.Status,
            ChangedAt = clock.UtcNow,
            ChangedByUserId = scope.UserId,
            Note = request.Note?.Trim(),
            IsVisibleToCustomer = request.IsVisibleToCustomer
        });

        await db.SaveChangesAsync(ct);

        // Solo se avisa de lo que el cliente puede ver: si el taller marcó el cambio como
        // interno, mandarle una notificación lo delataría igual.
        if (request.IsVisibleToCustomer)
            await NotifyCustomerOfStatusAsync(order, ct);

        return await GetAsync(id, ct);
    }

    private async Task NotifyCustomerOfStatusAsync(WorkOrder order, CancellationToken ct)
    {
        var customerId = await db.Vehicles
            .Where(v => v.Id == order.VehicleId)
            .Select(v => v.CustomerId)
            .FirstOrDefaultAsync(ct);

        if (customerId == Guid.Empty) return;

        var body = order.Status switch
        {
            WorkOrderStatus.Ready => "Su vehículo está listo para retirar.",
            WorkOrderStatus.Delivered => "Su vehículo fue entregado. Gracias por confiar en nosotros.",
            WorkOrderStatus.Cancelled => "La orden fue cancelada.",
            _ => $"Su vehículo pasó a: {Describe(order.Status).ToLowerInvariant()}."
        };

        await notifications.NotifyCustomerAsync(order.TenantId, customerId, new NotificationDraft(
            NotificationType.WorkOrderStatusChanged,
            $"Orden {order.Number}",
            body,
            WorkOrderId: order.Id), ct);
    }

    private async Task<string> VehicleLabelAsync(Guid vehicleId, CancellationToken ct) =>
        await db.Vehicles
            .Where(v => v.Id == vehicleId)
            .Select(v => v.Brand + " " + v.Model + (v.Plate == null ? "" : " · " + v.Plate))
            .FirstOrDefaultAsync(ct) ?? "Vehículo del taller";

    public async Task<WorkOrderTaskDto> AddTaskAsync(
        Guid workOrderId, SaveWorkOrderTaskRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var order = await FindEditableAsync(workOrderId, scope, ct);

        if (scope.IsCustomer) throw new ForbiddenException("Un cliente no puede editar los pasos.");

        var lastSequence = await db.WorkOrderTasks
            .Where(t => t.WorkOrderId == order.Id)
            .MaxAsync(t => (int?)t.Sequence, ct) ?? 0;

        var task = new WorkOrderTask
        {
            WorkOrderId = order.Id,
            Sequence = lastSequence + 1,
            // Si no se indica técnico, hereda el de la orden: es lo que espera quien la abre.
            AssignedTechnicianId = request.AssignedTechnicianId ?? order.AssignedTechnicianId
        };

        Apply(task, request);
        db.WorkOrderTasks.Add(task);
        await db.SaveChangesAsync(ct);

        return await MapTaskAsync(task, ct);
    }

    public async Task<WorkOrderTaskDto> UpdateTaskAsync(
        Guid workOrderId, Guid taskId, SaveWorkOrderTaskRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        await FindEditableAsync(workOrderId, scope, ct);

        if (scope.IsCustomer) throw new ForbiddenException("Un cliente no puede editar los pasos.");

        var task = await FindTaskAsync(workOrderId, taskId, ct);
        task.AssignedTechnicianId = request.AssignedTechnicianId ?? task.AssignedTechnicianId;

        Apply(task, request);
        await db.SaveChangesAsync(ct);

        return await MapTaskAsync(task, ct);
    }

    public async Task<WorkOrderTaskDto> CompleteTaskAsync(
        Guid workOrderId, Guid taskId, CompleteTaskRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        await FindEditableAsync(workOrderId, scope, ct);

        if (scope.IsCustomer) throw new ForbiddenException("Un cliente no puede completar pasos.");

        var task = await FindTaskAsync(workOrderId, taskId, ct);
        var now = clock.UtcNow;

        task.IsDone = request.IsDone;
        task.ActualHours = request.ActualHours ?? task.ActualHours;
        task.TechnicianNotes = request.TechnicianNotes?.Trim() ?? task.TechnicianNotes;

        if (request.IsDone)
        {
            // Si el técnico marca hecho sin haber pulsado "iniciar", igual queda una hora de
            // inicio: sin ella no se puede medir cuánto tomó el paso.
            task.StartedAt ??= now;
            task.CompletedAt = now;
        }
        else
        {
            task.CompletedAt = null;
        }

        await db.SaveChangesAsync(ct);
        return await MapTaskAsync(task, ct);
    }

    public async Task DeleteTaskAsync(Guid workOrderId, Guid taskId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        await FindEditableAsync(workOrderId, scope, ct);
        var task = await FindTaskAsync(workOrderId, taskId, ct);

        db.WorkOrderTasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WorkOrderPartDto>> ListPartsAsync(
        Guid workOrderId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (!await Scoped(scope).AnyAsync(w => w.Id == workOrderId, ct))
            throw new NotFoundException("La orden de trabajo no existe.");

        return await PartsOfAsync(workOrderId, ct);
    }

    public async Task<WorkOrderPartDto> AddPartAsync(
        Guid workOrderId, AddWorkOrderPartRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var order = await FindEditableAsync(workOrderId, scope, ct);

        if (scope.IsCustomer) throw new ForbiddenException("Un cliente no puede cargar repuestos.");

        if (request.Quantity <= 0)
            throw new AppException("La cantidad debe ser mayor que cero.");

        if (order.Status is WorkOrderStatus.Delivered or WorkOrderStatus.Cancelled)
            throw new ConflictException(
                $"La orden está {Describe(order.Status).ToLowerInvariant()}: ya no admite repuestos.");

        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == request.PartId, ct)
            ?? throw new NotFoundException("El repuesto no existe.");

        if (!part.IsActive)
            throw new AppException($"{part.Name} está desactivado en el catálogo.");

        if (request.WorkOrderTaskId is { } taskId)
            await FindTaskAsync(workOrderId, taskId, ct);

        // Sale de la bodega de la sucursal donde está el vehículo, no de otra: si falta,
        // ConsumeAsync corta aquí con 409 y no se crea la línea.
        await stock.ConsumeAsync(order.BranchId, part.Id, request.Quantity, order.Id, scope.UserId, ct);

        var line = new WorkOrderPart
        {
            WorkOrderId = order.Id,
            PartId = part.Id,
            WorkOrderTaskId = request.WorkOrderTaskId,
            Quantity = request.Quantity,
            // Congelados a propósito: cambiar el precio del catálogo mañana no debe alterar
            // lo que ya se le cobró a un cliente ni el margen de una orden cerrada.
            UnitPrice = request.UnitPrice ?? part.SalePrice,
            UnitCost = part.CostPrice
        };

        db.WorkOrderParts.Add(line);
        await db.SaveChangesAsync(ct);

        return (await PartsOfAsync(order.Id, ct)).First(p => p.Id == line.Id);
    }

    public async Task RemovePartAsync(Guid workOrderId, Guid partLineId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var order = await FindEditableAsync(workOrderId, scope, ct);

        if (scope.IsCustomer) throw new ForbiddenException("Un cliente no puede quitar repuestos.");

        var line = await db.WorkOrderParts
            .FirstOrDefaultAsync(p => p.Id == partLineId && p.WorkOrderId == workOrderId, ct)
            ?? throw new NotFoundException("El repuesto no está cargado en esta orden.");

        await stock.ReturnAsync(
            order.BranchId, line.PartId, line.Quantity, order.Id, scope.UserId,
            $"Devolución de {order.Number}", ct);

        db.WorkOrderParts.Remove(line);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<WorkOrderPartDto>> PartsOfAsync(
        Guid workOrderId, CancellationToken ct) =>
        await db.WorkOrderParts.AsNoTracking()
            .Where(p => p.WorkOrderId == workOrderId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new WorkOrderPartDto(
                p.Id,
                p.PartId,
                p.Part.Sku,
                p.Part.Name,
                p.Part.Unit,
                p.Quantity,
                p.UnitPrice,
                p.UnitCost,
                p.Quantity * p.UnitPrice,
                p.WorkOrderTaskId,
                db.WorkOrderTasks.Where(t => t.Id == p.WorkOrderTaskId).Select(t => t.Title).FirstOrDefault()))
            .ToListAsync(ct);

    /// <summary>
    /// El Técnico ve solo las órdenes que tiene asignadas; el Cliente, solo las de sus
    /// vehículos; el Dueño, todas las del taller.
    /// </summary>
    private IQueryable<WorkOrder> Scoped(AccessScope scope)
    {
        var q = db.WorkOrders.AsNoTracking();

        if (scope.IsTechnician)
            return q.Where(w => w.AssignedTechnicianId == scope.UserId);

        if (scope.IsCustomer)
            return q.Where(w => w.Vehicle.CustomerId == scope.CustomerId);

        return q;
    }

    private async Task<WorkOrder> FindEditableAsync(Guid id, AccessScope scope, CancellationToken ct) =>
        await Scoped(scope).AsTracking().FirstOrDefaultAsync(w => w.Id == id, ct)
        ?? throw new NotFoundException("La orden de trabajo no existe.");

    private async Task<WorkOrderTask> FindTaskAsync(Guid workOrderId, Guid taskId, CancellationToken ct) =>
        await db.WorkOrderTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.WorkOrderId == workOrderId, ct)
        ?? throw new NotFoundException("El paso no existe en esta orden.");

    private static void Apply(WorkOrderTask task, SaveWorkOrderTaskRequest request)
    {
        task.Title = request.Title.Trim();
        task.Description = request.Description?.Trim();
        task.LaborServiceId = request.LaborServiceId;
        task.EstimatedHours = request.EstimatedHours;
    }

    private async Task EnsureTechnicianAsync(Guid technicianId, Guid branchId, CancellationToken ct)
    {
        var technician = await db.UsersInTenant.FirstOrDefaultAsync(u => u.Id == technicianId, ct)
            ?? throw new NotFoundException("El técnico no existe.");

        if (!technician.IsActive)
            throw new AppException("El técnico está desactivado.");

        // Asignar a alguien que no trabaja en esa sucursal deja la orden invisible para él:
        // su bandeja filtra por asignación, pero el resto de la app filtra por sucursal.
        var worksHere = await db.UserBranches
            .AnyAsync(ub => ub.UserId == technicianId && ub.BranchId == branchId, ct);

        if (!worksHere)
            throw new AppException("El técnico no está asignado a la sucursal de la orden.");
    }

    /// <summary>
    /// Correlativo por sucursal, ej. "MTZ-000123". El contador vive en la fila de la sucursal
    /// y se incrementa dentro de la misma transacción que inserta la orden.
    /// </summary>
    private async Task<string> NextNumberAsync(Branch branch, CancellationToken ct)
    {
        var tracked = await db.Branches.FirstAsync(b => b.Id == branch.Id, ct);
        tracked.WorkOrderSequence++;

        var prefix = string.IsNullOrEmpty(tracked.Code) ? "OT" : tracked.Code;
        return $"{prefix}-{tracked.WorkOrderSequence:D6}";
    }

    private async Task<WorkOrderDetailDto> MapDetailAsync(
        WorkOrder order, AccessScope scope, CancellationToken ct)
    {
        var userIds = order.Tasks.Select(t => t.AssignedTechnicianId)
            .Append(order.AssignedTechnicianId)
            .Concat(order.StatusHistory.Select(h => (Guid?)h.ChangedByUserId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var names = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        // El cliente ve qué repuestos se le pusieron y a qué precio, pero no el costo del
        // taller: ese dato es del margen, no de la factura.
        var parts = scope.IsCustomer
            ? (await PartsOfAsync(order.Id, ct)).Select(p => p with { UnitCost = 0 }).ToList()
            : await PartsOfAsync(order.Id, ct);

        // El cliente ve una versión curada de la línea de tiempo: las notas internas del
        // taller no salen de ahí.
        var timeline = order.StatusHistory
            .Where(h => !scope.IsCustomer || h.IsVisibleToCustomer)
            .OrderBy(h => h.ChangedAt)
            .Select(h => new WorkOrderStatusEntryDto(
                h.FromStatus,
                h.ToStatus,
                h.ChangedAt,
                names.GetValueOrDefault(h.ChangedByUserId, "—"),
                h.Note,
                h.IsVisibleToCustomer))
            .ToList();

        var tasks = order.Tasks
            .OrderBy(t => t.Sequence)
            .Select(t => new WorkOrderTaskDto(
                t.Id, t.Title, t.Description, t.Sequence, t.IsDone,
                t.AssignedTechnicianId,
                t.AssignedTechnicianId is { } id ? names.GetValueOrDefault(id) : null,
                t.EstimatedHours, t.ActualHours, t.TechnicianNotes, t.StartedAt, t.CompletedAt))
            .ToList();

        return new WorkOrderDetailDto(
            order.Id,
            order.Number,
            order.BranchId,
            order.Branch.Name,
            order.Status,
            // El cliente no cambia estados, así que no tiene sentido ofrecerle las opciones.
            scope.IsCustomer ? [] : WorkOrderStatusTransitions.NextStatuses(order.Status),
            order.VehicleId,
            $"{order.Vehicle.Brand} {order.Vehicle.Model}",
            order.Vehicle.Type,
            order.Vehicle.Plate,
            order.Vehicle.Mileage,
            order.Vehicle.CustomerId,
            order.Vehicle.Customer.FullName,
            order.Vehicle.Customer.Phone,
            order.AssignedTechnicianId,
            order.AssignedTechnicianId is { } techId ? names.GetValueOrDefault(techId) : null,
            order.Description,
            order.Diagnosis,
            order.MileageIn,
            order.OpenedAt,
            order.PromisedAt,
            order.ClosedAt,
            order.ServiceRequestId,
            tasks,
            timeline,
            parts,
            parts.Sum(p => p.Total));
    }

    private async Task<WorkOrderTaskDto> MapTaskAsync(WorkOrderTask task, CancellationToken ct)
    {
        var name = task.AssignedTechnicianId is { } id
            ? await db.Users.Where(u => u.Id == id).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            : null;

        return new WorkOrderTaskDto(
            task.Id, task.Title, task.Description, task.Sequence, task.IsDone,
            task.AssignedTechnicianId, name, task.EstimatedHours, task.ActualHours,
            task.TechnicianNotes, task.StartedAt, task.CompletedAt);
    }

    private static string Describe(WorkOrderStatus status) => status switch
    {
        WorkOrderStatus.Received => "Recibida",
        WorkOrderStatus.Diagnosing => "En diagnóstico",
        WorkOrderStatus.WaitingApproval => "Esperando aprobación",
        WorkOrderStatus.WaitingParts => "Esperando repuestos",
        WorkOrderStatus.InProgress => "En proceso",
        WorkOrderStatus.Testing => "En pruebas",
        WorkOrderStatus.Ready => "Lista para entrega",
        WorkOrderStatus.Delivered => "Entregada",
        WorkOrderStatus.Cancelled => "Cancelada",
        _ => status.ToString()
    };
}
