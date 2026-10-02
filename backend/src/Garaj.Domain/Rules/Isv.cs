namespace Garaj.Domain.Rules;

/// <summary>
/// El ISV va **dentro** del precio, no encima.
///
/// El precio del catálogo es lo que el cliente paga: así es como un taller cotiza por teléfono
/// —«el cambio de aceite son mil doscientos»— y así es como lo entiende quien recibe la
/// factura. Cuando el documento lo desglosa, el impuesto se saca hacia atrás en vez de sumarse
/// encima, y por eso el total es el mismo esté o no desglosado.
///
/// Antes se sumaba al final, y una venta de L 1,000 terminaba cobrando L 1,150: el dueño decía
/// un precio y el papel decía otro.
/// </summary>
public static class Isv
{
    /// <summary>
    /// El importe sin impuesto: lo cobrado entre uno más la tasa. La tasa viene en por ciento
    /// —15, no 0,15— que es como la guarda el taller.
    /// </summary>
    public static decimal Base(decimal bruto, decimal tasaPorCiento) =>
        tasaPorCiento <= 0
            ? bruto
            : Math.Round(bruto / (1 + tasaPorCiento / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// El impuesto contenido. Se calcula como resta y no como producto para que base más
    /// impuesto dé exactamente lo cobrado, sin un centavo de diferencia que después nadie
    /// puede explicar en el cuadre.
    /// </summary>
    public static decimal Contenido(decimal bruto, decimal tasaPorCiento) =>
        bruto - Base(bruto, tasaPorCiento);
}
