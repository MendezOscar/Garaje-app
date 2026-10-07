namespace Garaj.Domain.Rules;

/// <summary>
/// Hasta cuándo se puede deshacer lo ya cerrado: anular una factura y reabrir una orden
/// entregada.
/// </summary>
/// <remarks>
/// Los plazos no son por comodidad del taller, son por el mes fiscal. Una factura con CAI
/// anulada después de declarar el ISV ya no se arregla anulando —se arregla con nota de
/// crédito, que este sistema no emite—, así que el corte es el fin del mes en que se emitió.
/// El comprobante sin CAI no declara nada: ahí el único riesgo es el descuadre de caja, y 30
/// días alcanzan de sobra.
///
/// Para reabrir se usa la garantía que el taller le dio al trabajo: si el vehículo volvió
/// dentro de garantía es la misma orden, y fuera de garantía es trabajo nuevo. Con la garantía
/// en cero —que es como viene el ajuste— quedarían cero días y nadie podría corregir el error
/// de ayer, así que hay un piso de 15 días.
/// </remarks>
public static class CorreccionDePostventa
{
    /// <summary>La hora de Honduras, sin depender de la base de zonas horarias del contenedor.</summary>
    private static readonly TimeSpan DesfaseLocal = TimeSpan.FromHours(-6);

    /// <summary>Días para anular un comprobante que no lleva CAI.</summary>
    public const int DiasParaAnularSinCai = 30;

    /// <summary>Piso de días para reabrir, aunque el taller no dé garantía.</summary>
    public const int DiasMinimosParaReabrir = 15;

    /// <summary>
    /// Hasta cuándo se puede anular. Con CAI, el último instante del mes en que se emitió;
    /// sin CAI, 30 días después de la venta.
    /// </summary>
    public static DateTimeOffset LimiteParaAnular(DateTimeOffset fechaDeVenta, bool esFiscal)
    {
        if (!esFiscal) return fechaDeVenta.AddDays(DiasParaAnularSinCai);

        var local = fechaDeVenta.ToOffset(DesfaseLocal);
        return new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, DesfaseLocal).AddMonths(1);
    }

    public static bool SePuedeAnular(DateTimeOffset fechaDeVenta, bool esFiscal, DateTimeOffset ahora) =>
        ahora < LimiteParaAnular(fechaDeVenta, esFiscal);

    /// <summary>Hasta cuándo se puede reabrir una orden entregada.</summary>
    public static DateTimeOffset LimiteParaReabrir(DateTimeOffset entregadaEl, int diasDeGarantia) =>
        entregadaEl.AddDays(Math.Max(diasDeGarantia, DiasMinimosParaReabrir));

    public static bool SePuedeReabrir(DateTimeOffset entregadaEl, int diasDeGarantia, DateTimeOffset ahora) =>
        ahora < LimiteParaReabrir(entregadaEl, diasDeGarantia);
}
