using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Orden de trabajo: el vehículo ya está en el taller y se le está haciendo algo.
/// Concentra el paso a paso (<see cref="Tasks"/>), la evidencia fotográfica, los repuestos
/// consumidos y el historial de estados que ve el cliente.
/// </summary>
public class WorkOrder : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid? ServiceRequestId { get; set; }

    /// <summary>Correlativo legible por sucursal, ej. "SUR-000123".</summary>
    public string Number { get; set; } = null!;

    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Received;

    /// <summary>Técnico responsable. Null mientras el Dueño no la asigne.</summary>
    public Guid? AssignedTechnicianId { get; set; }

    /// <summary>Motivo de ingreso, copiado del requerimiento y editable por el Dueño.</summary>
    public string Description { get; set; } = null!;

    /// <summary>Diagnóstico del técnico tras revisar el vehículo.</summary>
    public string? Diagnosis { get; set; }

    /// <summary>
    /// De dónde sale el precio de la mano de obra de esta orden. Con
    /// <see cref="LaborMode.Catalog"/> se suma lo que valga cada paso; con
    /// <see cref="LaborMode.Manual"/>, los pasos no llevan precio y se cobra
    /// <see cref="ManualLaborTotal"/>.
    /// </summary>
    public LaborMode LaborMode { get; set; } = LaborMode.Catalog;

    /// <summary>Total de mano de obra escrito a mano. Solo cuenta en modo manual.</summary>
    public decimal? ManualLaborTotal { get; set; }

    public int? MileageIn { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? PromisedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Venta generada al cerrar la orden. Null mientras no se factura.</summary>
    public Guid? SaleId { get; set; }

    /// <summary>
    /// Token del enlace de seguimiento. Igual que en la cotización y el estado de cuenta, el
    /// token **es** la credencial: el cliente abre el enlace que le llega por WhatsApp y ve en
    /// qué va su vehículo sin cuenta ni contraseña. Es lo que permite atender al cliente que
    /// nunca va a instalar la app, que son casi todos.
    ///
    /// Expone estado, pasos, fotos marcadas como visibles y —si ya se facturó— el total y el
    /// saldo. Nunca el costo del taller. Si un enlace se filtra, se corta cambiando el token.
    /// </summary>
    public Guid PublicToken { get; set; } = Guid.NewGuid();

    // ---------- Próximo servicio ----------
    //
    // Lo que el taller le recomendó al cliente al entregar. Se escribe al cerrar la orden y es
    // opcional: hay trabajos que no vuelven, y ponerles recordatorio sería llamar a alguien
    // para nada. Es una recomendación de esta orden, no del vehículo: si el carro vuelve antes,
    // la orden nueva trae la suya y esta deja de importar.

    /// <summary>Cuándo le toca. Null significa que este trabajo no se repite.</summary>
    public DateTimeOffset? NextServiceAt { get; set; }

    /// <summary>
    /// A qué kilometraje le toca. Se guarda para decírselo al cliente —«a los 45,000»—, pero lo
    /// que dispara el recordatorio es la fecha: hasta que el vehículo no vuelve, el taller no
    /// sabe cuánto ha rodado.
    /// </summary>
    public int? NextServiceMileage { get; set; }

    /// <summary>Cuándo se le recordó por última vez. Null si todavía no se le ha avisado.</summary>
    public DateTimeOffset? NextServiceRemindedAt { get; set; }

    public Branch Branch { get; set; } = null!;
    public Vehicle Vehicle { get; set; } = null!;
    public ICollection<WorkOrderTask> Tasks { get; set; } = new List<WorkOrderTask>();
    public ICollection<WorkOrderPart> Parts { get; set; } = new List<WorkOrderPart>();
    public ICollection<WorkOrderStatusHistory> StatusHistory { get; set; } = new List<WorkOrderStatusHistory>();
}
