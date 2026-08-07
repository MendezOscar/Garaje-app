using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>Sucursal del taller. Las órdenes, el stock y las ventas siempre pertenecen a una.</summary>
public class Branch : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Code { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Último correlativo emitido de orden de trabajo en esta sucursal.</summary>
    public int WorkOrderSequence { get; set; }

    /// <summary>Último correlativo emitido de cotización en esta sucursal.</summary>
    public int QuoteSequence { get; set; }

    /// <summary>Último correlativo emitido de venta en esta sucursal.</summary>
    public int SaleSequence { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
