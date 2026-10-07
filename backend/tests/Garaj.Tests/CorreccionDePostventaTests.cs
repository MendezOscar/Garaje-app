using Garaj.Domain.Rules;

namespace Garaj.Tests;

/// <summary>
/// Los plazos para deshacer lo ya cerrado. Se prueban los bordes, que es donde se cae: el
/// último instante del mes de la factura, el primero del mes siguiente, y el día exacto en
/// que vence la garantía.
/// </summary>
public class CorreccionDePostventaTests
{
    private static DateTimeOffset Local(int año, int mes, int día, int hora = 12) =>
        new(año, mes, día, hora, 0, 0, TimeSpan.FromHours(-6));

    // ---------- Anular ----------

    [Fact]
    public void La_factura_con_cai_se_anula_el_mismo_dia()
    {
        var venta = Local(2026, 10, 7);
        Assert.True(CorreccionDePostventa.SePuedeAnular(venta, esFiscal: true, venta.AddHours(3)));
    }

    [Fact]
    public void La_factura_con_cai_se_anula_hasta_el_ultimo_minuto_del_mes()
    {
        var venta = Local(2026, 10, 7);
        Assert.True(CorreccionDePostventa.SePuedeAnular(
            venta, esFiscal: true, Local(2026, 10, 31, 23)));
    }

    [Fact]
    public void La_factura_con_cai_del_mes_pasado_ya_no_se_anula()
    {
        var venta = Local(2026, 10, 7);
        Assert.False(CorreccionDePostventa.SePuedeAnular(
            venta, esFiscal: true, Local(2026, 11, 1, 0)));
    }

    [Fact]
    public void El_comprobante_sin_cai_aguanta_treinta_dias()
    {
        var venta = Local(2026, 10, 7);

        Assert.True(CorreccionDePostventa.SePuedeAnular(
            venta, esFiscal: false, venta.AddDays(29)));
        Assert.False(CorreccionDePostventa.SePuedeAnular(
            venta, esFiscal: false, venta.AddDays(31)));
    }

    [Fact]
    public void El_comprobante_sin_cai_cruza_el_mes_sin_problema()
    {
        // La fecha fiscal no lo limita: lo único que cuentan son sus 30 días.
        var venta = Local(2026, 10, 25);
        Assert.True(CorreccionDePostventa.SePuedeAnular(
            venta, esFiscal: false, Local(2026, 11, 10)));
    }

    // ---------- Reabrir ----------

    [Fact]
    public void Se_reabre_dentro_de_la_garantia_que_se_dio()
    {
        var entrega = Local(2026, 10, 1);

        Assert.True(CorreccionDePostventa.SePuedeReabrir(entrega, 90, entrega.AddDays(80)));
        Assert.False(CorreccionDePostventa.SePuedeReabrir(entrega, 90, entrega.AddDays(91)));
    }

    [Fact]
    public void Sin_garantia_quedan_los_quince_dias_de_piso()
    {
        // Con el ajuste en cero, cero días dejaría sin arreglo el error de ayer.
        var entrega = Local(2026, 10, 1);

        Assert.True(CorreccionDePostventa.SePuedeReabrir(entrega, 0, entrega.AddDays(14)));
        Assert.False(CorreccionDePostventa.SePuedeReabrir(entrega, 0, entrega.AddDays(16)));
    }

    [Fact]
    public void Una_garantia_corta_no_baja_del_piso()
    {
        var entrega = Local(2026, 10, 1);
        Assert.True(CorreccionDePostventa.SePuedeReabrir(entrega, 7, entrega.AddDays(14)));
    }
}
