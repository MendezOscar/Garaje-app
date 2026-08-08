namespace Garaj.Infrastructure.Push;

/// <summary>
/// Credenciales del proyecto de Firebase. Sin ellas la aplicación arranca igual y los avisos
/// se quedan en la campana: el push es un complemento, no un requisito para operar.
/// </summary>
public class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Id del proyecto de Firebase, ej. "garaj-app".</summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// JSON de la cuenta de servicio, entero y en una sola variable de entorno
    /// (<c>Push__ServiceAccountJson</c>). Es una credencial: nunca en appsettings.json.
    /// </summary>
    public string? ServiceAccountJson { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ServiceAccountJson);
}
