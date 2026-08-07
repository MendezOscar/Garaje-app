namespace Garaj.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "garaj-api";
    public string Audience { get; set; } = "garaj-clients";

    /// <summary>Clave de firma HMAC. Mínimo 32 bytes; en producción viene de variable de entorno.</summary>
    public string SigningKey { get; set; } = null!;

    /// <summary>Corto a propósito: el daño de un token robado se limita a esta ventana.</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>Largo porque el técnico no debería re-loguearse a diario en el taller.</summary>
    public int RefreshTokenDays { get; set; } = 30;
}
