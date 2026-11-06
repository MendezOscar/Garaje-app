using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Messages;
using Garaj.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// El cliente del modelo. Es lo único de todo el sistema que habla con el proveedor.
/// </summary>
/// <remarks>
/// La app y el panel nunca llaman a la API del modelo: llaman a GarajApp y GarajApp llama al
/// modelo. Es la misma regla que ya rige para el almacenamiento y para Firebase, y por el
/// mismo motivo —una llave dentro de un .ipa es una llave pública—.
///
/// El orden de lo que se manda importa y no es decorativo: instrucciones, luego catálogo,
/// luego la pregunta. El caché del proveedor funciona por prefijo, así que lo estable va
/// primero y lo que cambia en cada llamada va al final. La prueba lo confirmó: con el catálogo
/// cacheado la entrada bajó de ~3,700 tokens a ~70.
/// </remarks>
public class AnthropicAssistant(
    IOptions<AssistantOptions> options,
    ILogger<AnthropicAssistant> logger) : IAssistant
{
    private readonly AssistantOptions _options = options.Value;

    /// <summary>
    /// Se crea una vez: el cliente mantiene su pool de conexiones HTTP, y rehacerlo en cada
    /// llamada desperdicia un handshake TLS contra el proveedor.
    /// </summary>
    private readonly Lazy<AnthropicClient> _client = new(() => new AnthropicClient
    {
        ApiKey = options.Value.ApiKey,
        Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds)
    });

    public async Task<AssistantResult> AskAsync(AssistantPrompt prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return Fallo("El asistente no está configurado en este servidor.");

        var reloj = Stopwatch.StartNew();

        try
        {
            var respuesta = await _client.Value.Messages.Create(new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = prompt.MaxTokens,

                // Las instrucciones y el catálogo van aquí, y los dos con marca de caché: son
                // lo que se repite entre una consulta y la siguiente.
                System = new List<TextBlockParam>
                {
                    new() { Text = prompt.Instructions, CacheControl = new CacheControlEphemeral() },
                    new() { Text = prompt.Context, CacheControl = new CacheControlEphemeral() }
                },

                // Esquema estricto: no texto que después haya que interpretar. Lo que no cuadre
                // con el esquema no llega, y lo que llegue se valida igual contra el catálogo.
                OutputConfig = prompt.Schema is null
                    ? new OutputConfig { Effort = Effort.Medium }
                    : new OutputConfig
                    {
                        Effort = Effort.Medium,
                        Format = new JsonOutputFormat { Schema = prompt.Schema }
                    },

                Messages = [new UserMessageParam { Content = prompt.Question }]
            }, cancellationToken: ct);

            reloj.Stop();

            var uso = new AssistantUsage(
                (int)respuesta.Usage.InputTokens,
                (int)respuesta.Usage.OutputTokens,
                (int)(respuesta.Usage.CacheReadInputTokens ?? 0),
                (int)(respuesta.Usage.CacheCreationInputTokens ?? 0));

            var costo = Costo(uso);

            // El modelo puede negarse. No es un error nuestro ni del taller, pero sí consumió
            // tokens: se devuelve como fallo y se registra igual, con lo que costó.
            if (respuesta.StopReason == "refusal")
            {
                logger.LogWarning("El modelo se negó a responder: {Detalle}", respuesta.StopDetails?.Explanation);
                return new AssistantResult(
                    false, null, "El asistente no pudo atender esta consulta.",
                    _options.Model, uso, costo, (int)reloj.ElapsedMilliseconds);
            }

            var texto = string.Concat(
                respuesta.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));

            if (string.IsNullOrWhiteSpace(texto))
                return new AssistantResult(
                    false, null, "El asistente devolvió una respuesta vacía.",
                    _options.Model, uso, costo, (int)reloj.ElapsedMilliseconds);

            return new AssistantResult(
                true, texto, null, _options.Model, uso, costo, (int)reloj.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            reloj.Stop();

            // Una llamada que revienta a mitad también consumió tokens, pero el proveedor no
            // nos dice cuántos. Se registra con consumo cero y costo cero, sabiendo que eso
            // subestima: es la razón por la que debajo de los créditos hay un techo en dinero.
            logger.LogError(ex, "Falló la llamada al asistente");
            return new AssistantResult(
                false, null, "El asistente no está disponible en este momento.",
                _options.Model, AssistantUsage.Empty, 0m, (int)reloj.ElapsedMilliseconds);
        }
    }

    /// <summary>Lo que costó de verdad, con las tarifas del modelo que se configuró.</summary>
    private decimal Costo(AssistantUsage uso) =>
        (uso.Input * _options.InputUsdPerMTok
         + uso.Output * _options.OutputUsdPerMTok
         + uso.CacheRead * _options.CacheReadUsdPerMTok
         + uso.CacheWrite * _options.CacheWriteUsdPerMTok) / 1_000_000m;

    private AssistantResult Fallo(string mensaje) =>
        new(false, null, mensaje, _options.Model, AssistantUsage.Empty, 0m, 0);
}
