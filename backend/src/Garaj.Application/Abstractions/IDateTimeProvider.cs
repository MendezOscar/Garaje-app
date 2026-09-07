namespace Garaj.Application.Abstractions;

/// <summary>
/// Reloj inyectable. Necesario porque los reportes agrupan por día/semana/mes y los tests
/// tienen que poder fijar la fecha sin depender del reloj de la máquina.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public static class DateTimeProviderExtensions
{
    /// <summary>Honduras, UTC-6. Sin horario de verano desde 2006, así que es una resta fija.</summary>
    private const int HondurasOffsetHours = -6;

    /// <summary>
    /// El día de hoy en Honduras, para lo que se cuenta por días y no por instantes: el
    /// vencimiento de la suscripción.
    ///
    /// El servidor corre en UTC, y el día UTC cambia a las 6 de la tarde de acá. Contando por
    /// día UTC, un taller pagado hasta el jueves se quedaba sin poder trabajar **el jueves a
    /// las 6 de la tarde**, que además es cuando se está cerrando el día. Restando el huso,
    /// el día cambia a medianoche, que es cuando el taller cree que cambia.
    /// </summary>
    public static DateOnly Today(this IDateTimeProvider clock) =>
        DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(HondurasOffsetHours));

    /// <summary>
    /// La medianoche de hoy en Honduras, como instante y <b>en UTC</b>.
    ///
    /// Es contra esto —y no contra la hora actual— que se compara una fecha acordada de pago: un
    /// vencimiento es un día, y el cliente no está atrasado hasta que ese día termina. Comparando
    /// contra el instante, una venta acordada para hoy aparecía vencida desde la mañana.
    ///
    /// Sale en UTC porque este valor va a parar a un `WHERE` contra una columna `timestamptz`, y
    /// Npgsql se niega a mandar un <see cref="DateTimeOffset"/> con desplazamiento distinto de
    /// cero: la consulta entera revienta con un 500. El instante es el mismo.
    /// </summary>
    public static DateTimeOffset StartOfToday(this IDateTimeProvider clock)
    {
        var hoy = clock.Today();
        return new DateTimeOffset(
                hoy.Year, hoy.Month, hoy.Day, 0, 0, 0, TimeSpan.FromHours(HondurasOffsetHours))
            .ToUniversalTime();
    }
}
