using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Subscriptions;
using Garaj.Domain.Rules;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Api.Middleware;

/// <summary>
/// Impide trabajar al taller que no pagó, sin quitarle sus datos.
///
/// La regla es corta a propósito: **estorba solo a los métodos que escriben**. Un `GET` siempre
/// pasa —eso *es* «solo lectura»— y `POST`, `PUT`, `PATCH` y `DELETE` responden 402. Que el
/// filtro sea por método y no por una lista de rutas importa: la función que se escriba el mes
/// que viene queda cubierta el día que nace, sin que nadie tenga que acordarse de anotarla.
///
/// Corre después de <see cref="TenantContextMiddleware"/>, que es quien resuelve de qué taller
/// es la petición.
/// </summary>
public class SubscriptionGuardMiddleware(RequestDelegate next)
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS"];

    public async Task InvokeAsync(
        HttpContext context, ITenantContext tenantContext, GarajDbContext db, IDateTimeProvider clock)
    {
        if (await IsBlockedAsync(context, tenantContext, db, clock) is { } status)
            throw new SubscriptionRequiredException(SubscriptionMessages.Blocked(status));

        await next(context);
    }

    private static async Task<SubscriptionStatus?> IsBlockedAsync(
        HttpContext context, ITenantContext tenantContext, GarajDbContext db, IDateTimeProvider clock)
    {
        if (SafeMethods.Contains(context.Request.Method)) return null;

        // Sin taller no hay mensualidad que cobrar: es la cotización que el cliente final abre
        // por WhatsApp, o el usuario de plataforma. Que el cliente pueda aprobar su cotización
        // con el taller vencido es deliberado: la deuda es del taller, no suya.
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty) return null;

        // El login, el refresco y el cierre de sesión son POST. Bloquearlos dejaría al taller
        // vencido sin poder entrar ni siquiera a consultar, que es justo lo que sí puede hacer.
        if (context.Request.Path.StartsWithSegments("/api/auth")) return null;

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, context.RequestAborted);

        if (tenant is null) return null;

        // Una lectura por clave primaria, y solo en las peticiones que escriben —que son las
        // menos—. Sin caché a propósito: un estado de cobro que llega tarde es peor que una
        // consulta barata, en las dos direcciones (cobrar de más y cobrar de menos).
        var status = SubscriptionRules.For(tenant, clock.Today());

        return status.CanWrite ? null : status;
    }
}
