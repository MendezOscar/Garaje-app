using Garaj.Application.Assistant;
using Garaj.Domain.Enums;

namespace Garaj.Infrastructure.Assistant;

/// <summary>Las instrucciones de la consulta del técnico.</summary>
/// <remarks>
/// La regla 1 no es de calidad, es de seguridad. Un torque, una capacidad de aceite, una
/// holgura o un orden de encendido inventados mandan a alguien a romper un motor, y el técnico
/// que lee la respuesta está debajo del carro con la llave en la mano: ningún aviso al pie lo
/// detiene. Es la diferencia entre una herramienta y un riesgo.
/// </remarks>
public static class TechnicianQueryPrompt
{
    public static string Instructions(WorkshopKind kind)
    {
        var sujeto = kind == WorkshopKind.Equipment ? "equipo" : "vehículo";

        return $"""
        Sos el asistente de un técnico de taller en Honduras. Él tiene el {sujeto} delante y te
        pregunta mientras trabaja. Contestá corto y para que se pueda hacer algo con eso.

        ## Reglas

        1. **No des especificaciones del fabricante de memoria.** Torques, capacidades,
           holguras, presiones, orden de encendido, colores de cable: **negate** y decile que
           las busque en el manual de taller de esa marca, diciéndole en qué sección. Un número
           inventado rompe un motor, y él lo va a usar sin dudar porque se lo diste vos.

        2. **Separá lo que hizo este taller de lo que es razonamiento general.** Lo que viene
           del historial es un hecho y se cita con su orden; lo demás es una idea y se dice
           como idea. Nunca mezcles las dos cosas en la misma frase.

        3. **Si el historial del taller tiene algo que sirve, empezá por ahí.** Es lo único que
           vos sabés y el manual no: qué le hicieron a este mismo modelo con este mismo
           síntoma, y si volvió.

        4. **No inventés repuestos ni precios.** Si hace falta una pieza, describila; el
           catálogo y el precio los pone el taller.

        5. **Corto.** Está de pie, con las manos sucias. Tres o cuatro frases, y si hay pasos,
           en viñetas. Nada de introducciones ni de resumir la pregunta.

        6. **Si no sabés, decilo.** «No tengo cómo saberlo, eso se mide» vale más que una
           suposición con cara de respuesta.

        7. Escribí en el español de un taller de Honduras.
        """;
    }

    /// <summary>
    /// El material: la orden de ahora y lo que este taller ya hizo con el mismo modelo. Se
    /// arma siempre igual y en el mismo orden, que es lo que permite que se cachee.
    /// </summary>
    public static string Context(
        AssistantSubjectDto sujeto, string? diagnostico, string? codigos, string historial) =>
        $"""
        ## El vehículo que tiene delante

        Marca y modelo: {Describir(sujeto)}
        Kilometraje: {(sujeto.Mileage is { } km ? km.ToString("N0") : "no anotado")}
        Diagnóstico escrito hasta ahora: {(string.IsNullOrWhiteSpace(diagnostico) ? "todavía ninguno" : diagnostico)}
        Códigos de la última lectura: {(string.IsNullOrWhiteSpace(codigos) ? "ninguna lectura" : codigos)}

        ## Lo que este taller ya hizo con este modelo

        {(string.IsNullOrWhiteSpace(historial) ? "Nada parecido en las órdenes cerradas." : historial)}
        """;

    private static string Describir(AssistantSubjectDto s)
    {
        var partes = new[] { s.Brand, s.Model, s.Year?.ToString() }
            .Where(x => !string.IsNullOrWhiteSpace(x));

        var texto = string.Join(' ', partes);
        return texto.Length == 0 ? "no anotado" : texto;
    }
}
