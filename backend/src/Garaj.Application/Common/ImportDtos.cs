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
