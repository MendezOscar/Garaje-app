using Garaj.Domain.Common;

namespace Garaj.Domain.Entities;

/// <summary>
/// Quien le paga al taller por armar motos. Hoy es uno; el modelo soporta varios a propósito.
/// </summary>
/// <remarks>
/// Es la pieza de la que cuelgan los dos precios, y por eso existe aunque haya uno solo: la
/// tarifa no es del modelo de moto, es de lo que ese contratista paga por ese modelo. El día
/// que entre el segundo, nada se reescribe.
/// </remarks>
public class AssemblyContractor : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Con quién se habla y cómo se factura. Texto libre: cada contrato es distinto.</summary>
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<AssemblyRate> Rates { get; set; } = new List<AssemblyRate>();
}

/// <summary>
/// Un modelo de moto de los que el taller arma.
/// </summary>
/// <remarks>
/// Es una lista y no texto libre porque de él cuelga la tarifa: con el modelo tecleado a mano,
/// «FT150» y «Ft 150» serían dos cosas y el cobro saldría distinto según quién lo escribió.
/// Además es lo que permite que registrar una moto sean tres toques en la bodega.
/// </remarks>
public class AssemblyModel : TenantEntity
{
    public string Name { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Lo que se cobra y lo que se paga por armar un modelo para un contratista.
/// </summary>
/// <remarks>
/// Los dos números viven en la misma fila porque se configuran juntos, en la ficha del
/// contratista, y porque la diferencia entre ellos es lo que el taller gana: tenerlos
/// separados obligaría a cruzar dos pantallas para ver si un modelo deja algo.
///
/// Son valores de partida, no reglas: al registrar una moto se copian a la fila, y de ahí en
/// adelante manda lo que quedó copiado. Así subir la tarifa en marzo no reescribe lo de enero,
/// que ya se facturó.
/// </remarks>
public class AssemblyRate : TenantEntity
{
    public Guid ContractorId { get; set; }
    public Guid ModelId { get; set; }

    /// <summary>Lo que se le cobra al contratista por armar una. No lo ve el técnico.</summary>
    public decimal Charge { get; set; }

    /// <summary>Lo que se le paga al técnico que la arma. Lo pone el dueño.</summary>
    public decimal TechnicianPay { get; set; }

    public bool IsActive { get; set; } = true;

    public AssemblyContractor Contractor { get; set; } = null!;
    public AssemblyModel Model { get; set; } = null!;
}

/// <summary>
/// La bodega o el lugar donde se arman: el «lugar» de la hoja de cálculo.
/// </summary>
/// <remarks>
/// No son sucursales del taller —no tienen usuarios, ni inventario, ni rango fiscal—: son los
/// locales del contratista a los que va el técnico. Por eso es un catálogo aparte y no
/// <see cref="Branch"/>.
/// </remarks>
public class AssemblyWarehouse : TenantEntity
{
    public string Name { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Una moto armada. Es la fila del Excel, y es plata dos veces: lo que se factura y lo que se
/// le paga al técnico.
/// </summary>
/// <remarks>
/// La serie es la llave de verdad. Repetida significa una moto cobrada dos veces; ausente,
/// una moto regalada. Ninguna hoja de cálculo avisa de eso, y es la razón principal por la
/// que esto deja de vivir en una hoja.
///
/// Los dos importes se copian de <see cref="AssemblyRate"/> al registrar y no se leen de ella
/// después: lo que se facturó en enero no cambia porque en marzo se renegocie la tarifa.
/// </remarks>
public class MotorcycleAssembly : TenantEntity
{
    /// <summary>La sucursal del taller que lleva el contrato. Es lo que acota quién la ve.</summary>
    public Guid BranchId { get; set; }

    public Guid ContractorId { get; set; }
    public Guid ModelId { get; set; }
    public Guid WarehouseId { get; set; }

    /// <summary>Quién la armó. Es a quien se le paga.</summary>
    public Guid TechnicianUserId { get; set; }

    /// <summary>
    /// El día en que se armó, en días de Honduras. No es la fecha de captura: se registra en
    /// la bodega, pero una capturada a las siete de la noche del 31 sigue siendo de enero.
    /// </summary>
    public DateOnly AssembledOn { get; set; }

    /// <summary>El número de serie del chasis, normalizado igual que el VIN de un vehículo.</summary>
    public string Serial { get; set; } = null!;

    /// <summary>Lo que se le cobra al contratista. Copiado de la tarifa. No lo ve el técnico.</summary>
    public decimal Charge { get; set; }

    /// <summary>Lo que se le paga al técnico. Copiado de la tarifa.</summary>
    public decimal TechnicianPay { get; set; }

    public string? Notes { get; set; }

    public Branch Branch { get; set; } = null!;
    public AssemblyContractor Contractor { get; set; } = null!;
    public AssemblyModel Model { get; set; } = null!;
    public AssemblyWarehouse Warehouse { get; set; } = null!;
}
