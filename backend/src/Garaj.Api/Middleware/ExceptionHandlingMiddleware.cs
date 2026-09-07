using System.Net;
using Garaj.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Api.Middleware;

/// <summary>
/// Traduce las excepciones a ProblemDetails. Los errores de negocio (<see cref="AppException"/>)
/// llevan su mensaje al cliente; cualquier otra cosa se registra y sale como 500 genérico,
/// para no filtrar detalles internos.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            logger.LogInformation(ex, "Error de negocio en {Path}: {Message}", context.Request.Path, ex.Message);
            await WriteProblemAsync(context, ex.StatusCode, ex.Message);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Dos operaciones tocaron la misma fila a la vez —las existencias del último
            // repuesto, típicamente—. No es un fallo del servidor: el segundo tiene que volver
            // a intentarlo con el dato fresco, y se le dice en esos términos.
            logger.LogWarning(ex, "Choque de concurrencia en {Path}", context.Request.Path);
            await WriteProblemAsync(context, HttpStatusCode.Conflict,
                "Alguien más cambió esto mientras usted trabajaba. Vuelva a intentarlo.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error no controlado en {Path}", context.Request.Path);
            await WriteProblemAsync(context, HttpStatusCode.InternalServerError,
                "Ocurrió un error inesperado. Intente nuevamente.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, HttpStatusCode status, string detail)
    {
        if (context.Response.HasStarted) return;

        context.Response.Clear();
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = (int)status,
            Title = ReasonPhrase(status),
            Detail = detail,
            Instance = context.Request.Path
        });
    }

    private static string ReasonPhrase(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => "No encontrado",
        HttpStatusCode.Forbidden => "Acceso denegado",
        HttpStatusCode.Unauthorized => "No autenticado",
        HttpStatusCode.Conflict => "Conflicto",
        HttpStatusCode.PaymentRequired => "Suscripción vencida",
        HttpStatusCode.InternalServerError => "Error interno",
        _ => "Solicitud inválida"
    };
}
