using Garaj.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// Caché propia y con tope, no la del resto de la aplicación: un PDF con fotos pesa cientos de
/// kilobytes y el contenedor de Render tiene poca memoria. Con <see cref="MemoryCacheOptions.SizeLimit"/>
/// medido en bytes, lo que no cabe desaloja a lo más viejo en vez de hinchar el proceso.
/// </summary>
public sealed class DocumentCache : IDocumentCache, IDisposable
{
    private const long LimiteEnBytes = 48 * 1024 * 1024;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = LimiteEnBytes });

    public async Task<byte[]> GetOrCreateAsync(string key, TimeSpan duration, Func<Task<byte[]>> render)
    {
        if (_cache.TryGetValue(key, out byte[]? guardado) && guardado is not null)
            return guardado;

        var bytes = await render();

        // Uno solo más grande que el tope no se guarda —MemoryCache lo rechaza— y se devuelve
        // igual: vale más servirlo lento que no servirlo.
        _cache.Set(key, bytes, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = duration,
            Size = bytes.Length,
        });

        return bytes;
    }

    public void Dispose() => _cache.Dispose();
}
