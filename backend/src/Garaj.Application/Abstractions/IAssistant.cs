using System.Text.Json;

namespace Garaj.Application.Abstractions;

/// <summary>
/// La única puerta hacia el modelo. Nadie más habla con el proveedor.
/// </summary>
/// <remarks>
/// Va detrás de una interfaz por lo mismo que <see cref="IStorageService"/>: cambiar de modelo
/// o de proveedor tiene que ser configuración, no reescribir. Y porque el plan deja escrita
/// una condición de cambio —si el costo medido por taller pasa de cierto número, una de las
/// funciones baja de modelo—, que sin esto sería tocar código de negocio.
///
/// La implementación <b>no decide</b> si se puede llamar ni cuánto se cobra: eso es de
/// <c>IAssistantQuota</c>. Esta solo llama y reporta qué costó.
/// </remarks>
public interface IAssistant
{
    /// <summary>
    /// Llama al modelo con salida estructurada y devuelve el JSON tal cual vino, sin validar
    /// contra el catálogo —eso es del servicio que lo pidió, y hay que hacerlo como si la
    /// respuesta viniera de un cliente hostil, porque a efectos prácticos viene de afuera—.
    /// </summary>
    /// <remarks>
    /// No lanza por un fallo del proveedor: devuelve el resultado con
    /// <see cref="AssistantResult.Succeeded"/> en false y lo que se sepa del consumo. Quien
    /// llama tiene que registrar también lo que falló, porque se paga igual.
    /// </remarks>
    Task<AssistantResult> AskAsync(AssistantPrompt prompt, CancellationToken ct = default);
}

/// <param name="Instructions">
/// Las reglas. Va primero y se cachea: el caché solo sirve si el prefijo no cambia.
/// </param>
/// <param name="Context">
/// El material de referencia —el catálogo filtrado del taller—. Va después de las
/// instrucciones y antes de la pregunta, también cacheado, y por eso tiene que armarse
/// siempre en el mismo orden: un diccionario sin ordenar invalida el caché sin avisar.
/// </param>
/// <param name="Question">El caso concreto. Es lo único que cambia entre llamadas.</param>
/// <param name="Schema">
/// El esquema JSON de la respuesta. Estricto a propósito: texto que después haya que
/// interpretar es exactamente lo que no se quiere.
/// </param>
public record AssistantPrompt(
    string Instructions,
    string Context,
    string Question,
    IReadOnlyDictionary<string, JsonElement> Schema,
    int MaxTokens = 4000);

/// <param name="Json">La respuesta cruda. Null si la llamada falló.</param>
/// <param name="CostUsd">Lo que costó de verdad, con la tarifa del modelo que se usó.</param>
public record AssistantResult(
    bool Succeeded,
    string? Json,
    string? Error,
    string Model,
    AssistantUsage Usage,
    decimal CostUsd,
    int ElapsedMs);

/// <param name="CacheRead">
/// Lo que se leyó de caché en vez de volver a mandarse. Es la mitad del argumento de costo:
/// con el catálogo cacheado la entrada medida bajó de ~3,700 tokens a ~70.
/// </param>
public record AssistantUsage(int Input, int Output, int CacheRead, int CacheWrite)
{
    public static readonly AssistantUsage Empty = new(0, 0, 0, 0);
}
