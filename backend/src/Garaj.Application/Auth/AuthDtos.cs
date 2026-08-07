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
public record CurrentUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid TenantId,
    string TenantName,
    IReadOnlyList<BranchSummaryDto> Branches,
    Guid? CustomerId);

public record BranchSummaryDto(Guid Id, string Name, string? Code);
