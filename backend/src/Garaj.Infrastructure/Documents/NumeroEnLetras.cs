using System.Globalization;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// El importe escrito con palabras, como exige el régimen de facturación: «DOS MIL OCHOCIENTOS
/// OCHO LEMPIRAS CON 30/100».
///
/// Se escribe a mano en lugar de traer una dependencia: son cuatro reglas del español —el
/// apócope de «uno», «cien» contra «ciento», los irregulares del 16 al 29 y las centenas— y
/// una biblioteca genérica traería veinte idiomas para no usar ninguno.
/// </summary>
public static class NumeroEnLetras
{
    private static readonly string[] Unidades =
    [
        "CERO", "UNO", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
        "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE",
        "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS",
        "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"
    ];

    private static readonly string[] Decenas =
    [
        "", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA",
        "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"
    ];

    private static readonly string[] Centenas =
    [
        "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS",
        "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"
    ];

    /// <summary>
    /// «DOS MIL OCHOCIENTOS OCHO LEMPIRAS CON 30/100». Los centavos van en cifras porque así
    /// se imprimen en los talonarios y así los lee quien revisa.
    /// </summary>
    public static string Moneda(decimal amount, string currency = "HNL")
    {
        var negativo = amount < 0;
        amount = Math.Abs(Math.Round(amount, 2, MidpointRounding.AwayFromZero));

        var entero = (long)Math.Truncate(amount);
        var centavos = (int)Math.Round((amount - entero) * 100, MidpointRounding.AwayFromZero);

        // El redondeo de los centavos puede empujar el entero: 0.999 → 1 lempira, 00/100.
        if (centavos == 100)
        {
            entero++;
            centavos = 0;
        }

        var unidad = Unidad(currency, entero);
        var letras = $"{Apocope(Escribir(entero))} {unidad} CON {centavos:D2}/100";

        return negativo ? $"MENOS {letras}" : letras;
    }

    /// <summary>
    /// Delante del nombre de la moneda, «uno» se apocopa: UN LEMPIRA, VEINTIÚN LEMPIRAS,
    /// TREINTA Y UN LEMPIRAS. Suelto seguiría siendo «UNO», por eso no va dentro de Escribir.
    /// </summary>
    private static string Apocope(string letras)
    {
        if (letras.EndsWith("VEINTIUNO", StringComparison.Ordinal))
            return string.Concat(letras.AsSpan(0, letras.Length - 9), "VEINTIÚN");

        return letras.EndsWith("UNO", StringComparison.Ordinal)
            ? string.Concat(letras.AsSpan(0, letras.Length - 3), "UN")
            : letras;
    }

    /// <summary>El número en palabras, sin moneda. Público para poder probarlo suelto.</summary>
    public static string Escribir(long numero)
    {
        if (numero == 0) return Unidades[0];
        if (numero < 0) return $"MENOS {Escribir(-numero)}";

        if (numero >= 1_000_000_000_000) return numero.ToString(CultureInfo.InvariantCulture);

        var partes = new List<string>();

        var millones = numero / 1_000_000;
        if (millones > 0)
        {
            partes.Add(millones == 1 ? "UN MILLÓN" : $"{Escribir(millones)} MILLONES");
            numero %= 1_000_000;
        }

        var miles = numero / 1_000;
        if (miles > 0)
        {
            // "MIL", no "UN MIL".
            partes.Add(miles == 1 ? "MIL" : $"{Escribir(miles)} MIL");
            numero %= 1_000;
        }

        if (numero > 0) partes.Add(Centena(numero));

        return string.Join(' ', partes);
    }

    private static string Centena(long numero)
    {
        // "CIEN" solo cuando está solo; con algo detrás es "CIENTO".
        if (numero == 100) return "CIEN";

        var partes = new List<string>();

        var centenas = numero / 100;
        if (centenas > 0)
        {
            partes.Add(Centenas[centenas]);
            numero %= 100;
        }

        if (numero > 0)
        {
            if (numero < 30)
            {
                partes.Add(Unidades[numero]);
            }
            else
            {
                var decenas = numero / 10;
                var unidades = numero % 10;

                partes.Add(unidades == 0
                    ? Decenas[decenas]
                    : $"{Decenas[decenas]} Y {Unidades[unidades]}");
            }
        }

        return string.Join(' ', partes);
    }

    /// <summary>
    /// El nombre de la moneda, en singular cuando es una: «UN LEMPIRA», «DOS LEMPIRAS». En una
    /// moneda desconocida se usa el código ISO tal cual.
    /// </summary>
    private static string Unidad(string currency, long cantidad) => currency.ToUpperInvariant() switch
    {
        "HNL" => cantidad == 1 ? "LEMPIRA" : "LEMPIRAS",
        "USD" => cantidad == 1 ? "DÓLAR" : "DÓLARES",
        var otra => otra
    };
}
