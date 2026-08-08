using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Microsoft.Extensions.Options;

namespace Garaj.Infrastructure.Storage;

/// <summary>
/// Implementación S3 que sirve tanto a MinIO como a Cloudflare R2. No hay una clase por
/// proveedor porque el protocolo es el mismo; lo único que cambia es el endpoint.
/// </summary>
public class S3StorageService : IStorageService, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly StorageOptions _options;

    public S3StorageService(IOptions<StorageOptions> options)
    {
        _options = options.Value;

        // Se valida aquí y no al arrancar a propósito: sin bucket configurado el taller
        // sigue operando —órdenes, pasos, estados— y solo fallan las fotos, con un mensaje
        // que dice qué falta. Tumbar la API entera por esto sería peor.
        if (!_options.IsConfigured)
            throw new AppException(
                "El almacenamiento de fotos no está configurado. Defina Storage__ServiceUrl, " +
                "Storage__AccessKey y Storage__SecretKey en el entorno.",
                System.Net.HttpStatusCode.ServiceUnavailable);

        _client = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                ForcePathStyle = _options.ForcePathStyle,
                AuthenticationRegion = _options.Region,
                // R2 y MinIO rechazan la petición si el SDK le añade el checksum de integridad
                // que AWS activó por omisión: no implementan esa extensión.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
    }

    /// <summary>
    /// El SDK genera siempre las URL prefirmadas en https, aunque el endpoint configurado sea
    /// http: en desarrollo devolvía https://localhost:9010 y MinIO cortaba el handshake TLS.
    /// El esquema no entra en la firma SigV4 —que cubre método, ruta, query y la cabecera
    /// host—, así que corregirlo aquí no la invalida. En producción, contra R2, no hace nada.
    /// </summary>
    private string WithConfiguredScheme(string url) =>
        _options.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? string.Concat("http://", url.AsSpan("https://".Length))
            : url;

    public Task<PresignedUpload> CreateUploadUrlAsync(
        string key, string contentType, TimeSpan expiresIn, CancellationToken ct = default)
    {
        var expiresAt = DateTime.UtcNow.Add(expiresIn);

        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            // Va dentro de la firma: si el cliente sube con otro Content-Type, el bucket
            // devuelve 403. Por eso se le devuelve la cabecera exacta que debe repetir.
            ContentType = contentType
        });

        return Task.FromResult(new PresignedUpload(
            WithConfiguredScheme(url),
            key,
            new Dictionary<string, string> { ["Content-Type"] = contentType },
            new DateTimeOffset(expiresAt, TimeSpan.Zero)));
    }

    public Task<string> GetDownloadUrlAsync(string key, TimeSpan expiresIn, CancellationToken ct = default) =>
        Task.FromResult(WithConfiguredScheme(_client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiresIn)
        })));

    public async Task UploadAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            // Sin esto el SDK intenta calcular la longitud rebobinando el stream, y los
            // streams de red no siempre lo permiten.
            AutoCloseStream = false,
            UseChunkEncoding = false
        }, ct);
    }

    public async Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_options.Bucket, key, ct);

            // Se copia a memoria: el llamador cierra el stream cuando quiere y la conexión
            // HTTP con el bucket no queda abierta mientras tanto.
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NotFoundException("El archivo no existe en el almacenamiento.");
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(_options.Bucket, key, ct);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public void Dispose() => _client.Dispose();
}
