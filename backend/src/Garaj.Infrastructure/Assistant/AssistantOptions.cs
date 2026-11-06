namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// Configuración del asistente. La llave entra por variable de entorno —<c>Assistant__ApiKey</c>
/// en Render— y <b>no se escribe en el repositorio, que es público</b>.
/// </summary>
public class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>Vacía apaga el asistente entero, sin importar lo que diga cada taller.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// El modelo. Se deja en configuración porque el plan tiene escrita una condición de
    /// cambio: si el costo medido por taller pasa del número acordado, una función baja de
    /// modelo. Eso tiene que ser una variable, no un despliegue.
    /// </summary>
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Tarifas del modelo, en dólares por millón de tokens. Al 6 de noviembre de 2026.</summary>
    public decimal InputUsdPerMTok { get; set; } = 4.00m;

    public decimal OutputUsdPerMTok { get; set; } = 20.00m;
    public decimal CacheReadUsdPerMTok { get; set; } = 0.20m;

    /// <summary>Escribir en caché cuesta más que la entrada normal; se lee mucho más barato.</summary>
    public decimal CacheWriteUsdPerMTok { get; set; } = 5.00m;

    /// <summary>A cómo se pasa el dólar a lempiras para contar el tope del taller.</summary>
    public decimal HnlPerUsd { get; set; } = 26m;

    /// <summary>
    /// Tope de gasto mensual de <b>toda la plataforma</b>, en dólares. Al llegar se apaga para
    /// todos.
    /// </summary>
    /// <remarks>
    /// El escenario que arruina un mes no es un taller curioso, es un error que reintenta en
    /// bucle de madrugada. Este techo no depende de que ningún taller esté bien configurado.
    /// Aparte de este, en la consola del proveedor va un límite de gasto: ese es el único
    /// candado que no depende de que el código esté bien.
    /// </remarks>
    public decimal MonthlyCapUsd { get; set; } = 400m;

    /// <summary>Segundos antes de darse por vencido con una llamada.</summary>
    public int TimeoutSeconds { get; set; } = 90;
}
