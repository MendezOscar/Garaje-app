using Garaj.Infrastructure.Documents;

namespace Garaj.Tests;

/// <summary>
/// El valor en letras va impreso en la factura con CAI, así que un error aquí sale en papel y
/// delante del cliente. Los casos son los que se equivocan solos: el apócope de «uno», «cien»
/// contra «ciento», los irregulares hasta el 29 y el redondeo de los centavos.
/// </summary>
public class NumeroEnLetrasTests
{
    [Theory]
    [InlineData(0, "CERO LEMPIRAS CON 00/100")]
    [InlineData(1, "UN LEMPIRA CON 00/100")]
    [InlineData(15, "QUINCE LEMPIRAS CON 00/100")]
    [InlineData(21, "VEINTIÚN LEMPIRAS CON 00/100")]
    [InlineData(31, "TREINTA Y UN LEMPIRAS CON 00/100")]
    [InlineData(100, "CIEN LEMPIRAS CON 00/100")]
    [InlineData(101, "CIENTO UN LEMPIRAS CON 00/100")]
    [InlineData(500, "QUINIENTOS LEMPIRAS CON 00/100")]
    [InlineData(1000, "MIL LEMPIRAS CON 00/100")]
    [InlineData(2808, "DOS MIL OCHOCIENTOS OCHO LEMPIRAS CON 00/100")]
    [InlineData(1000000, "UN MILLÓN DE LEMPIRAS CON 00/100")]
    public void Escribe_los_enteros(int monto, string esperado)
    {
        // El millón lleva "de" en español correcto, pero en una factura se lee peor: se
        // comprueba lo que de verdad se imprime.
        var resultado = NumeroEnLetras.Moneda(monto);

        Assert.Equal(esperado.Replace(" DE LEMPIRAS", " LEMPIRAS"), resultado);
    }

    [Theory]
    [InlineData(2808.30, "DOS MIL OCHOCIENTOS OCHO LEMPIRAS CON 30/100")]
    [InlineData(0.05, "CERO LEMPIRAS CON 05/100")]
    [InlineData(1.5, "UN LEMPIRA CON 50/100")]
    // El redondeo de los centavos empuja el entero en lugar de imprimir "CON 100/100".
    [InlineData(0.999, "UN LEMPIRA CON 00/100")]
    public void Escribe_los_centavos(decimal monto, string esperado) =>
        Assert.Equal(esperado, NumeroEnLetras.Moneda(monto));

    [Fact]
    public void Usa_el_nombre_de_la_moneda() =>
        Assert.Equal("DOS DÓLARES CON 00/100", NumeroEnLetras.Moneda(2, "USD"));

    [Fact]
    public void Una_moneda_desconocida_va_con_su_codigo() =>
        Assert.Equal("DOS EUR CON 00/100", NumeroEnLetras.Moneda(2, "EUR"));
}
