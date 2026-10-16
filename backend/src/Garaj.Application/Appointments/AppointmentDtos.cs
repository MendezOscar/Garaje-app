using Garaj.Application.Common;
using Garaj.Application.Tenants;
using Garaj.Domain.Enums;

namespace Garaj.Application.Appointments;

/// <summary>Una cita como la ve el taller.</summary>
/// <param name="VehicleLabel">
/// Qué vehículo es: el del padrón cuando lo hay, y si no lo que la persona dijo por el enlace.
/// </param>
public record AppointmentDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    Guid? CustomerId,
    Guid? VehicleId,
    string ContactName,
    string ContactPhone,
    string VehicleLabel,
    string Reason,
    DateOnly Day,
    AppointmentSlot Slot,
    AppointmentStatus Status,
    AppointmentSource Source,
    string? ResponseNote,
    Guid? WorkOrderId,
    string? WorkOrderNumber,
    Guid PublicToken,
    DateTimeOffset CreatedAt);

public record AppointmentQuery : PageQuery
{
    public Guid? BranchId { get; init; }
    public AppointmentStatus? Status { get; init; }

    /// <summary>Rango de días de la agenda. Sin rango, de hoy en adelante.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Incluye lo ya cerrado —atendidas, rechazadas y canceladas—. Por defecto, no.</summary>
    public bool IncludeClosed { get; init; }
}

/// <summary>
/// Lo que pide el cliente desde la app. El vehículo es suyo, así que no se describe: se elige.
/// </summary>
public record RequestAppointmentRequest(
    Guid BranchId,
    Guid VehicleId,
    string Reason,
    DateOnly Day,
    AppointmentSlot Slot);

/// <summary>Lo que anota el taller por teléfono o en el mostrador.</summary>
public record CreateAppointmentRequest(
    Guid BranchId,
    string ContactName,
    string ContactPhone,
    string? VehicleNote,
    Guid? CustomerId,
    Guid? VehicleId,
    string Reason,
    DateOnly Day,
    AppointmentSlot Slot);

/// <summary>
/// Confirmar puede mover el día: el taller contesta «ese día no, pero el jueves sí» sin
/// obligar al cliente a pedirla de nuevo.
/// </summary>
public record ConfirmAppointmentRequest(DateOnly? Day, AppointmentSlot? Slot, string? Note);

public record RejectAppointmentRequest(string Reason);

/// <summary>El taller en el enlace público: lo que ya está en la puerta del local.</summary>
/// <param name="LastDay">
/// Hasta qué día se puede agendar, para que el calendario no ofrezca el año que viene.
/// </param>
public record BookingPageDto(
    string TenantName,
    bool HasLogo,
    bool Enabled,
    IReadOnlyCollection<BookingBranchDto> Branches,
    DateOnly LastDay);

public record BookingBranchDto(Guid Id, string Name, string? Address, string? Phone);

/// <summary>Lo que manda quien no tiene —ni quiere— la aplicación.</summary>
public record BookAppointmentRequest(
    Guid BranchId,
    string ContactName,
    string ContactPhone,
    string? VehicleNote,
    string Reason,
    DateOnly Day,
    AppointmentSlot Slot);

/// <summary>La cita como la ve su dueño desde el enlace, sin nada del taller por dentro.</summary>
public record PublicAppointmentDto(
    Guid Token,
    string TenantName,
    string BranchName,
    string? BranchAddress,
    string? BranchPhone,
    string ContactName,
    string VehicleLabel,
    string Reason,
    DateOnly Day,
    AppointmentSlot Slot,
    AppointmentStatus Status,
    string? ResponseNote);

public interface IAppointmentService
{
    Task<PagedResult<AppointmentDto>> ListAsync(AppointmentQuery query, CancellationToken ct = default);
    Task<AppointmentDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>La pide el cliente desde la app, sobre uno de sus vehículos.</summary>
    Task<AppointmentDto> RequestAsync(RequestAppointmentRequest request, CancellationToken ct = default);

    /// <summary>La anota el taller.</summary>
    Task<AppointmentDto> CreateAsync(CreateAppointmentRequest request, CancellationToken ct = default);

    Task<AppointmentDto> ConfirmAsync(Guid id, ConfirmAppointmentRequest request, CancellationToken ct = default);
    Task<AppointmentDto> RejectAsync(Guid id, RejectAppointmentRequest request, CancellationToken ct = default);

    /// <summary>El vehículo llegó: la cita se cierra y deja de pesar en la agenda.</summary>
    Task<AppointmentDto> MarkAttendedAsync(Guid id, Guid? workOrderId, CancellationToken ct = default);

    // ---------- Sin sesión ----------

    Task<BookingPageDto> BookingPageAsync(Guid bookingToken, CancellationToken ct = default);
    Task<PublicAppointmentDto> BookAsync(Guid bookingToken, BookAppointmentRequest request, CancellationToken ct = default);
    Task<PublicAppointmentDto> GetPublicAsync(Guid token, CancellationToken ct = default);
    Task<PublicAppointmentDto> CancelPublicAsync(Guid token, CancellationToken ct = default);
    Task<TenantLogo?> LogoPublicAsync(Guid bookingToken, CancellationToken ct = default);
}
