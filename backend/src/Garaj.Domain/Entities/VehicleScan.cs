using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Una lectura del vehículo hecha con el adaptador OBD2: los códigos que reportó, con fecha
/// y con quién la hizo.
/// </summary>
/// <remarks>
/// Es el otro camino del escáner, y la diferencia con el informe en PDF de la tableta LAUNCH
/// está en el formato, no en el aparato: el PDF es un documento —se adjunta, se enseña y ahí
/// termina—; esto entra código por código, de modo que el taller puede preguntar después
/// «qué carros reportaron P0301» y encontrarlos. Eso es justamente lo que se avisó que el
/// informe adjunto no iba a dar.
///
/// Lo que el taller borre del vehículo no borra esto: la lectura queda como quedó, que es lo
/// que permite comparar la de hoy con la de hace tres meses cuando el carro vuelve con lo
/// mismo. Por eso se guarda entera cada vez y no se actualiza la anterior.
/// </remarks>
public class VehicleScan : TenantEntity
{
    /// <summary>La orden desde la que se escaneó. La lectura nace dentro de una orden abierta.</summary>
    public Guid WorkOrderId { get; set; }

    /// <summary>
    /// El vehículo leído, copiado de la orden. Se guarda aquí aparte para poder buscar por
    /// vehículo sin pasar por la orden, y para que la lectura sobreviva si la orden se borra.
    /// </summary>
    public Guid? VehicleId { get; set; }

    /// <summary>Cuándo se hizo la lectura, según el teléfono que la hizo.</summary>
    public DateTimeOffset ScannedAt { get; set; }

    public Guid ScannedByUserId { get; set; }

    /// <summary>
    /// El VIN que reportó el propio vehículo (modo 09 02). Vale más que el escrito a mano en
    /// la ficha: este lo dictó la computadora del carro.
    /// </summary>
    public string? Vin { get; set; }

    /// <summary>Kilometraje al momento de la lectura, escrito por el técnico. El OBD2 no lo da.</summary>
    public int? Mileage { get; set; }

    /// <summary>Cómo se llamaba el adaptador con el que se leyó, para saber con qué se hizo.</summary>
    public string? DeviceName { get; set; }

    /// <summary>El protocolo que negoció el adaptador, tal como lo nombró él.</summary>
    public string? Protocol { get; set; }

    /// <summary>Lo que el técnico quiera dejar dicho de esta lectura.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// El PDF que se armó con esta lectura y quedó adjunto a la orden. Null si no se pudo
    /// generar: la lectura vale igual, y el informe se puede rehacer.
    /// </summary>
    public Guid? MediaAttachmentId { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
    public ICollection<VehicleScanCode> Codes { get; set; } = new List<VehicleScanCode>();
}

/// <summary>
/// Un código de falla de una lectura. Es la fila que hace buscable el diagnóstico.
/// </summary>
public class VehicleScanCode : TenantEntity
{
    public Guid VehicleScanId { get; set; }

    /// <summary>El código tal como manda la norma, en mayúsculas: P0301, C1234, U0100.</summary>
    public string Code { get; set; } = null!;

    public DtcStatus Status { get; set; } = DtcStatus.Stored;

    /// <summary>
    /// Qué significa el código, en español. La resuelve la app con su tabla y se guarda aquí
    /// copiada: si mañana se corrige un texto, la lectura vieja tiene que seguir diciendo lo
    /// que el técnico leyó el día que la hizo.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Si es un código de la norma —los mismos en cualquier marca— o uno propio del
    /// fabricante. Los propios se guardan igual, pero sin descripción de nuestra parte: esa
    /// la da el escáner de la marca, que es el que tiene comprada esa información.
    /// </summary>
    public bool IsGeneric { get; set; }

    public VehicleScan Scan { get; set; } = null!;
}
