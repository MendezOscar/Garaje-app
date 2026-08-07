namespace Garaj.Application.Abstractions;

/// <summary>
/// Reloj inyectable. Necesario porque los reportes agrupan por día/semana/mes y los tests
/// tienen que poder fijar la fecha sin depender del reloj de la máquina.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
