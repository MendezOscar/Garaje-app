using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Un tramo de trabajo sobre una orden: desde que el técnico tocó «Empezar» hasta que paró.
/// </summary>
/// <remarks>
/// Son tramos y no un total porque el trabajo se interrumpe: entra otro carro urgente, se
/// acaba el día, el repuesto llega mañana. Guardar «empezó a las 9 y terminó a las 4 del día
/// siguiente» contaría la noche como trabajo; con tramos, el que sigue al otro día suma dos
/// pedazos y la noche no existe.
///
/// Es por orden y no por paso: el técnico tiene las manos sucias y un teléfono en el bolsillo,
/// y un cronómetro por cada paso se queda sin tocar. Lo que el dueño quiere saber —cuánto se
/// tardó este trabajo y quién lo hizo— se contesta igual.
/// </remarks>
public class WorkOrderTimeEntry : TenantEntity
{
    public Guid WorkOrderId { get; set; }

    /// <summary>Quién trabajó. El tiempo es de la persona, no del puesto.</summary>
    public Guid UserId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null mientras el cronómetro corre.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Lo cerró el sistema y no la persona: porque empezó en otra orden, o porque el tramo
    /// pasó del largo de una jornada. Se dice en el reporte: ese número no es de fiar.
    /// </summary>
    public bool AutoClosed { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;

    /// <summary>
    /// Lo que duró el tramo, recortado a una jornada. El cronómetro que nadie paró el viernes
    /// no puede decir que se trabajaron sesenta horas.
    /// </summary>
    public TimeSpan Duracion(DateTimeOffset ahora)
    {
        var fin = EndedAt ?? ahora;
        var duracion = fin - StartedAt;

        if (duracion < TimeSpan.Zero) return TimeSpan.Zero;

        return duracion > Jornada ? Jornada : duracion;
    }

    /// <summary>Lo máximo que puede durar un tramo: más que esto es un cronómetro olvidado.</summary>
    public static readonly TimeSpan Jornada = TimeSpan.FromHours(12);
}
