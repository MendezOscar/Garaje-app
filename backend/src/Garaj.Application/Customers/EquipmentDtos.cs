using Garaj.Application.Common;

namespace Garaj.Application.Customers;

/// <summary>Categoría de equipo del taller: "Laptop", "Aire acondicionado", "Celular".</summary>
public record EquipmentTypeDto(Guid Id, string Name, string? Icon, bool IsActive);

public record SaveEquipmentTypeRequest(string Name, string? Icon, bool IsActive = true);

public record EquipmentDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    Guid EquipmentTypeId,
    string EquipmentTypeName,
    string Brand,
    string Model,
    string? SerialNumber,
    string? Tag,
    string? Color,
    string? Accessories,
    /// <summary>Lo propio del tipo, como JSON. El servidor no lo interpreta.</summary>
    string? Attributes,
    string? Notes,
    bool IsActive);

public record SaveEquipmentRequest(
    Guid CustomerId,
    Guid EquipmentTypeId,
    string Brand,
    string Model,
    string? SerialNumber,
    string? Tag,
    string? Color,
    string? Accessories,
    string? Attributes,
    string? Notes,
    bool IsActive = true);

public record EquipmentQuery : PageQuery
{
    public string? Search { get; init; }
    public Guid? CustomerId { get; init; }
    public bool IncludeInactive { get; init; }
}

public interface IEquipmentService
{
    Task<PagedResult<EquipmentDto>> ListAsync(EquipmentQuery query, CancellationToken ct = default);
    Task<EquipmentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<EquipmentDto> CreateAsync(SaveEquipmentRequest request, CancellationToken ct = default);
    Task<EquipmentDto> UpdateAsync(Guid id, SaveEquipmentRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<EquipmentTypeDto>> TypesAsync(bool includeInactive, CancellationToken ct = default);
    Task<EquipmentTypeDto> CreateTypeAsync(SaveEquipmentTypeRequest request, CancellationToken ct = default);
    Task<EquipmentTypeDto> UpdateTypeAsync(Guid id, SaveEquipmentTypeRequest request, CancellationToken ct = default);
}
