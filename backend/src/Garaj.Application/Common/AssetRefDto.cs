using Garaj.Domain.Enums;

namespace Garaj.Application.Common;

/// <summary>
/// Lo que entró al taller, visto igual por la pantalla sea un vehículo o un equipo. Las
/// órdenes y los requerimientos lo llevan al lado de los campos de vehículo de siempre, que
/// no se quitan: la versión de la app que está publicada los lee, y una app instalada no se
/// actualiza porque nosotros despleguemos.
/// </summary>
/// <param name="Label">"Honda CB125F" o "Dell Latitude 5490".</param>
/// <param name="Identifier">La placa, o la serie —y si no hay serie, la etiqueta de bodega—.</param>
/// <param name="Category">Qué es: "Moto", "Laptop". El subtítulo de la tarjeta.</param>
public record AssetRefDto(
    AssetKind Kind,
    Guid Id,
    string Label,
    string? Identifier,
    string? Category);
