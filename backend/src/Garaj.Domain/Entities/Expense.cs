using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Un gasto del taller: lo que sale por el otro lado de la caja.
/// </summary>
/// <remarks>
/// Es la mitad que faltaba para el estado de resultados. Los ingresos y el costo de lo
/// vendido ya los sabía el sistema; sin los gastos, la utilidad que mostraba era la bruta y
/// nadie vive de esa.
/// </remarks>
public class Expense : TenantEntity, IBranchEntity
{
    public Guid BranchId { get; set; }

    public ExpenseCategory Category { get; set; }

    /// <summary>En qué se gastó, escrito por quien lo registra.</summary>
    public string Description { get; set; } = null!;

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>A quién se le pagó. Opcional: no todo gasto tiene un proveedor con nombre.</summary>
    public string? SupplierName { get; set; }

    /// <summary>
    /// Fecha contable del gasto, que es la que agrupan los reportes. No es CreatedAt: un
    /// gasto del lunes se registra el miércoles y sigue siendo del lunes.
    /// </summary>
    public DateTimeOffset ExpenseDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// El empleado al que se le pagó, en los gastos de salario. Es lo que permite responder
    /// cuánto se le ha pagado a cada técnico sin llevar un registro aparte: el pago es un
    /// gasto, y en la caja entra una sola vez.
    /// </summary>
    public Guid? EmployeeUserId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Branch Branch { get; set; } = null!;
}
