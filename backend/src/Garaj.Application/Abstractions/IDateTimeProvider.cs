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
    /// <summary>
    /// El día de hoy en UTC, para lo que se cuenta por días y no por instantes (el vencimiento
    /// de la suscripción). Que el corte sea por día UTC le regala seis horas al taller
    /// hondureño en vez de quitárselas, que es el lado correcto donde equivocarse.
    /// </summary>
    public static DateOnly Today(this IDateTimeProvider clock) =>
        DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
}
