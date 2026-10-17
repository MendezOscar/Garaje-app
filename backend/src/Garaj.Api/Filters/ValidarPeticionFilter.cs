using FluentValidation;
using Garaj.Application.Common;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Garaj.Api.Filters;

/// <summary>
/// Corre el validador de FluentValidation que corresponda a cada argumento de la acción, si
/// existe. El paquete que trae el proyecto es el de inyección de dependencias, no el de MVC,
/// así que la integración con los controladores es esta: doce líneas en vez de una dependencia
/// más que además está marcada como obsoleta.
///
/// El error sale como <see cref="AppException"/> —400 con mensaje— porque es lo que el manejador
/// de excepciones ya sabe traducir y lo que el panel y la app ya saben mostrar. Un DTO sin
/// validador pasa de largo: la idea es ir cubriéndolos, no bloquear lo que todavía no lo tiene.
/// </summary>
public sealed class ValidarPeticionFilter(IServiceProvider servicios) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argumento in context.ActionArguments.Values.OfType<object>())
        {
            if (servicios.GetService(typeof(IValidator<>).MakeGenericType(argumento.GetType()))
                is not IValidator validador)
                continue;

            var resultado = await validador.ValidateAsync(
                new ValidationContext<object>(argumento), context.HttpContext.RequestAborted);

            if (!resultado.IsValid)
            {
                // Todos los mensajes juntos: el que llena un formulario prefiere que le digan
                // de una vez todo lo que le falta, no de uno en uno.
                throw new AppException(string.Join(" ", resultado.Errors.Select(e => e.ErrorMessage)));
            }
        }

        await next();
    }
}
