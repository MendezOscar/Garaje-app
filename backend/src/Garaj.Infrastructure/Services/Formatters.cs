using System.Text.RegularExpressions;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Deja el teléfono en E.164 sin '+' porque es lo que exige el link `wa.me`. Si se guardaran
/// tal como los teclea el mostrador ("9888-1111", "+504 9888 1111"), la mitad de las
/// cotizaciones abriría un chat con un número inválido.
/// </summary>
public static partial class PhoneFormatter
{
    /// <summary>Honduras. Se usa cuando el número llega con los 8 dígitos locales y sin país.</summary>
    private const string DefaultCountryCode = "504";

    private const int LocalNumberLength = 8;

    public static string Normalize(string phone)
    {
        var digits = NonDigits().Replace(phone ?? string.Empty, string.Empty);

        if (digits.Length == 0) return string.Empty;

        // "00504..." es el prefijo internacional que marcan algunos teléfonos fijos.
        if (digits.StartsWith("00")) digits = digits[2..];

        return digits.Length == LocalNumberLength ? DefaultCountryCode + digits : digits;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}

/// <summary>
/// Normaliza la placa a mayúsculas sin separadores. El mostrador la teclea como sea
/// ("pbh-1234", "PBH 1234") y la búsqueda tiene que encontrarla igual.
/// </summary>
public static partial class PlateFormatter
{
    public static string Normalize(string? plate) =>
        NonAlphanumeric().Replace(plate ?? string.Empty, string.Empty).ToUpperInvariant();

    [GeneratedRegex(@"[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
