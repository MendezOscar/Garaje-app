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

    /// <summary>
    /// Fecha en que se acordó terminar de pagar. Null cuando la venta se cobró de contado.
    /// </summary>
    /// <remarks>
    /// No se guarda una bandera "es a crédito": una venta lo es cuando queda saldo, y eso
    /// se sabe sumando los abonos. Guardar además el estado sería un segundo sitio donde
    /// la verdad puede desincronizarse.
    /// </remarks>
    public DateTimeOffset? DueDate { get; set; }

    public string? Notes { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }

    // ---------- Régimen de facturación (opcional) ----------
    //
    // Una venta sin CAI los deja todos en null y sigue siendo el comprobante de entrega de
    // siempre. Cuando se emite con CAI, estos campos son una *fotografía* del rango y no una
    // referencia: el rango se agota y se reemplaza, y la factura ya impresa no puede cambiar.

    public Guid? FiscalRangeId { get; set; }

    /// <summary>Correlativo fiscal completo, ej. "000-001-01-00000001".</summary>
    public string? FiscalNumber { get; set; }

    public string? FiscalCai { get; set; }

    /// <summary>El rango autorizado tal como se imprime, ej. "000-…-00000001 a 000-…-00005000".</summary>
    public string? FiscalRangeText { get; set; }

    public DateTimeOffset? FiscalIssueDeadline { get; set; }

    /// <summary>RTN del cliente en el momento de facturar. Sin él la factura va a consumidor final.</summary>
    public string? CustomerTaxId { get; set; }

    /// <summary>
    /// A nombre de quién salió la factura. Es una fotografía como el resto: la ficha del
    /// cliente se puede corregir después y una factura ya emitida no cambia. Null en las
    /// ventas sin CAI, que siguen mostrando el nombre que tenga la ficha hoy.
    /// </summary>
    public string? CustomerName { get; set; }

    public bool IsFiscal => FiscalNumber is not null;

    public Branch Branch { get; set; } = null!;
    public ICollection<SaleLine> Lines { get; set; } = new List<SaleLine>();
    public ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
}

/// <summary>
/// Un abono a una venta. Lo que se pagó de contado es un abono por el total, hecho el mismo
/// día: así no hay dos caminos distintos para el dinero.
/// </summary>
/// <remarks>
/// Los abonos son la única fuente de verdad de lo cobrado, igual que los movimientos lo son
/// del inventario. La venta no guarda un "pagado" que haya que mantener al día: se suma.
/// Un saldo guardado se desincroniza en cuanto alguien corrige un abono, y entonces la caja
/// deja de cuadrar sin que nadie sepa por qué.
/// </remarks>
public class SalePayment : TenantEntity
{
    public Guid SaleId { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>Fecha en que entró el dinero, que puede no ser la de captura.</summary>
    public DateTimeOffset PaidAt { get; set; }

    /// <summary>Número de recibo, de transferencia o de depósito.</summary>
    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public Sale Sale { get; set; } = null!;
}
