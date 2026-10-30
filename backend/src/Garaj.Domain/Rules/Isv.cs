namespace Garaj.Domain.Rules;

/// <summary>
/// Qué significa el precio del catálogo frente al ISV. Son dos modelos, y cada taller usa uno.
///
/// <b>Con el impuesto dentro</b> —lo normal y lo que viene puesto— el precio es lo que el
/// cliente paga: así cotiza un taller por teléfono, «el cambio de aceite son mil doscientos»,
/// y así lo entiende quien recibe la factura. Al desglosarlo, el impuesto se saca hacia atrás
/// en vez de sumarse encima, y el total es el mismo esté o no desglosado.
///
/// <b>Con el impuesto aparte</b>, el precio es la base y el ISV se suma al facturar: es el
/// «¿con factura o sin factura?» del mostrador, donde con factura se paga más.
///
/// Lo que nunca puede pasar es que el modelo cambie entre una venta y otra: el mismo repuesto
/// tendría dos precios y ni la cotización ni el margen cuadrarían. Por eso lo elige el taller
/// una sola vez, y cada venta y cada cotización se guardan con el modelo que usaron.
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

    /// <summary>El impuesto que se le suma a una base, cuando el precio no lo lleva adentro.</summary>
    public static decimal Sobre(decimal baseImponible, decimal tasaPorCiento) =>
        tasaPorCiento <= 0
            ? 0m
            : Math.Round(baseImponible * tasaPorCiento / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Lo que se cobra y cuánto de eso es impuesto, a partir de lo que suman las líneas.
    /// Es el único lugar donde se decide: la venta y la cotización llaman aquí.
    /// </summary>
    /// <param name="precioConImpuesto">
    /// Si el precio del catálogo ya lleva el ISV adentro. El taller lo elige una vez.
    /// </param>
    public static (decimal Total, decimal Impuesto) Totales(
        decimal suma, decimal tasaPorCiento, bool precioConImpuesto)
    {
        if (tasaPorCiento <= 0) return (suma, 0m);

        if (precioConImpuesto) return (suma, Contenido(suma, tasaPorCiento));

        var impuesto = Sobre(suma, tasaPorCiento);
        return (suma + impuesto, impuesto);
    }
}
