using Garaj.Infrastructure.Auth;

namespace Garaj.Api.Services;

/// <summary>Expone IP y user-agent al servicio de autenticación sin acoplarlo a ASP.NET.</summary>
public class HttpRequestInfo(IHttpContextAccessor accessor) : IHttpContextAccessorAdapter
{
    public string? RemoteIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : Truncate(value, 300);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
