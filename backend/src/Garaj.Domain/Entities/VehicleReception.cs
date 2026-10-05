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
/// </remarks>
public class VehicleReception : TenantEntity
{
    public Guid WorkOrderId { get; set; }

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

    public Guid? ReceivedByUserId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
}
