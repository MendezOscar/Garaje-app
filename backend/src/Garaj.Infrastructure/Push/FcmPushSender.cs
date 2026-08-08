using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Garaj.Application.Notifications;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Garaj.Infrastructure.Push;

/// <summary>
/// Envío push por la API HTTP v1 de Firebase Cloud Messaging.
/// </summary>
/// <remarks>
/// Se habla con FCM por HTTP directo en lugar de con el SDK de administración de Firebase:
/// el SDK trae media docena de dependencias para hacer exactamente este POST. Lo único que
/// no es trivial es el token OAuth2 de la cuenta de servicio, y de eso se encarga
/// <c>Google.Apis.Auth</c>.
/// </remarks>
public class FcmPushSender : IPushSender
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly PushOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FcmPushSender> _logger;
    private readonly ITokenAccess? _credential;

    public FcmPushSender(
        IOptions<PushOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<FcmPushSender> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        if (!_options.IsConfigured) return;

        // La credencial cachea internamente el access token y lo renueva sola, así que se
        // construye una vez —el servicio es singleton— y no en cada envío.
        _credential = GoogleCredential.FromJson(_options.ServiceAccountJson).CreateScoped(Scope);
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<string> deviceTokens,
        NotificationDraft draft,
        CancellationToken ct = default)
    {
        if (_credential is null || deviceTokens.Count == 0) return [];

        var accessToken = await _credential.GetAccessTokenForRequestAsync(cancellationToken: ct);

        using var http = _httpClientFactory.CreateClient(nameof(FcmPushSender));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var url = $"https://fcm.googleapis.com/v1/projects/{_options.ProjectId}/messages:send";
        var dead = new List<string>();

        // Un POST por dispositivo. FCM v1 no tiene envío múltiple —el endpoint batch quedó
        // obsoleto— y son unos pocos aparatos por taller, no una campaña masiva.
        foreach (var token in deviceTokens)
        {
            var response = await http.PostAsJsonAsync(url, Payload(token, draft), ct);

            if (response.IsSuccessStatusCode) continue;

            // 404 UNREGISTERED es la app desinstalada o el token rotado; 400 suele ser un
            // token con formato inválido. Ninguno de los dos se arregla reintentando.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                dead.Add(token);
                continue;
            }

            _logger.LogWarning(
                "FCM respondió {Status} al enviar «{Title}»: {Body}",
                (int)response.StatusCode, draft.Title, await response.Content.ReadAsStringAsync(ct));
        }

        return dead;
    }

    /// <summary>
    /// Los datos van en <c>data</c> además de en <c>notification</c>: la app necesita el tipo
    /// y el id para saber a qué pantalla llevar al tocar el aviso, y FCM solo entrega cadenas.
    /// </summary>
    private static object Payload(string token, NotificationDraft draft) => new
    {
        message = new
        {
            token,
            notification = new { title = draft.Title, body = draft.Body },
            data = new Dictionary<string, string>
            {
                ["type"] = ((int)draft.Type).ToString(),
                ["workOrderId"] = draft.WorkOrderId?.ToString() ?? "",
                ["quoteId"] = draft.QuoteId?.ToString() ?? "",
                ["serviceRequestId"] = draft.ServiceRequestId?.ToString() ?? ""
            },
            android = new { priority = "high" },
            apns = new
            {
                payload = new { aps = new { sound = "default" } }
            }
        }
    };
}
