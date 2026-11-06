using Garaj.Application.Abstractions;
using Garaj.Domain.Enums;

namespace Garaj.Application.Assistant;

/// <summary>
/// Los tres techos del asistente, y el registro de lo que se gasta.
/// </summary>
/// <remarks>
/// Está separado de <see cref="IAssistant"/> a propósito: una cosa es hablar con el modelo y
/// otra decidir si se puede. Sin esta separación el tope se termina comprobando en cada sitio
/// que llama, y basta que uno se olvide para que el techo no exista.
/// </remarks>
public interface IAssistantQuota
{
    /// <summary>
    /// Si el taller puede gastar ahora. No reserva nada: entre la comprobación y la llamada
    /// pueden colarse dos peticiones a la vez, y eso se acepta —el desvío es de un crédito,
    /// no de un orden de magnitud, y bloquear por tenant costaría más de lo que protege—.
    /// </summary>
    Task<AssistantAllowance> CheckAsync(AssistantFeature feature, CancellationToken ct = default);

    /// <summary>
    /// Registra la llamada. <b>Se llama siempre</b>, haya salido bien o mal: un reintento y una
    /// llamada fallida se pagan igual que la que funcionó.
    /// </summary>
    Task<Guid> RecordAsync(
        AssistantFeature feature,
        AssistantResult result,
        Guid? workOrderId = null,
        CancellationToken ct = default);

    /// <summary>Lo consumido del día y del mes. Es lo que se enseña cuando se acaba.</summary>
    Task<AssistantUsageDto> UsageAsync(CancellationToken ct = default);
}

/// <param name="Reason">
/// Por qué no, redactado para el taller. Nunca es un error: «se acabaron las consultas de hoy,
/// vuelven mañana» es una respuesta, un 500 no.
/// </param>
public record AssistantAllowance(bool Allowed, string? Reason, AssistantUsageDto Usage)
{
    public static AssistantAllowance Yes(AssistantUsageDto usage) => new(true, null, usage);

    public static AssistantAllowance No(string reason, AssistantUsageDto usage) =>
        new(false, reason, usage);
}

/// <param name="SpentHnl">
/// Lo gastado en lempiras este mes. Va en el DTO interno, pero <b>no</b> sale hacia el taller:
/// ese número es nuestro.
/// </param>
public record AssistantUsageDto(
    bool Enabled,
    int CreditsToday,
    int CreditsThisMonth,
    int CreditsPerDay,
    int CreditsPerMonth,
    decimal SpentHnl,
    decimal CapHnl);

/// <summary>Cuánto vale cada función, en créditos.</summary>
/// <remarks>
/// Un solo número que el taller entiende. La lectura del negocio no gasta porque no la pide
/// él: la mandamos nosotros, y cobrarle por algo que no pidió no se puede explicar.
/// </remarks>
public static class CreditosDelAsistente
{
    public static int De(AssistantFeature feature) => feature switch
    {
        AssistantFeature.WorkSteps => 1,
        AssistantFeature.TechnicianQuery => 5,
        _ => 0
    };
}
