namespace Garaj.Application.Assistant;

/// <summary>
/// La consulta del técnico: preguntar con el vehículo en la rampa, con el contexto de la
/// orden ya puesto y el historial del propio taller como material.
/// </summary>
/// <remarks>
/// Lo que vale y nadie más puede dar es la segunda parte: «a tres Corolla 2015 con P0301 este
/// taller les cambió la bobina del cilindro 3, y en uno volvió a los dos meses». Eso está
/// dentro de órdenes cerradas que nadie va a abrir.
/// </remarks>
public interface ITechnicianQueryService
{
    /// <summary>Las consultas de esta orden, la más reciente primero.</summary>
    Task<IReadOnlyList<ConversationListItemDto>> ListAsync(
        Guid workOrderId, CancellationToken ct = default);

    Task<ConversationDto> GetAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Pregunta. Sin <paramref name="conversationId"/> abre una consulta nueva; con él sigue
    /// la que ya estaba.
    /// </summary>
    Task<ConversationDto> AskAsync(
        Guid workOrderId, Guid? conversationId, string question, CancellationToken ct = default);
}

public record ConversationListItemDto(
    Guid Id, string Title, bool IsClosed, int Turns, DateTimeOffset CreatedAt);

/// <param name="Message">
/// Por qué no se pudo preguntar, cuando no se pudo: sin créditos del día, la consulta cerrada,
/// el asistente caído. Nunca un error.
/// </param>
/// <param name="CreditsLeftToday">
/// Lo que queda. Se enseña porque una consulta vale cinco veces lo que una propuesta, y el
/// técnico tiene que poder decidir si gasta.
/// </param>
public record ConversationDto(
    Guid? Id,
    string? Title,
    bool IsClosed,
    IReadOnlyList<ConversationTurnDto> Turns,
    string? Message,
    int CreditsLeftToday);

public record ConversationTurnDto(bool FromUser, string Content, DateTimeOffset At);

/// <param name="ConversationId">Null abre una consulta nueva.</param>
public record AskAssistantRequest(string Question, Guid? ConversationId = null);
