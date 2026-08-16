namespace Garaj.Application.Auth;

public record LoginRequest(string Email, string Password);

public record RefreshTokenRequest(string RefreshToken);

/// <param name="ExpiresAt">Vencimiento del access token; el cliente refresca antes de llegar aquí.</param>
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    CurrentUserDto User);

/// <summary>Lo que los tres clientes necesitan para armar su navegación tras el login.</summary>
/// <param name="TenantLogoUrl">
/// Ruta relativa a la base de la API, o null si el taller no tiene logo. Con ella el panel y
/// la app pintan «GarajApp para [logo del taller]».
/// </param>
/// <param name="Subscription">
/// Cómo va el taller con su mensualidad. **Solo se rellena para el Dueño**: el cobro es entre
/// él y nosotros, y que el técnico lea que su taller debe la mensualidad sería exponerlo
/// delante de su gente. Lo que no se manda no se puede filtrar mal en la pantalla.
/// </param>
public record CurrentUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid TenantId,
    string TenantName,
    string? TenantLogoUrl,
    IReadOnlyList<BranchSummaryDto> Branches,
    Guid? CustomerId,
    SubscriptionInfoDto? Subscription = null);

public record BranchSummaryDto(Guid Id, string Name, string? Code);

/// <summary>
/// El aviso de cobro tal como lo pinta el panel y la app. Trae el estado ya resuelto y el
/// texto ya escrito: no es el cliente quien decide qué significan las fechas.
/// </summary>
/// <param name="State">`Active`, `DueSoon`, `Grace`, `ReadOnly` o `Suspended`.</param>
/// <param name="ReadOnlyOn">Día en que, sin pagar, el taller deja de poder trabajar.</param>
/// <param name="AgreementThrough">Hasta cuándo vale el acuerdo de pago, si lo hay.</param>
public record SubscriptionInfoDto(
    string State,
    bool CanWrite,
    DateOnly? PaidThrough,
    int? DaysLeft,
    DateOnly? ReadOnlyOn,
    DateOnly? AgreementThrough,
    string? AgreementNote,
    string Message);
