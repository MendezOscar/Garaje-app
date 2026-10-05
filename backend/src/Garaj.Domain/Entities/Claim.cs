using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// El cliente vuelve diciendo que el trabajo quedó mal.
/// </summary>
/// <remarks>
/// Se cuelga de la venta y no de la orden porque es la venta la que lleva la garantía —y
/// porque un servicio rápido no tiene orden y también se reclama—. Si la garantía estaba viva
/// cuando se recibió el reclamo se congela aquí: vencerla después no puede cambiar lo que el
/// taller aceptó ese día.
/// </remarks>
public class Claim : TenantEntity
{
    public Guid SaleId { get; set; }

    /// <summary>Correlativo legible, ej. "REC-000012".</summary>
    public string Number { get; set; } = null!;

    public ClaimStatus Status { get; set; } = ClaimStatus.Open;

    /// <summary>Lo que el cliente dice que pasó, con sus palabras.</summary>
    public string Reason { get; set; } = null!;

    /// <summary>Si estaba en garantía el día que se recibió. Se congela a propósito.</summary>
    public bool WasUnderWarranty { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
    public Guid? ReceivedByUserId { get; set; }

    /// <summary>Qué se hizo, escrito al cerrarlo. Es lo que se lee un año después.</summary>
    public string? Resolution { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>
    /// La orden que se abrió para repararlo, cuando hubo que repararlo. Va aparte de la
    /// original: es trabajo nuevo, con sus pasos y sus repuestos, y es lo que después dice
    /// cuánto le costó la garantía al taller.
    /// </summary>
    public Guid? RepairWorkOrderId { get; set; }

    public Sale Sale { get; set; } = null!;
}
