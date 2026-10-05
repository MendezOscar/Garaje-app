using Garaj.Application.Common;
using Garaj.Domain.Enums;

namespace Garaj.Application.Sales;

/// <summary>
/// Un reclamo como se lee en una lista: de quién, sobre qué trabajo y en qué va.
/// </summary>
public record ClaimListItemDto(
    Guid Id,
    string Number,
    ClaimStatus Status,
    Guid SaleId,
    string SaleNumber,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    string? CustomerName,
    string? CustomerPhone,
    string? VehicleLabel,
    string Reason,
    bool WasUnderWarranty,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ResolvedAt,
    // La orden que se abrió para repararlo, si hubo que abrirla.
    Guid? RepairWorkOrderId,
    string? RepairWorkOrderNumber);

public record ClaimDetailDto(
    Guid Id,
    string Number,
    ClaimStatus Status,
    Guid SaleId,
    string SaleNumber,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    string? VehicleLabel,
    string Reason,
    bool WasUnderWarranty,
    DateTimeOffset? WarrantyUntil,
    DateTimeOffset ReceivedAt,
    string? ReceivedByName,
    string? Resolution,
    DateTimeOffset? ResolvedAt,
    string? ResolvedByName,
    Guid? RepairWorkOrderId,
    string? RepairWorkOrderNumber,
    // Lo que costó repararlo, cuando se abrió orden: repuestos y mano de obra de esa orden.
    // Es el número que dice cuánto le cuesta la garantía al taller.
    decimal RepairCost);

/// <param name="SaleId">
/// El trabajo que se reclama. Va la venta y no la orden porque es la venta la que lleva la
/// garantía, y porque un servicio rápido se reclama igual sin tener orden.
/// </param>
public record CreateClaimRequest(Guid SaleId, string Reason);

/// <param name="OpenRepairOrder">
/// Abre una orden de trabajo nueva para repararlo, ligada a este reclamo. Es trabajo nuevo:
/// tiene sus pasos y sus repuestos, y así se puede saber cuánto costó la garantía.
/// </param>
public record ResolveClaimRequest(
    ClaimStatus Status,
    string Resolution,
    bool OpenRepairOrder = false);

public record ClaimQuery : PageQuery
{
    public ClaimStatus? Status { get; init; }

    /// <summary>Solo los que siguen abiertos. Es la lista con la que se trabaja.</summary>
    public bool OnlyOpen { get; init; }

    public Guid? CustomerId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public interface IClaimService
{
    Task<PagedResult<ClaimListItemDto>> ListAsync(ClaimQuery query, CancellationToken ct = default);
    Task<ClaimDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<ClaimDetailDto> CreateAsync(CreateClaimRequest request, CancellationToken ct = default);

    /// <summary>Lo cierra: reparado en garantía, reparado y cobrado, o no procede.</summary>
    Task<ClaimDetailDto> ResolveAsync(
        Guid id, ResolveClaimRequest request, CancellationToken ct = default);

    /// <summary>Lo vuelve a abrir, cuando se cerró por error.</summary>
    Task<ClaimDetailDto> ReopenAsync(Guid id, CancellationToken ct = default);
}
