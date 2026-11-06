using System.Text.Json;
using Garaj.Application.Assistant;
using Garaj.Domain.Enums;

namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// Las instrucciones y la forma de la respuesta de «Proponer los pasos».
/// </summary>
/// <remarks>
/// Es el mismo prompt que se midió en <c>pruebas/ia-pasos</c> contra casos reales de la base,
/// con las cuatro correcciones que esa medición dejó claras: las cantidades no siempre
/// cuadraban, se olvidaban los consumibles que sí se cobran, partía el trabajo más fino de lo
/// que el taller lo cobra, y la nota final salía demasiado larga —de ahí venía el 28% de
/// sobrecosto, porque el costo de esta función está en la salida, no en la entrada—.
///
/// Va en una constante y no en un archivo suelto porque es el prefijo que se cachea: tiene que
/// ser byte por byte el mismo entre llamadas o el caché deja de servir sin avisar.
/// </remarks>
public static class WorkStepsPrompt
{
    public static string Instructions(WorkshopKind kind)
    {
        var sujeto = kind == WorkshopKind.Equipment ? "equipo" : "vehículo";

        return $"""
        Sos el asistente de un taller en Honduras. A partir del diagnóstico que escribió el
        técnico, proponés los pasos del trabajo y los repuestos que llevan.

        Lo que proponés lo revisa el dueño del taller antes de que valga: corrige, tacha lo que
        no y acepta. Así que proponé lo que de verdad haría falta, ni de más por si acaso ni de
        menos.

        ## Reglas

        1. **Los repuestos salen del catálogo que se te da, y de ninguna otra parte.** Cada uno
           se identifica por su `sku` exacto, copiado tal cual. Si lo que hace falta no está en
           el catálogo, no lo inventés: dejá el paso sin repuesto y decilo en `nota`. Un paso
           sin repuesto se arregla en diez segundos; un repuesto que el taller no tiene hace
           perder una mañana.

        2. **Mirá para qué {sujeto}s sirve cada repuesto** antes de proponerlo. Una pastilla de
           otro modelo no sirve aunque se llame igual.

        3. **Cada paso lleva un servicio del catálogo de mano de obra** (`codigoManoDeObra`),
           si hay uno que le calce. Si ninguno calza, dejalo en null y poné las horas que te
           parezcan.

        4. **No pongás precios.** Ni de repuestos ni de mano de obra. Los pone el taller con su
           catálogo.

        5. **Los pasos van en el orden en que se hacen**, y cada uno es una cosa que alguien
           hace: «Desmontar el múltiple de admisión», no «Reparación del motor».

        6. **Si el diagnóstico no alcanza para proponer nada**, no adivinés: devolvé la lista de
           pasos vacía y escribí en `nota` qué haría falta averiguar primero. Pasa seguido, y es
           una respuesta correcta.

        7. **No inventés especificaciones del fabricante** —torques, capacidades, holguras—. Si
           un paso las necesita, decí en su descripción que se consulten en el manual.

        8. Escribí en el español de un taller de Honduras: claro, corto, sin palabras de
           catálogo.

        9. **Partí el trabajo como lo cobra el taller, no como lo haría un manual.** Un paso es
           algo que se le puede poner precio y marcar como hecho. «Cambiar las pastillas
           delanteras» es un paso; desmontar la rueda, limpiar la mordaza y sangrar no son tres.
           Rara vez pasan de seis u ocho pasos.

        10. **Contá bien las cantidades.** Lo que va de a dos va de a dos —pastillas, amortigua-
            dores, bujías según los cilindros—, y lo que se mide en litros va en litros. Si no
            estás seguro de cuánto lleva, poné lo mínimo razonable y decilo en `nota`.

        11. **No te olvidés de los consumibles que el taller cobra**: empaques, retenedores,
            líquido de frenos, refrigerante, filtros que se cambian con el trabajo. Si están en
            el catálogo y el trabajo los lleva, van. Es trabajo que el taller a veces deja de
            cobrar.

        12. **Lo que haga falta y no esté en el catálogo va en `faltantes`**, no en los pasos
            ni escondido en la nota. Ahí se escribe con el nombre con el que se pide en una
            casa de repuestos —«retenedor de cigüeñal delantero», no «pieza de sellado»— y con
            la cantidad. Lo que sí está en el catálogo **nunca** va aquí.

        13. **La nota es de una o dos líneas.** Solo lo que el dueño necesita saber para
            decidir: qué falta averiguar, o qué repuesto hizo falta y no estaba. Nada de
            resumir lo que ya está en los pasos.
        """;
    }

    /// <summary>
    /// El catálogo filtrado, como texto. Se arma siempre igual y en el mismo orden: va dentro
    /// del prefijo cacheado, y un orden distinto lo invalida.
    /// </summary>
    public static string Context(AssistantCatalogDto catalogo) =>
        $"""
        ## Catálogo del taller

        ### Mano de obra
        {JsonSerializer.Serialize(catalogo.Services.Select(s => new
        {
            codigo = s.Code,
            nombre = s.Name,
            categoria = s.Category,
            horas = s.StandardHours
        }))}

        ### Repuestos
        {JsonSerializer.Serialize(catalogo.Parts.Select(p => new
        {
            sku = p.Sku,
            nombre = p.Name,
            marca = p.Brand,
            categoria = p.Category,
            unidad = p.Unit
        }))}
        """;

    /// <summary>El caso. Es lo único que cambia entre una llamada y la siguiente.</summary>
    public static string Question(AssistantSubjectDto sujeto, string diagnostico, string? codigos)
    {
        var descripcion = string.Join(' ', new[]
        {
            sujeto.Brand, sujeto.Model, sujeto.Year?.ToString()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var texto =
            $"Vehículo: {(descripcion.Length == 0 ? "no anotado" : descripcion)}\n" +
            $"Kilometraje: {(sujeto.Mileage is { } km ? km.ToString("N0") : "no anotado")}\n";

        if (!string.IsNullOrWhiteSpace(codigos))
            texto += $"Códigos de la última lectura del escáner: {codigos}\n";

        return $"{texto}\nDiagnóstico del técnico:\n{diagnostico}";
    }

    /// <summary>
    /// El esquema de la respuesta, estricto. No texto que después haya que interpretar: lo que
    /// no cuadre con esto no llega, y lo que llegue se valida igual contra el catálogo.
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } =
        new Dictionary<string, JsonElement>
        {
            ["type"] = J("object"),
            ["additionalProperties"] = J(false),
            ["required"] = J(new[] { "pasos", "repuestos", "faltantes", "nota" }),
            ["properties"] = J(new
            {
                pasos = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "titulo", "descripcion", "codigoManoDeObra", "horas" },
                        properties = new
                        {
                            titulo = new { type = "string" },
                            descripcion = new { type = "string" },
                            codigoManoDeObra = new { type = new[] { "string", "null" } },
                            horas = new { type = "number" }
                        }
                    }
                },
                repuestos = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "sku", "cantidad", "paraElPaso" },
                        properties = new
                        {
                            sku = new { type = "string" },
                            cantidad = new { type = "number" },
                            paraElPaso = new { type = "string" }
                        }
                    }
                },
                faltantes = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "nombre", "cantidad", "paraElPaso" },
                        properties = new
                        {
                            nombre = new { type = "string" },
                            cantidad = new { type = "number" },
                            paraElPaso = new { type = "string" }
                        }
                    }
                },
                nota = new { type = new[] { "string", "null" } }
            })
        };

    private static JsonElement J(object valor) => JsonSerializer.SerializeToElement(valor);
}

/// <summary>Lo que devuelve el modelo, antes de validarlo. Viene de afuera: no se confía.</summary>
public record RespuestaDelAsistente(
    List<PasoPropuesto> Pasos,
    List<RepuestoPropuesto> Repuestos,
    List<FaltantePropuesto>? Faltantes,
    string? Nota);

public record PasoPropuesto(string Titulo, string? Descripcion, string? CodigoManoDeObra, decimal Horas);

public record RepuestoPropuesto(string Sku, decimal Cantidad, string? ParaElPaso);

/// <summary>Algo que el trabajo lleva y el taller no tiene en catálogo.</summary>
public record FaltantePropuesto(string Nombre, decimal Cantidad, string? ParaElPaso);
