using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Rango de facturación autorizado por el SAR para una sucursal: el CAI, los números que puede
/// emitir y hasta cuándo.
///
/// El taller puede no tener ninguno —entonces sus facturas son comprobantes de entrega, como
/// hasta ahora—, y cuando el SAR le autoriza uno nuevo se registra y el anterior queda
/// inactivo. Nunca se borra: las facturas ya emitidas guardan su copia de estos datos, y el
/// rango viejo es lo que explica de dónde salió cada número.
/// </summary>
public class FiscalRange : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }

    /// <summary>Clave de Autorización de Impresión, tal como la entrega el SAR (37 caracteres).</summary>
    public string Cai { get; set; } = null!;

    /// <summary>Código de establecimiento, la primera parte del correlativo (ej. "000").</summary>
    public string EstablishmentCode { get; set; } = "000";

    /// <summary>Punto de emisión, la segunda parte (ej. "001").</summary>
    public string PointOfSaleCode { get; set; } = "001";

    /// <summary>Tipo de documento: "01" es factura.</summary>
    public string DocumentType { get; set; } = "01";

    /// <summary>Primer número autorizado (la cuarta parte del correlativo, sin ceros).</summary>
    public int RangeStart { get; set; }

    /// <summary>Último número autorizado.</summary>
    public int RangeEnd { get; set; }

    /// <summary>
    /// El próximo número a emitir. Arranca en <see cref="RangeStart"/> y solo avanza: un
    /// número consumido no se reutiliza ni cuando la factura se anula.
    /// </summary>
    public int NextNumber { get; set; }

    /// <summary>Fecha límite de emisión. Pasada esa fecha el rango ya no factura.</summary>
    public DateTimeOffset IssueDeadline { get; set; }

    /// <summary>Solo un rango activo por sucursal: es el que emite.</summary>
    public bool IsActive { get; set; } = true;

    public Branch Branch { get; set; } = null!;

    /// <summary>Cuántos números quedan por emitir. Sirve para avisar antes de quedarse sin.</summary>
    public int Remaining => Math.Max(0, RangeEnd - NextNumber + 1);

    /// <summary>Correlativo completo de un número de este rango, ej. "000-001-01-00000001".</summary>
    public string Format(int number) =>
        $"{EstablishmentCode}-{PointOfSaleCode}-{DocumentType}-{number:D8}";

    /// <summary>El rango autorizado en el formato que se imprime en la factura.</summary>
    public string RangeText => $"{Format(RangeStart)} a {Format(RangeEnd)}";
}
