using Garaj.Domain.Enums;

namespace Garaj.Domain.Rules;

/// <summary>
/// Transiciones válidas de estado de una orden de trabajo. Se centraliza aquí porque la
/// regla la aplican tanto la API como los tests, y porque el móvil puede quedar desfasado
/// enviando un cambio desde la cola offline sobre una orden que ya avanzó.
/// </summary>
public static class WorkOrderStatusTransitions
{
    /// <summary>
    /// El grafo completo, con los nueve estados. No se toca cuando un taller apaga un paso:
    /// es el camino canónico, y el flujo de cada taller sale de recorrerlo saltando lo que
    /// tiene apagado.
    /// </summary>
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

    /// <summary>Qué interruptor manda sobre cada estado, o null si el estado no se apaga.</summary>
    private static readonly Dictionary<WorkOrderStatus, WorkOrderSteps> Optional = new()
    {
        [WorkOrderStatus.Diagnosing] = WorkOrderSteps.Diagnosing,
        [WorkOrderStatus.WaitingApproval] = WorkOrderSteps.WaitingApproval,
        [WorkOrderStatus.WaitingParts] = WorkOrderSteps.WaitingParts,
        [WorkOrderStatus.Testing] = WorkOrderSteps.Testing,
        [WorkOrderStatus.Ready] = WorkOrderSteps.Ready
    };

    /// <summary>Los pasos que un taller puede apagar, en el orden en que se le enseñan.</summary>
    public static IReadOnlyList<WorkOrderStatus> OptionalStatuses { get; } =
    [
        WorkOrderStatus.Diagnosing,
        WorkOrderStatus.WaitingApproval,
        WorkOrderStatus.WaitingParts,
        WorkOrderStatus.Testing,
        WorkOrderStatus.Ready
    ];

    /// <summary>El interruptor de un estado, o null si es de los que no se apagan.</summary>
    public static WorkOrderSteps? StepOf(WorkOrderStatus status) =>
        Optional.TryGetValue(status, out var step) ? step : null;

    /// <summary>Si este taller usa este estado. Los del esqueleto siempre son verdad.</summary>
    public static bool IsEnabled(WorkOrderStatus status, WorkOrderSteps steps) =>
        StepOf(status) is not { } step || steps.HasFlag(step);

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to, WorkOrderSteps steps) =>
        NextStatuses(from, steps).Contains(to);

    public static IReadOnlyList<WorkOrderStatus> NextStatuses(WorkOrderStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>
    /// A dónde puede ir una orden en un taller que apagó algunos pasos.
    /// </summary>
    /// <remarks>
    /// Es el grafo de siempre, recorrido en anchura: un paso apagado no se ofrece, pero sí se
    /// atraviesa, y lo que estaba detrás de él queda al alcance. Un taller sin ninguno de los
    /// cinco queda con «Recibida → En proceso → Entregada», que es lo que pidió.
    ///
    /// El estado de partida nunca sale en la lista, ni aunque se llegue a él rodeando: la
    /// orden ya está ahí. Y la orden parada en un paso que el taller acaba de apagar también
    /// encuentra salida, porque se parte de donde está y no de donde debería estar.
    /// </remarks>
    public static IReadOnlyList<WorkOrderStatus> NextStatuses(WorkOrderStatus from, WorkOrderSteps steps)
    {
        if (steps == WorkOrderSteps.All) return NextStatuses(from);

        var alcanzables = new List<WorkOrderStatus>();
        var vistos = new HashSet<WorkOrderStatus> { from };
        var cola = new Queue<WorkOrderStatus>();
        cola.Enqueue(from);

        while (cola.Count > 0)
        {
            foreach (var destino in NextStatuses(cola.Dequeue()))
            {
                if (!vistos.Add(destino)) continue;

                if (IsEnabled(destino, steps))
                    alcanzables.Add(destino);
                else
                    cola.Enqueue(destino);
            }
        }

        alcanzables.Sort();
        return alcanzables;
    }

    /// <summary>Estados en los que la orden sigue viva en el taller.</summary>
    public static bool IsOpen(WorkOrderStatus status) =>
        status is not (WorkOrderStatus.Delivered or WorkOrderStatus.Cancelled);
}
