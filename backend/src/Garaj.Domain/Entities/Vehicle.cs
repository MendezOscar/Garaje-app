using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>Vehículo o moto de un cliente. Es la unidad sobre la que se abre un requerimiento.</summary>
public class Vehicle : TenantEntity
{
    public Guid CustomerId { get; set; }
    public VehicleType Type { get; set; }

    public string Brand { get; set; } = null!;
    public string Model { get; set; } = null!;
    public int? Year { get; set; }

    /// <summary>Placa/matrícula. Normalizada en mayúsculas sin espacios para poder buscarla.</summary>
    public string? Plate { get; set; }

    public string? Vin { get; set; }
    public string? Color { get; set; }
    public string? EngineNumber { get; set; }

    /// <summary>Último kilometraje registrado. Se actualiza al abrir cada orden de trabajo.</summary>
    public int? Mileage { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public Customer Customer { get; set; } = null!;
    public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
}
