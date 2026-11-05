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

    /// <summary>Si nace con el módulo de diagnóstico.</summary>
    public static bool DiagnosticoDe(SubscriptionPlan plan) => plan == SubscriptionPlan.Pro;

    /// <summary>Cómo se llama el plan donde lo lee un humano: el panel y la factura.</summary>
    public static string NombreDe(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Basic => "Básico",
        SubscriptionPlan.Pro => "Pro",
        _ => "Heredado"
    };
}
