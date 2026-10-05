using Garaj.Domain.Enums;

namespace Garaj.Application.Users;

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    Guid? CustomerId,
    IReadOnlyList<Guid> BranchIds,
    DateTimeOffset? LastLoginAt,
    // Cómo se le paga. Solo tiene sentido en técnicos.
    TechnicianPayMode PayMode,
    decimal PayAmount);

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
    IReadOnlyList<Guid>? BranchIds,
    // Cómo se le paga: sueldo fijo, porcentaje de la mano de obra que generó, o por hora.
    // Sin definir, el sistema no propone nada al pagarle.
    TechnicianPayMode PayMode = TechnicianPayMode.Undefined,
    decimal PayAmount = 0);

public record ResetPasswordRequest(string NewPassword);

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(string? role, CancellationToken ct = default);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cuánto le tocaría a este técnico por el periodo, según cómo se le paga. Es una
    /// propuesta para la pantalla de pago: lo que se le paga de verdad lo escribe el Dueño.
    /// </summary>
    Task<TechnicianPayProposalDto> PayProposalAsync(
        Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <param name="LaborRevenue">La mano de obra facturada de sus órdenes en el periodo.</param>
/// <param name="Hours">Las horas que registró en los pasos que completó.</param>
/// <param name="AlreadyPaid">Lo que ya se le pagó en el periodo, de los gastos de salario.</param>
public record TechnicianPayProposalDto(
    Guid TechnicianId,
    string TechnicianName,
    TechnicianPayMode PayMode,
    decimal PayAmount,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal LaborRevenue,
    decimal Hours,
    decimal Proposal,
    decimal AlreadyPaid);
