namespace Garaj.Application.Common;

/// <summary>
/// Lo que dejó —o dejaría— la carga de un catálogo desde Excel.
/// </summary>
/// <remarks>
/// La misma respuesta sirve para la vista previa y para la carga de verdad: el taller sube el
/// archivo, ve fila por fila qué va a entrar y qué está mal escrito, y recién entonces guarda.
/// Cargar a ciegas 300 renglones y descubrir después que la mitad tenía el precio en blanco es
/// peor que no cargar nada.
/// </remarks>
/// <param name="Applied">Falso en la vista previa: nada se guardó todavía.</param>
public record ImportResultDto(
    bool Applied,
    int Total,
    int Created,
    int Updated,
    int Failed,
    IReadOnlyList<ImportRowDto> Rows);

/// <param name="Row">La fila del Excel, para que quien lo llenó la encuentre.</param>
/// <param name="Action">`nuevo`, `actualiza` o `error`.</param>
public record ImportRowDto(
    int Row,
    string Key,
    string Name,
    string Action,
    string? Error);

/// <summary>Lo que las dos cargas —repuestos y mano de obra— hacen igual.</summary>
public static class Importaciones
{
    /// <summary>
    /// Cuántas filas se pueden cargar de una vez.
    ///
    /// No es el tope de lo que aguanta —cinco mil entran en un par de segundos—, sino el
    /// punto donde un archivo deja de ser un catálogo y es otra cosa: una exportación entera
    /// de otro sistema, o el archivo equivocado. Mejor decirlo que tardar un minuto.
    /// </summary>
    public const int MaximoDeFilas = 5_000;

    /// <summary>Cuántas filas de cada clase viajan de vuelta. El resto va en los totales.</summary>
    public const int Muestra = 200;

    public static void EnsureCabe(int filas)
    {
        if (filas > MaximoDeFilas)
            throw new AppException(
                $"El archivo trae {filas:N0} filas y se cargan hasta {MaximoDeFilas:N0} por vez. "
                + "Pártalo en dos y súbalos uno tras otro.");
    }

    /// <summary>
    /// La respuesta con una muestra, no con el archivo entero: con tres mil filas, pintar
    /// tres mil renglones en pantalla no le dice nada a nadie y pone lenta la página. Los
    /// errores van primero porque son los únicos que hay que leer uno por uno.
    /// </summary>
    public static ImportResultDto Recortado(
        bool applied, int total, int creados, int actualizados,
        List<ImportRowDto> errores, List<ImportRowDto> buenas) =>
        new(applied, total, creados, actualizados, errores.Count,
            [.. errores.Take(Muestra), .. buenas.Take(Muestra)]);
}
