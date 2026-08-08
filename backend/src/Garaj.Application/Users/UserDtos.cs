namespace Garaj.Application.Users;

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    Guid? CustomerId,
    IReadOnlyList<Guid> BranchIds,
    DateTimeOffset? LastLoginAt);

/// <param name="BranchIds">Sucursales del Técnico. Se ignora en Dueño, que las ve todas.</param>
/// <param name="CustomerId">Obligatorio al crear un usuario con perfil Cliente.</param>
public record CreateUserRequest(
    string Email,
    string FullName,
    string Role,
    string Password,
    IReadOnlyList<Guid>? BranchIds,
    Guid? CustomerId);

public record UpdateUserRequest(
    string FullName,
    bool IsActive,
    IReadOnlyList<Guid>? BranchIds);

public record ResetPasswordRequest(string NewPassword);

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(string? role, CancellationToken ct = default);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
}
