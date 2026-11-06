using Garaj.Domain.Enums;

namespace Garaj.Application.VehicleScans;

/// <param name="Code">El código tal como lo leyó el adaptador. Se normaliza a mayúsculas.</param>
/// <param name="Description">
/// Qué significa, resuelto por la app con su tabla. Viene del cliente y no del servidor
/// porque la tabla tiene que servir también cuando el taller está sin señal, que es
/// precisamente cuando se escanea: el carro está en la rampa, no en la oficina.
/// </param>
public record ScanCodeInput(string Code, DtcStatus Status, string? Description);

/// <param name="ScannedAt">
/// Cuándo se leyó, según el teléfono. Se pide explícita por lo mismo que en las fotos: la
/// lectura puede subirse más tarde, cuando el teléfono recupere la señal.
/// </param>
public record CreateVehicleScanRequest(
    Guid WorkOrderId,
    DateTimeOffset ScannedAt,
    IReadOnlyList<ScanCodeInput> Codes,
    string? Vin,
    int? Mileage,
    string? DeviceName,
    string? Protocol,
    string? Notes);

public record VehicleScanCodeDto(string Code, DtcStatus Status, string? Description, bool IsGeneric);

/// <param name="AttachmentId">
/// El PDF de la lectura que quedó colgado de la orden, para abrirlo desde aquí. Null si no se
/// pudo generar; la lectura se ve igual en pantalla.
/// </param>
public record VehicleScanDto(
    Guid Id,
    Guid WorkOrderId,
    DateTimeOffset ScannedAt,
    string ScannedByName,
    IReadOnlyList<VehicleScanCodeDto> Codes,
    string? Vin,
    int? Mileage,
    string? DeviceName,
    string? Protocol,
    string? Notes,
    Guid? AttachmentId);

/// <summary>
/// Una aparición de un código, para la búsqueda. Lleva el vehículo y la orden porque la
/// pregunta que se hace el taller es «a qué carros les ha salido esto», no «en qué lecturas».
/// </summary>
public record VehicleScanHitDto(
    Guid ScanId,
    Guid WorkOrderId,
    string WorkOrderNumber,
    DateTimeOffset ScannedAt,
    string Code,
    DtcStatus Status,
    string? Description,
    Guid? VehicleId,
    string? Plate,
    string VehicleLabel,
    string CustomerName);

public interface IVehicleScanService
{
    /// <summary>
    /// Guarda la lectura y arma su informe en PDF, que queda adjunto a la orden y de ahí sale
    /// en la cotización, igual que el informe de la tableta.
    /// </summary>
    Task<VehicleScanDto> CreateAsync(CreateVehicleScanRequest request, CancellationToken ct = default);

    /// <summary>Las lecturas de una orden, de la más reciente a la más vieja.</summary>
    Task<IReadOnlyList<VehicleScanDto>> ListForWorkOrderAsync(Guid workOrderId, CancellationToken ct = default);

    /// <summary>Las lecturas de un vehículo a lo largo del tiempo: su historial de fallas.</summary>
    Task<IReadOnlyList<VehicleScanDto>> ListForVehicleAsync(Guid vehicleId, CancellationToken ct = default);

    /// <summary>
    /// Dónde ha salido este código en el taller. Es lo que el informe adjunto no podía dar y
    /// la razón de que los códigos se guarden uno por uno.
    /// </summary>
    Task<IReadOnlyList<VehicleScanHitDto>> SearchByCodeAsync(string code, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
