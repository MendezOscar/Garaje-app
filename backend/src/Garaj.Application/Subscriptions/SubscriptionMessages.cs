using Garaj.Application.Auth;
using Garaj.Domain.Enums;
using Garaj.Domain.Rules;

namespace Garaj.Application.Subscriptions;

/// <summary>
/// El texto del aviso de cobro, escrito una sola vez. Va aquí y no en el panel ni en la app
/// porque son tres clientes distintos y el mensaje tiene que decir lo mismo en los tres; y
/// porque redactarlo desde fechas sueltas es exactamente donde se cuelan los «vence en -2 días».
/// </summary>
public static class SubscriptionMessages
{
    public static SubscriptionInfoDto ToDto(SubscriptionStatus status) => new(
        status.State.ToString(),
        status.CanWrite,
        status.PaidThrough,
        status.DaysLeft,
        status.ReadOnlyOn,
        status.AgreementThrough,
        status.AgreementNote,
        Describe(status));

    /// <summary>Lo que lee el Dueño en la franja del panel y de la app.</summary>
    public static string Describe(SubscriptionStatus status)
    {
        var vencio = Day(status.PaidThrough);

        return status.State switch
        {
            SubscriptionState.Suspended =>
                "El servicio está suspendido. Comuníquese con GarajApp para reactivarlo.",

            // El acuerdo se anuncia aunque esté vencido: es la razón por la que sigue trabajando.
            _ when status.AgreementThrough is { } hasta =>
                $"Acuerdo de pago vigente hasta el {Day(hasta)}."
                + (string.IsNullOrWhiteSpace(status.AgreementNote) ? "" : $" {status.AgreementNote}"),

            SubscriptionState.DueSoon when status.DaysLeft == 0 =>
                $"Su mensualidad vence hoy, {vencio}.",

            SubscriptionState.DueSoon =>
                $"Su mensualidad vence el {vencio}: {Days(status.DaysLeft ?? 0)}.",

            SubscriptionState.Grace =>
                $"Su mensualidad venció el {vencio}. Puede seguir trabajando hasta el "
                + $"{Day(status.ReadOnlyOn?.AddDays(-1))}.",

            SubscriptionState.ReadOnly =>
                $"Su mensualidad venció el {vencio}. Puede consultar toda su información, pero "
                + "no registrar trabajo nuevo hasta ponerse al día.",

            _ => string.Empty
        };
    }

    /// <summary>
    /// Lo que devuelve el 402 cuando el taller intenta escribir. Se separa del anterior porque
    /// aquí el usuario acaba de tocar un botón que no hizo nada: hay que decirle qué pasó y qué
    /// hacer, no darle un estado.
    /// </summary>
    public static string Blocked(SubscriptionStatus status) =>
        status.State == SubscriptionState.Suspended
            ? "El servicio de GarajApp está suspendido. Comuníquese con nosotros para reactivarlo."
            : $"La mensualidad de GarajApp venció el {Day(status.PaidThrough)}. Puede consultar "
              + "su información, pero para volver a registrar trabajo hay que ponerse al día.";

    private static string Days(int days) =>
        days == 1 ? "falta 1 día" : $"faltan {days} días";

    private static string Day(DateOnly? date) => date?.ToString("dd/MM/yyyy") ?? "—";
}
