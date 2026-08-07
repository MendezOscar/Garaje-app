using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Registro inmutable de cada cambio de estado. Es la línea de tiempo que el cliente ve
/// en su app, así que la nota está pensada para leerse desde afuera del taller.
/// </summary>
public class WorkOrderStatusHistory : TenantEntity
{
    public Guid WorkOrderId { get; set; }

    public WorkOrderStatus? FromStatus { get; set; }
    public WorkOrderStatus ToStatus { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
    public Guid ChangedByUserId { get; set; }

    public string? Note { get; set; }

    /// <summary>Si es false, la entrada solo la ve el personal del taller.</summary>
    public bool IsVisibleToCustomer { get; set; } = true;

    public WorkOrder WorkOrder { get; set; } = null!;
}
