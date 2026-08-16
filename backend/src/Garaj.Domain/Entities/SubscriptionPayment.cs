using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Un pago de mensualidad del taller hacia nosotros. Es el historial que contesta «¿yo ya te
/// pagué junio?», así que no se edita ni se borra: si un cobro salió mal se registra el ajuste.
/// </summary>
/// <remarks>
/// No implementa <c>ITenantEntity</c> aunque lleve <c>TenantId</c>. El global query filter está
/// pensado para aislar a un taller de otro, y esto no lo lee ningún taller: lo lee el usuario de
/// plataforma, cuyo token no tiene taller y al que el filtro le devolvería siempre cero filas.
/// </remarks>
public class SubscriptionPayment : AuditableEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Día en que el taller pagó, no el día en que lo registramos: casi nunca es el mismo.</summary>
    public DateOnly PaidOn { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "HNL";

    /// <summary>Cómo pagó: depósito, transferencia, efectivo. Texto libre.</summary>
    public string? Method { get; set; }

    /// <summary>Número de depósito o de transferencia, para poder cotejarlo con el banco.</summary>
    public string? Reference { get; set; }

    /// <summary>A qué fecha quedó corrida la suscripción con este pago.</summary>
    public DateOnly CoversThrough { get; set; }

    public string? Note { get; set; }

    /// <summary>Quién de nosotros lo registró.</summary>
    public Guid? RegisteredByUserId { get; set; }
}
