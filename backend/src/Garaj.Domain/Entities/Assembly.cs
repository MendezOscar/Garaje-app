using Garaj.Domain.Common;
using Garaj.Domain.Enums;

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

    /// <summary>
    /// La sucursal del taller que lleva este contrato. De aquí sale la de cada moto armada.
    /// </summary>
    /// <remarks>
    /// Vive aquí y no en el registro de la moto porque es un dato del contrato, no del día:
    /// el técnico está parado en la bodega del contratista —que no es una sucursal del
    /// taller— y no tiene cómo contestar en cuál de las del taller va. Preguntárselo fue un
    /// callejón sin salida para el que tenía dos.
    ///
    /// Nulo mientras el dueño no lo diga: entonces se cae a la única sucursal del taller.
    /// </remarks>
    public Guid? BranchId { get; set; }

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

    /// <summary>
    /// Los primeros caracteres del VIN de este modelo, para proponerlo desde la primera moto.
    /// </summary>
    /// <remarks>
    /// Lo que significan esas letras es la tabla interna del fabricante y no hay decodificador
    /// público que valga, así que lo escribe el dueño una vez, con una caja delante: de las
    /// etiquetas de Italika salen <c>LLCLPLPC</c> para la 200Z, <c>LLCLPP20</c> para la FT125
    /// y <c>LRPRPMV0</c> para la 250Z.
    ///
    /// Vacío no rompe nada: el modelo se sigue aprendiendo de lo ya registrado, solo que a
    /// partir de la segunda moto de cada lote.
    /// </remarks>
    public string? VinPrefix { get; set; }

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

    /// <summary>
    /// Lo que le costó al taller armarla, aparte del pago al técnico. Copiado de los ajustes.
    /// </summary>
    /// <remarks>
    /// Copiado y no leído: el gasto de hoy no puede cambiar el margen de un mes ya cerrado.
    /// Nunca sale hacia el contratista —es lo que el taller gasta, no lo que él paga— ni
    /// hacia el técnico.
    /// </remarks>
    public decimal Expense { get; set; }

    /// <summary>Lo que de verdad le queda al taller por esta moto.</summary>
    public decimal Margin => Charge - TechnicianPay - Expense;

    public string? Notes { get; set; }

    /// <summary>
    /// El corte de facturación que ya la incluyó, o null si todavía está por facturar.
    /// </summary>
    /// <remarks>
    /// Es lo que impide cobrar dos veces el mismo mes y lo que congela la fila: una moto ya
    /// facturada no se corrige ni se borra, porque del otro lado hay una factura emitida.
    /// </remarks>
    public Guid? BillingSettlementId { get; set; }

    /// <summary>El corte de pago que ya la incluyó, o null si todavía está por pagar.</summary>
    public Guid? PaySettlementId { get; set; }

    public bool Congelada => BillingSettlementId is not null || PaySettlementId is not null;

    public Branch Branch { get; set; } = null!;
    public AssemblyContractor Contractor { get; set; } = null!;
    public AssemblyModel Model { get; set; } = null!;
    public AssemblyWarehouse Warehouse { get; set; } = null!;
}

/// <summary>
/// Un corte: el periodo que ya se facturó al contratista, o el que ya se le pagó a un técnico.
/// </summary>
/// <remarks>
/// Existe para resolver la pregunta que la hoja de cálculo nunca pudo contestar: «¿esto ya lo
/// cobré?». Al cerrarlo, las motos del periodo quedan marcadas y dejan de aparecer en lo que
/// falta por facturar o por pagar, y ya no se pueden corregir: del otro lado hay una factura
/// emitida o una plata entregada.
///
/// Se puede reabrir, que es la única manera honesta de corregir un corte mal hecho: reabrir
/// suelta las motos y deja rastro de quién lo hizo.
/// </remarks>
public class AssemblySettlement : TenantEntity
{
    public AssemblySettlementKind Kind { get; set; }

    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }

    /// <summary>A quién se le facturó. Null en un corte de pago.</summary>
    public Guid? ContractorId { get; set; }

    /// <summary>A quién se le pagó. Null en un corte de facturación.</summary>
    public Guid? TechnicianUserId { get; set; }

    public int Count { get; set; }

    /// <summary>Lo que sumó: el cobro en un corte de facturación, el pago en uno de pago.</summary>
    public decimal Total { get; set; }

    /// <summary>El número de factura o de la nota de pago. Lo escribe quien cierra.</summary>
    public string? Reference { get; set; }

    public Guid? ClosedByUserId { get; set; }

    /// <summary>
    /// El día en que el contratista pagó esta factura, o null mientras siga por cobrar.
    /// </summary>
    /// <remarks>
    /// Cerrar el corte es facturar, no cobrar: la hoja se le manda al contratista y él se
    /// toma su tiempo. Sin esta fecha, un corte cerrado parecía plata entrada y no lo era, y
    /// la pregunta «¿cuáles facturas están pendientes?» volvía a no tener respuesta — que es
    /// justo el problema que este módulo vino a resolver, una vuelta más adelante.
    ///
    /// Solo tiene sentido en un corte de facturación. En uno de pago, cerrar ya es entregar
    /// la plata: la hoja se firma en el momento.
    /// </remarks>
    public DateOnly? CollectedOn { get; set; }

    /// <summary>El recibo, la transferencia o el cheque con que pagó. Lo escribe el dueño.</summary>
    public string? CollectedReference { get; set; }

    public bool Cobrado => CollectedOn is not null;

    public AssemblyContractor? Contractor { get; set; }
}
