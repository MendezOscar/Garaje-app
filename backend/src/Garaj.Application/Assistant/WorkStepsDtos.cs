using Garaj.Application.WorkOrders;

namespace Garaj.Application.Assistant;

/// <summary>
/// «Proponer los pasos»: del diagnóstico sale una lista de pasos y repuestos que el dueño
/// edita y acepta.
/// </summary>
/// <remarks>
/// Nada de lo que devuelve se guarda como pasos de la orden hasta que alguien acepta. Por eso
/// son dos operaciones y no una: proponer no escribe en la orden, aceptar sí.
/// </remarks>
public interface IWorkStepsAssistant
{
    Task<WorkStepsProposalDto> ProposeAsync(Guid workOrderId, CancellationToken ct = default);

    /// <summary>
    /// Crea en la orden los pasos que quedaron después de editar, y devuelve los repuestos
    /// como <b>sugerencia</b>: cargarlos descontaría la bodega y el trabajo todavía no se ha
    /// hecho.
    /// </summary>
    Task<AcceptWorkStepsResultDto> AcceptAsync(
        Guid workOrderId, Guid proposalId, AcceptWorkStepsRequest request,
        CancellationToken ct = default);
}

/// <param name="Available">
/// Si salió propuesta. En false no es un error: es que el taller no tiene el asistente, que se
/// acabaron las consultas del día, o que el diagnóstico está vacío. El motivo va en
/// <paramref name="Message"/>, redactado para el taller.
/// </param>
/// <param name="Note">
/// Lo que el asistente quiso aclarar: qué falta averiguar, qué repuesto no estaba en el
/// catálogo. Cuando no alcanza para proponer nada, los pasos vienen vacíos y esto trae el
/// porqué —y esa es una respuesta correcta, no un fallo—.
/// </param>
public record WorkStepsProposalDto(
    Guid? Id,
    bool Available,
    string? Message,
    IReadOnlyList<ProposedTaskDto> Tasks,
    IReadOnlyList<ProposedPartDto> Parts,
    string? Note,
    int CreditsLeftToday);

/// <param name="LaborServiceId">
/// El servicio del catálogo con el que se cobra, si alguno calza. Null es correcto: vale más
/// un paso sin servicio que uno colgado del servicio equivocado.
/// </param>
public record ProposedTaskDto(
    string Title,
    string? Description,
    Guid? LaborServiceId,
    string? LaborServiceName,
    decimal Hours);

/// <param name="ForTask">
/// Para qué paso es. Sirve para que al tachar un paso se vea qué repuesto se va con él.
/// </param>
/// <param name="Available">Existencia en la bodega de la sucursal de la orden.</param>
public record ProposedPartDto(
    Guid PartId,
    string Sku,
    string Name,
    string Unit,
    decimal Quantity,
    decimal Available,
    string ForTask);

/// <param name="Tasks">
/// Los pasos tal como quedaron después de que el humano los editó. Se mandan completos y no
/// como «acepto el 1 y el 3»: lo que se guarda es lo que la persona vio y corrigió, no lo que
/// el modelo propuso.
/// </param>
public record AcceptWorkStepsRequest(IReadOnlyList<SaveWorkOrderTaskRequest> Tasks);

public record AcceptWorkStepsResultDto(
    IReadOnlyList<WorkOrderTaskDto> Tasks,
    IReadOnlyList<SuggestedPartDto> SuggestedParts);
