using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Equipo de un cliente: la computadora, el teléfono, el aire acondicionado. Es a un taller
/// de equipos lo que <see cref="Vehicle"/> a uno mecánico —la unidad sobre la que se abre un
/// requerimiento— y por eso son dos tablas y no una: un vehículo tiene placa, kilometraje y
/// motor, y forzarlos a convivir en una sola ficha deja la mitad de las casillas vacías
/// siempre, para los dos.
/// </summary>
public class Equipment : TenantEntity
{
    public Guid CustomerId { get; set; }
    public Guid EquipmentTypeId { get; set; }

    public string Brand { get; set; } = null!;
    public string Model { get; set; } = null!;

    /// <summary>
    /// Serie o IMEI, normalizada en mayúsculas sin espacios. Es opcional a propósito: una
    /// licuadora no la trae, y en un equipo viejo la etiqueta muchas veces ya no se lee.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// La etiqueta que el taller le pega al recibirlo, para lo que llega sin serie legible.
    /// No la sabe el cliente: sirve para encontrar el aparato en la bodega.
    /// </summary>
    public string? Tag { get; set; }

    public string? Color { get; set; }

    /// <summary>Lo que el cliente deja con el equipo: el cargador, el control, la base.</summary>
    public string? Accessories { get; set; }

    /// <summary>
    /// Lo propio de cada tipo —BTU de un aire, procesador de una laptop, voltaje de un
    /// motor— guardado como JSON. Es eso o una tabla de campos por rubro, y el taller
    /// necesita anotarlo, no consultarlo.
    /// </summary>
    public string? Attributes { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public Customer Customer { get; set; } = null!;
    public EquipmentType EquipmentType { get; set; } = null!;
    public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
}

/// <summary>
/// Categoría de equipo del taller: "Laptop", "Aire acondicionado", "Celular". Es tabla y no
/// enum porque cada rubro tiene las suyas y desde aquí no se pueden adivinar.
/// </summary>
public class EquipmentType : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Nombre del ícono que le pone la app, ej. "laptop". Null usa el genérico.</summary>
    public string? Icon { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Equipment> Equipment { get; set; } = new List<Equipment>();
}
