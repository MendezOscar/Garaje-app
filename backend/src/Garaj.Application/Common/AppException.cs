using System.Net;

namespace Garaj.Application.Common;

/// <summary>
/// Error esperado de negocio. Un middleware lo traduce a ProblemDetails con su código,
/// de modo que los casos de uso no tengan que conocer HTTP.
/// </summary>
public class AppException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>
/// Se usa también cuando el recurso existe pero pertenece a otro tenant o a otro técnico:
/// devolver 404 en vez de 403 evita confirmar que el id existe.
/// </summary>
public class NotFoundException(string message = "El recurso no existe.")
    : AppException(message, HttpStatusCode.NotFound);

public class ForbiddenException(string message = "No tiene permiso para esta operación.")
    : AppException(message, HttpStatusCode.Forbidden);

public class ConflictException(string message)
    : AppException(message, HttpStatusCode.Conflict);

public class UnauthorizedException(string message = "Credenciales inválidas.")
    : AppException(message, HttpStatusCode.Unauthorized);

/// <summary>
/// El taller está al día con sus permisos pero no con su mensualidad. 402 y no 403 a propósito:
/// no es que no tenga derecho a hacerlo, es que hay que pagar para volver a hacerlo, y el panel
/// y la app distinguen los dos casos para decir cosas distintas.
/// </summary>
public class SubscriptionRequiredException(string message)
    : AppException(message, HttpStatusCode.PaymentRequired);
