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
}
