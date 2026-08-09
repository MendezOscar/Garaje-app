using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Customers;

public record CustomerDto(
    Guid Id,
    string FullName,
    string Phone,
    string? Email,
    string? DocumentId,
    string? Address,
    string? Notes,
    bool IsActive,
    int VehicleCount,
    // Si el cliente puede entrar a la app, y con qué correo. Null cuando no tiene acceso: la
    // mayoría de los clientes de un taller nunca lo pide.
    bool HasAppAccess,
    string? AppUserEmail);

public record SaveCustomerRequest(
    string FullName,
    string Phone,
    string? Email,
    string? DocumentId,
    string? Address,
    string? Notes,
    bool IsActive = true);

/// <param name="Search">Busca por nombre, teléfono o placa de alguno de sus vehículos.</param>
public record CustomerQuery : PageQuery
{
    public string? Search { get; init; }
    public bool IncludeInactive { get; init; }
}

public record VehicleDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    VehicleType Type,
    string Brand,
    string Model,
    int? Year,
    string? Plate,
    string? Vin,
    string? Color,
    int? Mileage,
    string? Notes,
    bool IsActive);

public record SaveVehicleRequest(
    Guid CustomerId,
    VehicleType Type,
    string Brand,
    string Model,
    int? Year,
    string? Plate,
    string? Vin,
    string? Color,
    int? Mileage,
    string? Notes,
    bool IsActive = true);

public record VehicleQuery : PageQuery
{
    public string? Search { get; init; }
    public Guid? CustomerId { get; init; }
    public bool IncludeInactive { get; init; }
}

/// <summary>Le abre acceso a la app a un cliente que ya está en el padrón.</summary>
public record GrantAppAccessRequest(string Email, string Password);

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken ct = default);
    Task<CustomerDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken ct = default);

    /// <summary>Crea el usuario del cliente y lo deja enlazado. Devuelve el cliente ya con acceso.</summary>
    Task<CustomerDto> GrantAppAccessAsync(
        Guid id, GrantAppAccessRequest request, CancellationToken ct = default);
}

public interface IVehicleService
{
    Task<PagedResult<VehicleDto>> ListAsync(VehicleQuery query, CancellationToken ct = default);
    Task<VehicleDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<VehicleDto> CreateAsync(SaveVehicleRequest request, CancellationToken ct = default);
    Task<VehicleDto> UpdateAsync(Guid id, SaveVehicleRequest request, CancellationToken ct = default);
}
