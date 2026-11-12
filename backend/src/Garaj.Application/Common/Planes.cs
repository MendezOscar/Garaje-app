using Garaj.Domain.Enums;

namespace Garaj.Application.Common;

/// <summary>
/// Con qué nace cada plan. Es lo único que sabe de precios y topes comerciales, para que
/// cambiar la oferta sea cambiar este archivo y no buscar números por todo el código.
/// </summary>
/// <remarks>
/// Son valores <b>de partida</b>, no reglas: al elegir el plan se copian al taller y de ahí
/// en adelante mandan los del taller. Así el cliente que paga por un técnico de más queda con
/// cuatro sin dejar de ser Básico, que es como se negocia de verdad.
/// </remarks>
public static class Planes
{
    /// <summary>Cuota mensual sugerida, en lempiras, para una sucursal.</summary>
    public static decimal CuotaDe(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => 850m,
        SubscriptionPlan.Pro => 1250m,
        _ => 0m
    };

    /// <summary>Tope de técnicos con que nace. Null es sin tope.</summary>
    public static int? TecnicosDe(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => 3,
        _ => null
    };

    /// <summary>
    /// Tope de sucursales con que nace. Null es sin tope.
    /// </summary>
    /// <remarks>
    /// La cuota del Básico es por una sucursal: la segunda se cobra aparte y se le sube el
    /// número al taller. El Pro y los heredados nacen sin tope, como con los técnicos.
    /// </remarks>
    public static int? SucursalesDe(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => 1,
        _ => null
    };

    /// <summary>Si nace con el módulo de diagnóstico.</summary>
    public static bool DiagnosticoDe(SubscriptionPlan plan) => plan == SubscriptionPlan.Pro;

    /// <summary>
    /// Si nace con el asistente. Es del plan Pro, y los talleres heredados lo llevan por lo
    /// mismo que llevan todo lo demás: se les ofreció sin topes.
    /// </summary>
    public static bool AsistenteDe(SubscriptionPlan plan) =>
        plan is SubscriptionPlan.Pro or SubscriptionPlan.Legacy;

    /// <summary>
    /// Créditos del asistente al mes, y al día. Son los mismos tenga el taller tres técnicos o
    /// quince: lo que se mide es el asistente, no la gente, y así el «técnicos sin límite» que
    /// el plan Pro vende sigue siendo cierto.
    /// </summary>
    public static (int Mes, int Dia) CreditosDe(SubscriptionPlan plan) =>
        AsistenteDe(plan) ? (450, 80) : (0, 0);

    /// <summary>
    /// Tope de gasto mensual en lempiras por taller. No se le enseña al taller: es el techo
    /// que nos protege a nosotros cuando los créditos se quedan cortos como aproximación.
    /// </summary>
    public static decimal TopeMensualDe(SubscriptionPlan plan) =>
        AsistenteDe(plan) ? 220m : 0m;

    /// <summary>Cómo se llama el plan donde lo lee un humano: el panel y la factura.</summary>
    public static string NombreDe(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => "Básico",
        SubscriptionPlan.Pro => "Pro",
        _ => "Heredado"
    };
}
