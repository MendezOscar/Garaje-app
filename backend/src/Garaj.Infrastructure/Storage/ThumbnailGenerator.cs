using SkiaSharp;

namespace Garaj.Infrastructure.Storage;

/// <summary>
/// Genera la miniatura de una foto. Existe porque la galería del detalle de orden carga diez
/// o quince fotos a la vez y en el taller la cobertura es mala: bajar los originales para
/// pintarlos a 100 px sería inutilizable.
/// </summary>
public static class ThumbnailGenerator
{
    public const string ContentType = "image/jpeg";

    /// <summary>
    /// Devuelve el JPEG de la miniatura, o null si el archivo no es una imagen que se pueda
    /// decodificar (un PDF adjunto, por ejemplo). No es un error: la galería cae al original.
    /// </summary>
    public static byte[]? Create(Stream original, int maxSide)
    {
        using var bitmap = SKBitmap.Decode(original);
        if (bitmap is null) return null;

        var scale = (float)maxSide / Math.Max(bitmap.Width, bitmap.Height);

        // Una foto ya pequeña se reencoda igual: comprimida a calidad 70 pesa bastante menos
        // que el original del teléfono, que es lo que se busca.
        var width = scale < 1 ? (int)Math.Round(bitmap.Width * scale) : bitmap.Width;
        var height = scale < 1 ? (int)Math.Round(bitmap.Height * scale) : bitmap.Height;

        using var resized = bitmap.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
        if (resized is null) return null;

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 70);

        return data?.ToArray();
    }
}
