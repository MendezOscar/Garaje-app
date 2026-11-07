using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// El taller como negocio. Es la raíz de aislamiento: todo lo demás cuelga de aquí.
/// No implementa ITenantEntity porque *es* el tenant.
/// </summary>
public class Tenant : AuditableEntity
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// Dirección de la casa matriz. El régimen de facturación la pide aparte de la del
    /// establecimiento que emite, que es la de la sucursal.
    /// </summary>
    public string? Address { get; set; }

    public string? LogoStorageKey { get; set; }

    /// <summary>Código de país por defecto para armar los links de WhatsApp (ej. "504" = Honduras).</summary>
    public string DefaultPhoneCountryCode { get; set; } = "504";

    /// <summary>Moneda ISO-4217 usada en cotizaciones y ventas.</summary>
    public string Currency { get; set; } = "HNL";

    /// <summary>
    /// Si el taller repara vehículos o equipos. Lo elige al configurarse y de ahí sale
    /// qué se le puede dar de alta a un cliente; cambiarlo con órdenes abiertas no borra
    /// nada, pero deja el taller con fichas que ya no se pueden abrir.
    /// </summary>
    public WorkshopKind WorksWith { get; set; } = WorkshopKind.Vehicles;

    /// <summary>Porcentaje de impuesto por defecto (ej. 15.00 = 15%).</summary>
    public decimal DefaultTaxRate { get; set; }

    /// <summary>
    /// Si los precios del catálogo ya llevan el ISV adentro.
    ///
    /// Verdadero es lo normal: el precio es lo que el cliente paga y la factura solo desglosa
    /// el impuesto. Falso es el taller que cotiza sin impuesto y lo suma al facturar, donde
    /// con factura se paga más que sin ella.
    /// </summary>
    public bool PricesIncludeTax { get; set; } = true;

    /// <summary>
    /// Días de garantía que lleva un trabajo por defecto. Cero es sin garantía, y es como
    /// nace un taller: ofrecerla es una decisión suya, no nuestra. Se puede cambiar al
    /// facturar, trabajo por trabajo.
    /// </summary>
    public int DefaultWarrantyDays { get; set; }

    /// <summary>
    /// Si el técnico ve precios: de repuestos, de mano de obra y los totales de la orden.
    /// </summary>
    /// <remarks>
    /// Nace encendido para no cambiarle la pantalla de un día para otro a un taller que ya
    /// trabaja así. Apagado, el técnico recibe la orden, agrega pasos y carga repuestos, pero
    /// no toca plata: pone la orden en espera de aprobación y el Dueño le pone precio y
    /// cotiza. Se impone en el servidor, no escondiendo campos en la pantalla: lo que no se
    /// manda no se puede mirar.
    /// </remarks>
    public bool TechniciansSeePrices { get; set; } = true;

    // ---------- Bodegaje ----------
    //
    // Lo que el taller cobra por el vehículo que nadie retira. Nace apagado: cobrarlo es una
    // decisión del taller, y uno que no lo cobra no quiere ni ver la casilla.

    public bool ChargesStorage { get; set; }

    /// <summary>Días de gracia desde el aviso de que está listo. Dentro de ellos no se cobra.</summary>
    public int StorageFreeDays { get; set; } = 3;

    /// <summary>Lo que se cobra por día pasada la gracia.</summary>
    public decimal StorageDailyRate { get; set; }

    /// <summary>
    /// Correlativo de los reclamos. Va en el taller y no en la sucursal: un reclamo es del
    /// taller, y el cliente lo reclama donde le quede más cerca.
    /// </summary>
    public int ClaimSequence { get; set; }

    // ---------- Citas ----------

    /// <summary>
    /// Token del enlace público de citas: el que el taller manda por WhatsApp o pega en un QR
    /// del mostrador. Es fijo y uno solo por taller, para que se pueda imprimir y no caduque.
    /// </summary>
    /// <remarks>
    /// No expone nada del taller salvo su nombre, su logo y sus sucursales —lo que ya está en
    /// la puerta del local—, y lo único que deja hacer es pedir una cita.
    /// </remarks>
    public Guid BookingToken { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Si el enlace de citas recibe solicitudes. Apagarlo es la salida del taller que se llenó
    /// o que no quiere agendar: el enlace sigue abriendo, pero dice que no se está agendando.
    /// </summary>
    public bool BookingEnabled { get; set; } = true;

    /// <summary>
    /// Suspensión manual: el corte definitivo, decidido por nosotros. A diferencia del vencimiento
    /// —que solo deja el taller en modo lectura— un taller suspendido no puede ni iniciar sesión.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ---------- Suscripción ----------
    // Lo que el taller nos paga a nosotros. Vive aquí y no en una entidad aparte porque es
    // parte de lo que define al taller como cliente nuestro, y porque así una sola lectura
    // por clave primaria resuelve si puede trabajar. El historial de pagos sí va aparte.

    /// <summary>
    /// El plan contratado. De él salen los topes con que nace el taller; después se ajustan
    /// aparte. Los talleres de antes de los planes se quedan en <c>Legacy</c>: todo incluido.
    /// </summary>
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Legacy;

    /// <summary>Nombre del plan contratado, tal como se le ofreció. Informativo: sale impreso.</summary>
    public string? PlanName { get; set; }

    /// <summary>
    /// Cuántos técnicos activos puede tener. <c>null</c> es sin tope, que es como quedan el
    /// plan Pro y los talleres de antes de los planes.
    /// </summary>
    /// <remarks>
    /// Va aquí y no deducido del plan porque es lo que se negocia: el taller del plan Básico
    /// que contrata al cuarto mecánico paga por ese técnico y se le sube el número, sin
    /// moverlo de plan. El Dueño y los Encargados no cuentan —no son técnicos— y el cliente
    /// con app tampoco.
    /// </remarks>
    public int? TechnicianLimit { get; set; }

    /// <summary>
    /// Si tiene el módulo de diagnóstico con escáner: el informe dentro de la orden, la
    /// lectura de los códigos y el historial por vehículo.
    /// </summary>
    public bool DiagnosticsEnabled { get; set; }

    /// <summary>
    /// Si tiene el asistente: proponer los pasos de un diagnóstico, la lectura del negocio y
    /// la consulta del técnico.
    /// </summary>
    /// <remarks>
    /// Va aparte de <see cref="DiagnosticsEnabled"/> y no mezclado con él, aunque los dos
    /// nazcan del plan Pro: ese es el del escáner. Con un solo interruptor, apagarle el
    /// asistente a un taller —porque se disparó el costo, o porque se está depurando— le
    /// apagaría también el escáner que ya está usando dentro de las órdenes.
    /// </remarks>
    public bool AssistantEnabled { get; set; }

    /// <summary>
    /// Si tiene el módulo de armado de motos: registrar cada moto armada en la bodega, y los
    /// reportes de facturación al contratista y de pago a los técnicos.
    /// </summary>
    /// <remarks>
    /// No sale de ningún plan y nace apagado para todos: es un módulo de un contrato, no una
    /// función que se venda en la lista de precios. Se enciende taller por taller desde el
    /// panel de plataforma.
    /// </remarks>
    public bool AssemblyEnabled { get; set; }

    /// <summary>
    /// Créditos del asistente al mes. Una propuesta de pasos vale 1 y una consulta del técnico
    /// 5; la lectura del negocio no gasta.
    /// </summary>
    public int AssistantCreditsPerMonth { get; set; }

    /// <summary>
    /// Tope diario de créditos. Es el que de verdad protege: el mensual se agota en una tarde
    /// si alguien descubre el juguete, y un tope diario le pone un piso de varios días a
    /// cualquier desastre sin racionarle nada a un día ocupado normal.
    /// </summary>
    public int AssistantCreditsPerDay { get; set; }

    /// <summary>
    /// Tope de gasto mensual en lempiras, que corta aunque queden créditos. <b>No se le
    /// enseña al taller.</b>
    /// </summary>
    /// <remarks>
    /// Los créditos son una aproximación: una propuesta en un taller con 400 repuestos en
    /// catálogo cuesta más que en uno con 40, porque va más texto en cada llamada. Este es el
    /// techo en dinero real que hay debajo, y se cuenta con el costo que devuelve cada
    /// llamada, no con una estimación.
    /// </remarks>
    public decimal AssistantMonthlyCapHnl { get; set; }

    /// <summary>Cuota mensual acordada, en la moneda del taller.</summary>
    public decimal MonthlyFee { get; set; }

    /// <summary>
    /// Hasta qué día está pagada la suscripción. Es el corazón de todo: de aquí salen el aviso
    /// que ve el Dueño y el bloqueo.
    ///
    /// Es <c>DateOnly</c> y no un instante a propósito: una fecha de pago es un día, no una
    /// hora. El «hoy» contra el que se compara es el de Honduras y no el del servidor —ver
    /// <c>DateTimeProviderExtensions.Today</c>—, porque el día UTC cambia a las 6 de la tarde
    /// de acá y le cortaría el sistema al taller justo mientras cierra el día.
    /// </summary>
    public DateOnly? PaidThrough { get; set; }

    /// <summary>
    /// Días de tolerancia después de <see cref="PaidThrough"/> en los que el taller sigue
    /// trabajando con aviso. Por taller, no global: al primer cliente se le puede dar más.
    /// </summary>
    public int GraceDays { get; set; } = 5;

    /// <summary>
    /// Acuerdo de pago: hasta este día el taller trabaja aunque esté vencido. Es la válvula para
    /// el que quedó debiendo y va a pagar; cortarle sería perder un cliente que se recupera.
    /// </summary>
    public DateOnly? UnblockedThrough { get; set; }

    /// <summary>Por qué se le dio el acuerdo. Sin esto, en dos meses nadie se acuerda.</summary>
    public string? UnblockNote { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
