using Garaj.Domain.Entities;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Rules;

/// <summary>
/// Estado de cobro de un taller, ya resuelto: qué puede hacer y qué hay que avisarle.
/// </summary>
/// <param name="DaysLeft">
/// Días que faltan para <paramref name="PaidThrough"/>. Negativo si ya pasó. Null si el taller
/// no tiene cobro configurado.
/// </param>
/// <param name="ReadOnlyOn">Día en que, sin pagar, el taller deja de poder escribir.</param>
public readonly record struct SubscriptionStatus(
    SubscriptionState State,
    DateOnly? PaidThrough,
    int? DaysLeft,
    DateOnly? ReadOnlyOn,
    DateOnly? AgreementThrough,
    string? AgreementNote)
{
    /// <summary>Si puede crear, modificar y facturar. Lo que consulta el bloqueo.</summary>
    public bool CanWrite => State is not (SubscriptionState.ReadOnly or SubscriptionState.Suspended);

    /// <summary>Si hay algo que decirle al Dueño. Estando al día y sin acuerdo, no se le dice nada.</summary>
    public bool ShouldWarn => State != SubscriptionState.Active || AgreementThrough is not null;
}

/// <summary>
/// Decide en un solo lugar cómo está el taller con su mensualidad. Lo preguntan tres bocas
/// distintas —el middleware que bloquea, el <c>/auth/me</c> que avisa y la lista del panel de
/// plataforma—, y que las tres respondan lo mismo es justamente el punto.
/// </summary>
public static class SubscriptionRules
{
    /// <summary>Días antes del vencimiento en que se empieza a avisar.</summary>
    public const int WarnDays = 7;

    public static SubscriptionStatus For(Tenant tenant, DateOnly today)
    {
        var agreement = tenant.UnblockedThrough >= today ? tenant.UnblockedThrough : null;
        var note = agreement is null ? null : tenant.UnblockNote;

        // La suspensión manual gana sobre todo lo demás, incluido un acuerdo de pago: es la
        // decisión explícita de cortar, y no la toma una fecha.
        if (!tenant.IsActive)
            return new SubscriptionStatus(
                SubscriptionState.Suspended, tenant.PaidThrough, null, null, agreement, note);

        // Taller sin cobro configurado —los que existían antes de esto, y el de demostración—:
        // no se bloquea nunca. El silencio no debe convertirse en un corte.
        if (tenant.PaidThrough is not { } paidThrough)
            return new SubscriptionStatus(SubscriptionState.Active, null, null, null, agreement, note);

        var daysLeft = paidThrough.DayNumber - today.DayNumber;
        var readOnlyOn = paidThrough.AddDays(Math.Max(tenant.GraceDays, 0) + 1);

        var state = agreement is not null ? SubscriptionState.Active
            : daysLeft > WarnDays ? SubscriptionState.Active
            : daysLeft >= 0 ? SubscriptionState.DueSoon
            : today < readOnlyOn ? SubscriptionState.Grace
            : SubscriptionState.ReadOnly;

        return new SubscriptionStatus(state, paidThrough, daysLeft, readOnlyOn, agreement, note);
    }
}
