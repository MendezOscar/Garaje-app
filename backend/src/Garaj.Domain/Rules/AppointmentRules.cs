namespace Garaj.Domain.Rules;

/// <summary>
/// Las dos reglas de una cita que no dependen de la base: hasta cuándo se agenda y qué es un
/// teléfono utilizable.
/// </summary>
/// <remarks>
/// Viven aquí, fuera del servicio, porque las aplica también el enlace público —donde no hay
/// sesión, ni taller resuelto, ni nada en que apoyarse— y porque son justo lo que hay que
/// poder probar sin levantar un Postgres.
/// </remarks>
public static class AppointmentRules
{
    /// <summary>Cuánto hacia adelante se agenda. Más allá, el taller no sabe ni quién trabaja.</summary>
    public const int DiasQueSeAgendan = 60;

    /// <summary>Citas abiertas que se le aceptan a un mismo número. Es el freno del enlace.</summary>
    public const int CitasAbiertasPorTelefono = 3;

    /// <summary>Dígitos mínimos de un teléfono para que sirva para devolverle el mensaje.</summary>
    public const int DigitosDeUnTelefono = 8;

    /// <summary>
    /// Si ese día se puede apartar. El «hoy» se recibe —no se toma del reloj— porque el día
    /// del taller es el de Honduras y el del servidor es UTC: en UTC, «hoy» cambia a las seis
    /// de la tarde de acá y la cita de hoy dejaría de poderse pedir desde el mediodía.
    /// </summary>
    public static bool EsAgendable(DateOnly dia, DateOnly hoy) =>
        dia >= hoy && dia <= hoy.AddDays(DiasQueSeAgendan);

    /// <summary>
    /// El teléfono en dígitos, como lo quiere WhatsApp. Devuelve null cuando lo que llegó no
    /// sirve para contestarle, que es lo único que de verdad hace falta de una cita.
    /// </summary>
    public static string? Telefono(string? valor)
    {
        var limpio = new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
        return limpio.Length < DigitosDeUnTelefono ? null : limpio;
    }
}
