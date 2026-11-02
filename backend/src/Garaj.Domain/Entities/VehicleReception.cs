using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Cómo entró el vehículo al taller: con cuánto combustible, qué golpes traía y qué dejó
/// adentro el cliente, firmado por quien lo entregó.
/// </summary>
/// <remarks>
/// Es la hoja que decide una discusión. Sin ella, cuando el cliente dice que el rayón de la
/// puerta no venía, el taller no tiene con qué responder: lo que se anota al recibir vale
/// porque se anotó delante del cliente y él lo firmó.
///
/// Es opcional: hay trabajos que no la justifican, y obligarla haría que se llene con
/// cualquier cosa, que es peor que no tenerla.
///
/// Nace pegada al <b>requerimiento</b> cuando la llena quien recibe —que es el momento en que
/// el cliente está delante y puede firmar— y se pasa a la orden al aprobarlo. Esperar a que
/// la orden exista y esté asignada significaba llenarla de memoria al día siguiente, que es
/// justo lo que no sirve para resolver la discusión del rayón.
/// </remarks>
public class VehicleReception : TenantEntity
{
    /// <summary>
    /// La orden a la que pertenece. Null mientras la hoja viva en el requerimiento: se llena
    /// al aprobarlo, y desde ahí la hoja es de la orden como siempre.
    /// </summary>
    public Guid? WorkOrderId { get; set; }

    /// <summary>El requerimiento donde nació, cuando se llenó al recibir.</summary>
    public Guid? ServiceRequestId { get; set; }

    public FuelLevel FuelLevel { get; set; } = FuelLevel.Unknown;

    /// <summary>Los golpes y rayones que ya traía, escritos como los dicta quien recibe.</summary>
    public string? Damages { get; set; }

    /// <summary>Lo que el cliente deja adentro: herramienta, documentos, la llanta de repuesto.</summary>
    public string? Belongings { get; set; }

    /// <summary>Cualquier otra cosa que haya que dejar dicha.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Quién entregó el vehículo. No siempre es el dueño: lo trae un empleado, el hijo, el
    /// chofer. Es quien firma, así que es su nombre el que importa aquí.
    /// </summary>
    public string? DeliveredByName { get; set; }

    /// <summary>La firma, guardada como el logo del taller: la clave del objeto en el bucket.</summary>
    public string? SignatureStorageKey { get; set; }

    /// <summary>
    /// Firmada queda cerrada. Lo que el cliente firmó no se corrige después: la hoja vale
    /// porque dice lo que él leyó, y una que se puede editar más tarde no prueba nada.
    /// </summary>
    public bool EstaFirmada => SignatureStorageKey is not null;

    public Guid? ReceivedByUserId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    public WorkOrder? WorkOrder { get; set; }
    public ServiceRequest? ServiceRequest { get; set; }
}
