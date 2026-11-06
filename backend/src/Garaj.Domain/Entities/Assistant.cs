using Garaj.Domain.Common;
using Garaj.Domain.Enums;

namespace Garaj.Domain.Entities;

/// <summary>
/// Una llamada al modelo, con lo que costó. Hay una fila por llamada, haya salido bien o mal.
/// </summary>
/// <remarks>
/// Es la tabla sobre la que se cuentan los topes, así que tiene que registrarlo todo. Un
/// reintento se paga igual que el primer intento y una llamada que falla a mitad también
/// consume tokens: si solo se guardaran las exitosas, el techo mensual sería una idea y no un
/// techo, que es justo el escenario que arruina un mes —un error que reintenta en bucle de
/// madrugada—.
///
/// El costo se guarda <b>en dólares</b> porque es la moneda en la que factura el proveedor; la
/// conversión a lempiras es para enseñarla, no para contar.
/// </remarks>
public class AssistantCall : TenantEntity
{
    public AssistantFeature Feature { get; set; }

    /// <summary>El identificador exacto del modelo, tal cual se mandó. Cambia con el tiempo.</summary>
    public string Model { get; set; } = null!;

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>
    /// Tokens que se leyeron de caché, que cuestan una fracción. Se guardan aparte porque son
    /// la mitad del argumento de costo de esta función: con el catálogo cacheado la entrada
    /// medida bajó de ~3,700 tokens a ~70.
    /// </summary>
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }

    /// <summary>Lo que costó de verdad, en dólares. Calculado con la tarifa del modelo usado.</summary>
    public decimal CostUsd { get; set; }

    /// <summary>Créditos que se le cobraron al taller. Cero en lo que se ofrece sin cobrar.</summary>
    public int Credits { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>La orden desde la que se pidió, si nació de una. Null en la lectura semanal.</summary>
    public Guid? WorkOrderId { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>Qué falló, en corto. Null si salió bien.</summary>
    public string? Error { get; set; }

    /// <summary>Cuánto tardó la llamada. Sirve para saber si hay que pasar a streaming.</summary>
    public int ElapsedMs { get; set; }
}

/// <summary>
/// Una propuesta del asistente, tal como salió, con lo que se le mandó para producirla.
/// </summary>
/// <remarks>
/// Se guarda el envío entero a propósito: cuando una propuesta salga mal hay que poder ver
/// qué vio el modelo, y sin eso no se distingue un prompt flojo de un catálogo mal filtrado.
/// Es lo que pide §9 de seguridad para cualquier cosa que después se convierte en datos de la
/// orden.
///
/// Nada de esto se guarda como pasos solo: aceptar es un acto del humano, y hasta que ocurra
/// la propuesta es un borrador. Por eso el estado nace en <see cref="AssistantProposalStatus.Draft"/>.
/// </remarks>
public class AssistantProposal : TenantEntity
{
    public Guid WorkOrderId { get; set; }

    public AssistantFeature Feature { get; set; }

    public AssistantProposalStatus Status { get; set; } = AssistantProposalStatus.Draft;

    /// <summary>Lo que se le mandó al modelo, entero, en JSON.</summary>
    public string Request { get; set; } = null!;

    /// <summary>Lo que devolvió, ya validado, en JSON. Es lo que se le enseña al usuario.</summary>
    public string Response { get; set; } = null!;

    /// <summary>La llamada que la produjo, para saber qué costó esta propuesta en concreto.</summary>
    public Guid AssistantCallId { get; set; }

    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
}

/// <summary>
/// La lectura del negocio de un periodo: qué pasó, qué cambió y qué conviene hacer.
/// </summary>
/// <remarks>
/// Se guarda entera y con el paquete de números que se le mandó al modelo, por lo mismo que la
/// propuesta de pasos: cuando una lectura diga algo raro, hay que poder ver con qué cifras la
/// escribió. Y porque el dueño va a querer comparar la de esta semana con la de hace un mes, y
/// rehacerla daría otro texto.
///
/// El modelo <b>no calcula</b>: recibe los números ya hechos y escribe la lectura. Un modelo
/// que se equivoca en una resta en el reporte del dueño destruye la confianza en todo lo
/// demás, y es un error que no se detecta mirando.
/// </remarks>
public class BusinessReading : TenantEntity
{
    public BusinessReadingKind Kind { get; set; }

    /// <summary>El periodo que mira, en días del taller.</summary>
    public DateOnly PeriodFrom { get; set; }

    public DateOnly PeriodTo { get; set; }

    /// <summary>El paquete de agregados que se le mandó, en JSON. Ningún nombre de cliente.</summary>
    public string Data { get; set; } = null!;

    /// <summary>La lectura, como la escribió el modelo.</summary>
    public string Content { get; set; } = null!;

    public Guid AssistantCallId { get; set; }
}

/// <summary>
/// Una consulta del técnico sobre una orden: la conversación entera, con su contexto.
/// </summary>
/// <remarks>
/// Es corta a propósito y se cierra sola a los pocos turnos. No es solo costo —vale cinco
/// veces lo que una propuesta—: una conversación larga se desenfoca, y una pregunta concreta
/// por consulta se contesta mejor que seis preguntas encadenadas.
///
/// Se guarda entera porque es lo que el taller aprendió sobre ese vehículo, y porque cuando
/// una respuesta salga mal hay que poder leer la conversación que la produjo.
/// </remarks>
public class AssistantConversation : TenantEntity
{
    public Guid WorkOrderId { get; set; }

    /// <summary>Quién preguntó. Un técnico ve las suyas; el Dueño, las de su taller.</summary>
    public Guid? StartedByUserId { get; set; }

    /// <summary>De qué va, sacado de la primera pregunta. Es lo que se lista.</summary>
    public string Title { get; set; } = null!;

    /// <summary>
    /// Cerrada. Al llegar al tope de turnos se cierra y hay que abrir otra: es la forma de que
    /// una conversación no se alargue sola hasta perder el hilo y el presupuesto.
    /// </summary>
    public bool IsClosed { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;
    public ICollection<AssistantMessage> Messages { get; set; } = new List<AssistantMessage>();
}

/// <summary>Un turno de la consulta: lo que preguntó el técnico o lo que contestó.</summary>
public class AssistantMessage : TenantEntity
{
    public Guid ConversationId { get; set; }

    /// <summary>Si lo escribió la persona. En false lo escribió el modelo.</summary>
    public bool FromUser { get; set; }

    public string Content { get; set; } = null!;

    /// <summary>La llamada que produjo esta respuesta. Null en lo que escribió la persona.</summary>
    public Guid? AssistantCallId { get; set; }

    public AssistantConversation Conversation { get; set; } = null!;
}
