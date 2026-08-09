using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Servicio de mano de obra del catálogo (ej. "Cambio de aceite", "ABC de motor").
/// Se cobra por precio fijo o por horas × tarifa, según <see cref="IsFixedPrice"/>.
/// </summary>
public class LaborService : TenantEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Category { get; set; }

    /// <summary>Horas estándar del servicio. Se usa como estimación al crear el paso.</summary>
    public decimal StandardHours { get; set; }

    public decimal HourlyRate { get; set; }

    /// <summary>Si es true se cobra <see cref="FixedPrice"/>; si no, StandardHours × HourlyRate.</summary>
    public bool IsFixedPrice { get; set; }

    public decimal FixedPrice { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Lo que se cobra por una unidad del servicio con las horas que llevó. Sin horas se
    /// usan las estándar. La regla vive aquí para que la cotización, la factura y lo que se
    /// muestra en el paso siempre den el mismo número.
    /// </summary>
    public decimal PriceFor(decimal? hours) =>
        IsFixedPrice ? FixedPrice : (hours ?? StandardHours) * HourlyRate;
}
