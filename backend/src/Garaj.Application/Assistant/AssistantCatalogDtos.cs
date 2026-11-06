using Garaj.Domain.Enums;

namespace Garaj.Application.Assistant;

/// <summary>
/// Arma el material que ve el modelo: lo que el taller tiene y le sirve a <b>este</b> trabajo.
/// </summary>
/// <remarks>
/// Es el trabajo real de la dirección 1, no la llamada al modelo. Mandar los 400 repuestos del
/// taller en cada consulta es caro y además empeora la respuesta; y un modelo al que se le deja
/// escribir el nombre del repuesto termina ofreciendo repuestos que el taller no tiene. Por eso
/// de aquí salen ids, y por eso sale filtrado.
///
/// Nada de lo que devuelve lleva precios: las cifras las pone el catálogo del taller al armar
/// la propuesta, no el modelo.
/// </remarks>
public interface IAssistantCatalog
{
    /// <summary>
    /// El catálogo que le sirve a esta orden. Sale ordenado de forma estable —por código— para
    /// que el prefijo no cambie entre llamadas: si cambia, el caché deja de servir y no avisa.
    /// </summary>
    Task<AssistantCatalogDto> ForWorkOrderAsync(Guid workOrderId, CancellationToken ct = default);
}

/// <param name="Kind">
/// Si el taller trabaja con vehículos o con equipos. De aquí sale cómo se le habla: en un
/// taller de equipos no se dice «el carro», y la mitad de los clientes no son de vehículos.
/// </param>
public record AssistantCatalogDto(
    WorkshopKind Kind,
    AssistantSubjectDto Subject,
    IReadOnlyList<AssistantPartDto> Parts,
    IReadOnlyList<AssistantServiceDto> Services);

/// <summary>
/// Lo que entró al taller, descrito sin nada que identifique a su dueño: ni nombre, ni
/// teléfono, ni RTN, ni dirección, <b>ni la placa</b>. Nada de eso hace falta para que salga
/// una lista de pasos.
/// </summary>
public record AssistantSubjectDto(
    string? Brand, string? Model, int? Year, int? Mileage, string? Engine);

public record AssistantPartDto(
    Guid Id, string Sku, string Name, string? Brand, string? Category, string Unit);

public record AssistantServiceDto(
    Guid Id, string Code, string Name, string? Category, decimal StandardHours);
