using System.Text;
using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// La consulta del técnico. Arma el contexto de la orden y el historial del taller, y lleva
/// la conversación.
/// </summary>
/// <remarks>
/// El historial es la mitad del valor de esta función y toda su particularidad: el manual lo
/// tiene cualquiera, pero lo que <b>este</b> taller le hizo a <b>este</b> modelo no está en
/// ninguna otra parte, y hoy vive dentro de órdenes cerradas que nadie va a abrir.
/// </remarks>
public class TechnicianQueryService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IAssistant assistant,
    IAssistantQuota quota,
    IAssistantCatalog catalog) : ITechnicianQueryService
{
    /// <summary>
    /// Cuántas preguntas caben en una consulta antes de cerrarla.
    ///
    /// No es solo costo: una conversación larga se desenfoca, y una pregunta concreta por
    /// consulta se contesta mejor que seis encadenadas. Al llegar aquí se cierra y se abre
    /// otra, que además obliga a replantear la pregunta.
    /// </summary>
    private const int MaximoTurnos = 6;

    /// <summary>Cuántas órdenes cerradas del mismo modelo se le dan como material.</summary>
    private const int MaximoHistorial = 8;

    private const int LargoDeLaPregunta = 2000;

    public async Task<IReadOnlyList<ConversationListItemDto>> ListAsync(
        Guid workOrderId, CancellationToken ct = default)
    {
        // También para leer lo de antes: apagarle el asistente a un taller es apagarlo
        // entero, no solo dejar de dejarle preguntar.
        await EnsureEnabledAsync(ct);

        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer) throw new ForbiddenException();

        var consulta = db.AssistantConversations
            .AsNoTracking()
            .Where(c => c.WorkOrderId == workOrderId);

        // El técnico ve las suyas. El dueño y el encargado, las del taller: responden por el
        // trabajo completo y a veces tienen que entender de dónde salió una decisión.
        if (scope.IsTechnician)
            consulta = consulta.Where(c => c.StartedByUserId == scope.UserId);

        return await consulta
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ConversationListItemDto(
                c.Id, c.Title, c.IsClosed, c.Messages.Count(m => m.FromUser), c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<ConversationDto> GetAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);

        var conversacion = await BuscarAsync(conversationId, ct);
        var uso = await quota.UsageAsync(ct);

        return Map(conversacion, null, Restantes(uso));
    }

    public async Task<ConversationDto> AskAsync(
        Guid workOrderId, Guid? conversationId, string question, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer) throw new ForbiddenException();

        await EnsureEnabledAsync(ct);

        var pregunta = question?.Trim();

        if (string.IsNullOrEmpty(pregunta))
            throw new ConflictException("Escriba la pregunta.");

        if (pregunta.Length > LargoDeLaPregunta) pregunta = pregunta[..LargoDeLaPregunta];

        var orden = await db.WorkOrders
            .AsNoTracking()
            .Where(o => o.Id == workOrderId)
            .Select(o => new { o.BranchId, o.Diagnosis })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        scope.EnsureBranchAllowed(orden.BranchId);

        AssistantConversation? conversacion = null;

        if (conversationId is { } id)
        {
            conversacion = await BuscarAsync(id, ct);

            if (conversacion.IsClosed)
                return Map(conversacion,
                    "Esta consulta ya se cerró. Abra una nueva con la pregunta concreta.",
                    Restantes(await quota.UsageAsync(ct)));
        }

        var permiso = await quota.CheckAsync(AssistantFeature.TechnicianQuery, ct);

        if (!permiso.Allowed)
            return conversacion is null
                ? new ConversationDto(null, null, false, [], permiso.Reason, Restantes(permiso.Usage))
                : Map(conversacion, permiso.Reason, Restantes(permiso.Usage));

        var material = await catalog.ForWorkOrderAsync(workOrderId, ct);

        var prompt = new AssistantPrompt(
            TechnicianQueryPrompt.Instructions(material.Kind),
            TechnicianQueryPrompt.Context(
                material.Subject,
                orden.Diagnosis,
                await CodigosAsync(workOrderId, ct),
                await HistorialAsync(material.Subject, workOrderId, ct)),
            Conversacion(conversacion, pregunta),
            Schema: null,
            MaxTokens: 1500);

        var salida = await assistant.AskAsync(prompt, ct);

        var llamadaId = await quota.RecordAsync(
            AssistantFeature.TechnicianQuery, salida, workOrderId, ct);

        var uso = await quota.UsageAsync(ct);

        if (!salida.Succeeded || string.IsNullOrWhiteSpace(salida.Json))
            return conversacion is null
                ? new ConversationDto(
                    null, null, false, [],
                    salida.Error ?? "El asistente no pudo responder.", Restantes(uso))
                : Map(conversacion, salida.Error, Restantes(uso));

        if (conversacion is null)
        {
            conversacion = new AssistantConversation
            {
                WorkOrderId = workOrderId,
                StartedByUserId = tenantContext.UserId,
                Title = pregunta.Length <= 200 ? pregunta : pregunta[..200]
            };

            db.AssistantConversations.Add(conversacion);
        }

        // Se dan de alta explícitamente en vez de solo colgarlos de la conversación.
        // `BaseEntity` pone el Id al construir, así que la fila nueva ya trae clave; bajo una
        // conversación que ya existe, EF lo toma por una edición y emite UPDATE en vez de
        // INSERT. Ese UPDATE no encuentra nada y revienta con «alguien más cambió esto»  —es
        // el mismo tropiezo que dio «Aplica a» en el inventario—.
        var pregunto = new AssistantMessage
        {
            Conversation = conversacion, FromUser = true, Content = pregunta
        };

        var contesto = new AssistantMessage
        {
            Conversation = conversacion,
            FromUser = false,
            Content = salida.Json,
            AssistantCallId = llamadaId
        };

        db.AssistantMessages.AddRange(pregunto, contesto);
        conversacion.Messages.Add(pregunto);
        conversacion.Messages.Add(contesto);

        if (conversacion.Messages.Count(m => m.FromUser) >= MaximoTurnos)
            conversacion.IsClosed = true;

        await db.SaveChangesAsync(ct);

        return Map(conversacion,
            conversacion.IsClosed
                ? "Hasta aquí llega esta consulta. Si le queda duda, abra una nueva con la pregunta concreta."
                : null,
            Restantes(uso));
    }

    /// <summary>
    /// La conversación hasta ahora, más la pregunta nueva. Va en la parte que cambia del
    /// prompt, no en el contexto: así las instrucciones y el historial se leen de caché.
    /// </summary>
    private static string Conversacion(AssistantConversation? conversacion, string pregunta)
    {
        if (conversacion is null or { Messages.Count: 0 }) return pregunta;

        var sb = new StringBuilder();

        foreach (var m in conversacion.Messages.OrderBy(m => m.CreatedAt))
            sb.AppendLine(m.FromUser ? $"Técnico: {m.Content}" : $"Vos: {m.Content}").AppendLine();

        sb.Append($"Técnico: {pregunta}");

        return sb.ToString();
    }

    /// <summary>
    /// Qué le hizo este taller al mismo modelo. Es lo que el manual no tiene.
    /// </summary>
    /// <remarks>
    /// Solo órdenes <b>entregadas</b> y con diagnóstico escrito: una orden abierta todavía no
    /// enseñó nada, y una sin diagnóstico no dice por qué se hizo lo que se hizo. Sin marca no
    /// se busca: traer «todas las órdenes del taller» como historial sería ruido caro.
    /// </remarks>
    private async Task<string> HistorialAsync(
        AssistantSubjectDto sujeto, Guid excluir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sujeto.Brand)) return string.Empty;

        var marca = sujeto.Brand.Trim().ToLower();
        var modelo = sujeto.Model?.Trim().ToLower();

        var ordenes = await db.WorkOrders
            .AsNoTracking()
            .Where(o => o.Id != excluir
                && o.Status == WorkOrderStatus.Delivered
                && o.Diagnosis != null
                && o.Vehicle != null
                && o.Vehicle.Brand.ToLower() == marca
                && (modelo == null || o.Vehicle.Model.ToLower() == modelo))
            .OrderByDescending(o => o.ClosedAt)
            .Take(MaximoHistorial)
            .Select(o => new
            {
                o.Number,
                o.ClosedAt,
                o.Diagnosis,
                Anio = o.Vehicle!.Year,
                Pasos = o.Tasks.OrderBy(t => t.Sequence).Select(t => t.Title).ToList(),
                // Del catálogo sale el nombre del repuesto; de una línea escrita a mano, su
                // descripción. Las dos cuentan: lo que se compró afuera también se cambió.
                Repuestos = o.Parts
                    .Select(p => p.Part != null ? p.Part.Name : p.Description)
                    .Where(n => n != null)
                    .ToList()
            })
            .ToListAsync(ct);

        if (ordenes.Count == 0) return string.Empty;

        var sb = new StringBuilder();

        foreach (var o in ordenes)
        {
            sb.AppendLine($"### Orden {o.Number} · {o.ClosedAt:dd/MM/yyyy}"
                + (o.Anio is { } anio ? $" · {anio}" : ""));
            sb.AppendLine($"Diagnóstico: {o.Diagnosis}");

            if (o.Pasos.Count > 0) sb.AppendLine($"Se hizo: {string.Join("; ", o.Pasos)}");
            if (o.Repuestos.Count > 0) sb.AppendLine($"Se cambió: {string.Join("; ", o.Repuestos)}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string?> CodigosAsync(Guid workOrderId, CancellationToken ct)
    {
        var ultima = await db.VehicleScans
            .AsNoTracking()
            .Where(s => s.WorkOrderId == workOrderId)
            .OrderByDescending(s => s.ScannedAt)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (ultima == Guid.Empty) return null;

        var codigos = await db.VehicleScanCodes
            .AsNoTracking()
            .Where(c => c.VehicleScanId == ultima)
            .OrderBy(c => c.Code)
            .Select(c => c.Description == null ? c.Code : c.Code + " (" + c.Description + ")")
            .ToListAsync(ct);

        return codigos.Count == 0 ? null : string.Join("; ", codigos);
    }

    private async Task<AssistantConversation> BuscarAsync(Guid id, CancellationToken ct)
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer) throw new ForbiddenException();

        var conversacion = await db.AssistantConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Esa consulta no existe.");

        // La consulta de otro técnico no es suya, y decirle que existe ya sería decirle algo.
        if (scope.IsTechnician && conversacion.StartedByUserId != scope.UserId)
            throw new NotFoundException("Esa consulta no existe.");

        return conversacion;
    }

    private async Task EnsureEnabledAsync(CancellationToken ct)
    {
        var encendido = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.AssistantEnabled)
            .FirstOrDefaultAsync(ct);

        if (!encendido) throw new NotFoundException();
    }

    private static ConversationDto Map(
        AssistantConversation c, string? mensaje, int restantes) =>
        new(c.Id, c.Title, c.IsClosed,
            c.Messages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ConversationTurnDto(m.FromUser, m.Content, m.CreatedAt))
                .ToList(),
            mensaje,
            restantes);

    private static int Restantes(AssistantUsageDto uso) =>
        Math.Max(0, uso.CreditsPerDay - uso.CreditsToday);
}
