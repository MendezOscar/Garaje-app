namespace Garaj.Application.Abstractions;

/// <summary>
/// Guarda PDF ya renderizados un rato. Generar uno es lo más caro que hace el servidor —varias
/// consultas, bajar las fotos del bucket y componer la página—, y el mismo documento se pide
/// varias veces seguidas: el cliente abre su cotización, la cierra, la vuelve a abrir, y el
/// taller la manda otra vez por WhatsApp.
///
/// La clave lleva dentro la fecha de modificación del documento, así que editarlo invalida la
/// copia sin que nadie tenga que acordarse de borrarla.
/// </summary>
public interface IDocumentCache
{
    Task<byte[]> GetOrCreateAsync(string key, TimeSpan duration, Func<Task<byte[]>> render);
}
