using Garaj.Domain.Enums;

namespace Garaj.Domain.Rules;

/// <summary>
/// Transiciones válidas de estado de una orden de trabajo. Se centraliza aquí porque la
/// regla la aplican tanto la API como los tests, y porque el móvil puede quedar desfasado
/// enviando un cambio desde la cola offline sobre una orden que ya avanzó.
/// </summary>
public static class WorkOrderStatusTransitions
{
    private static readonly Dictionary<WorkOrderStatus, WorkOrderStatus[]> Allowed = new()
    {
        [WorkOrderStatus.Received] = [WorkOrderStatus.Diagnosing, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.Diagnosing] = [WorkOrderStatus.WaitingApproval, WorkOrderStatus.WaitingParts, WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.WaitingApproval] = [WorkOrderStatus.WaitingParts, WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.WaitingParts] = [WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.InProgress] = [WorkOrderStatus.WaitingParts, WorkOrderStatus.WaitingApproval, WorkOrderStatus.Testing, WorkOrderStatus.Ready, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.Testing] = [WorkOrderStatus.InProgress, WorkOrderStatus.Ready, WorkOrderStatus.Cancelled],
        [WorkOrderStatus.Ready] = [WorkOrderStatus.Delivered, WorkOrderStatus.InProgress],
        [WorkOrderStatus.Delivered] = [],
        [WorkOrderStatus.Cancelled] = []
    };

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<WorkOrderStatus> NextStatuses(WorkOrderStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Estados en los que la orden sigue viva en el taller.</summary>
    public static bool IsOpen(WorkOrderStatus status) =>
        status is not (WorkOrderStatus.Delivered or WorkOrderStatus.Cancelled);
}
