namespace Garaj.Infrastructure.Identity;

/// <summary>
/// Refresh token persistido y rotativo. Se guarda solo el hash: si alguien lee la tabla no
/// obtiene tokens usables. Al usar uno ya consumido se revoca toda la cadena
/// (<see cref="ReplacedByTokenHash"/>), porque significa que fue interceptado.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>SHA-256 del token entregado al cliente. El valor en claro nunca se almacena.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Hash del token que lo sustituyó al rotar. Permite seguir la cadena hacia adelante.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Origen de la emisión, para poder cerrar sesión de un dispositivo concreto.</summary>
    public string? CreatedByIp { get; set; }
    public string? DeviceInfo { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    public AppUser User { get; set; } = null!;
}
