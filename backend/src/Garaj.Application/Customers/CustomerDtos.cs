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
    int VehicleCount);

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

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken ct = default);
    Task<CustomerDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken ct = default);
}

public interface IVehicleService
{
    Task<PagedResult<VehicleDto>> ListAsync(VehicleQuery query, CancellationToken ct = default);
    Task<VehicleDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<VehicleDto> CreateAsync(SaveVehicleRequest request, CancellationToken ct = default);
    Task<VehicleDto> UpdateAsync(Guid id, SaveVehicleRequest request, CancellationToken ct = default);
}
