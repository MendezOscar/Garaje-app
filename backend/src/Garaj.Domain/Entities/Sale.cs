using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Venta cerrada: sale del cierre de una orden de trabajo o de una venta directa de mostrador.
/// Es la fuente de los reportes de ingresos diarios, semanales y mensuales.
/// </summary>
public class Sale : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? WorkOrderId { get; set; }

    /// <summary>Correlativo legible por sucursal, ej. "VTA-SUR-000312".</summary>
    public string Number { get; set; } = null!;

    /// <summary>Fecha contable de la venta. Es la que agrupan los reportes, no CreatedAt.</summary>
    public DateTimeOffset SaleDate { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }

    /// <summary>Costo total de los repuestos vendidos. Permite calcular margen sin recorrer líneas.</summary>
    public decimal CostTotal { get; set; }

    public string? Notes { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }

    public Branch Branch { get; set; } = null!;
    public ICollection<SaleLine> Lines { get; set; } = new List<SaleLine>();
}
