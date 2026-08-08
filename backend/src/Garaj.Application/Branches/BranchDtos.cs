namespace Garaj.Application.Branches;

public record BranchDto(
    Guid Id,
    string Name,
    string? Code,
    string? Address,
    string? City,
    string? Phone,
    bool IsActive);

public record SaveBranchRequest(
    string Name,
    string? Code,
    string? Address,
    string? City,
    string? Phone,
    bool IsActive = true);

public interface IBranchService
{
    Task<IReadOnlyList<BranchDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<BranchDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<BranchDto> CreateAsync(SaveBranchRequest request, CancellationToken ct = default);
    Task<BranchDto> UpdateAsync(Guid id, SaveBranchRequest request, CancellationToken ct = default);
}
