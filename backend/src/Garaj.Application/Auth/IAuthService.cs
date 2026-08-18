namespace Garaj.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Rota el refresh token: el recibido queda revocado y se emite uno nuevo. Si llega un
    /// token ya usado se revoca toda la cadena, porque implica que alguien lo interceptó.
    /// </summary>
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Elimina la cuenta de quien la pide. Cierra la sesión en todos sus aparatos y borra sus
    /// datos personales; el trabajo del taller que lleva su firma se conserva anonimizado.
    /// </summary>
    Task DeleteMyAccountAsync(DeleteAccountRequest request, CancellationToken ct = default);
}
