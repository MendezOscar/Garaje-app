using Garaj.Application.Abstractions;
using Garaj.Application.Appointments;
using Garaj.Application.Common;
using Garaj.Application.Notifications;
using Garaj.Application.Tenants;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Domain.Rules;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// La agenda del taller y el enlace por el que se pide una cita sin instalar nada.
/// </summary>
public class AppointmentService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ITenantService tenants,
    INotificationPublisher notifications) : IAppointmentService
{
    public async Task<PagedResult<AppointmentDto>> ListAsync(
        AppointmentQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (query.BranchId is { } branchId) q = q.Where(a => a.BranchId == branchId);
        if (query.Status is { } status) q = q.Where(a => a.Status == status);

        // Sin rango, la agenda es de hoy en adelante: lo de ayer ya no se atiende.
        var desde = query.From ?? clock.Today();
        q = q.Where(a => a.Day >= desde);
        if (query.To is { } hasta) q = q.Where(a => a.Day <= hasta);

        if (!query.IncludeClosed)
        {
            q = q.Where(a => a.Status == AppointmentStatus.Requested
                || a.Status == AppointmentStatus.Confirmed);
        }

        var total = await q.CountAsync(ct);

        // Las que esperan respuesta arriba: la agenda es una bandeja antes que un calendario.
        var ordered = q
            .OrderBy(a => a.Status == AppointmentStatus.Requested ? 0 : 1)
            .ThenBy(a => a.Day)
            .ThenBy(a => a.Slot);

        var items = await Project(ordered).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);

        return new PagedResult<AppointmentDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<AppointmentDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        return await Project(Scoped(scope).Where(a => a.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("La cita no existe.");
    }

    public async Task<AppointmentDto> RequestAsync(
        RequestAppointmentRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var vehicle = await db.Vehicles.Include(v => v.Customer)
                          .FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct)
                      ?? throw new NotFoundException("El vehículo no existe.");

        // Sin esto, un cliente podría apartar cita sobre el vehículo de otro pasando un id ajeno.
        if (scope.IsCustomer && vehicle.CustomerId != scope.CustomerId)
            throw new NotFoundException("El vehículo no existe.");

        await EnsureBranchExistsAsync(request.BranchId, ct);
        var dia = DiaValido(request.Day);

        var cita = new Appointment
        {
            BranchId = request.BranchId,
            CustomerId = vehicle.CustomerId,
            VehicleId = vehicle.Id,
            ContactName = vehicle.Customer.FullName,
            ContactPhone = vehicle.Customer.Phone,
            Reason = Texto(request.Reason, "Diga qué necesita para poder apartarle el día."),
            Day = dia,
            Slot = request.Slot,
            Source = AppointmentSource.CustomerApp,
            Status = AppointmentStatus.Requested
        };

        db.Appointments.Add(cita);
        await db.SaveChangesAsync(ct);

        await AvisarAlTallerAsync(cita, ct);

        return await GetAsync(cita.Id, ct);
    }

    public async Task<AppointmentDto> CreateAsync(
        CreateAppointmentRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();
        scope.EnsureBranchAllowed(request.BranchId);

        await EnsureBranchExistsAsync(request.BranchId, ct);
        var dia = DiaValido(request.Day);

        Vehicle? vehicle = null;
        if (request.VehicleId is { } vehicleId)
        {
            vehicle = await db.Vehicles.Include(v => v.Customer)
                          .FirstOrDefaultAsync(v => v.Id == vehicleId, ct)
                      ?? throw new NotFoundException("El vehículo no existe.");
        }

        var cita = new Appointment
        {
            BranchId = request.BranchId,
            CustomerId = vehicle?.CustomerId ?? request.CustomerId,
            VehicleId = vehicle?.Id,
            ContactName = Texto(
                string.IsNullOrWhiteSpace(request.ContactName)
                    ? vehicle?.Customer.FullName ?? string.Empty
                    : request.ContactName,
                "Hace falta a nombre de quién va la cita."),
            ContactPhone = Telefono(
                string.IsNullOrWhiteSpace(request.ContactPhone)
                    ? vehicle?.Customer.Phone ?? string.Empty
                    : request.ContactPhone),
            VehicleNote = vehicle is null ? request.VehicleNote?.Trim() : null,
            Reason = Texto(request.Reason, "Diga qué necesita para poder apartarle el día."),
            Day = dia,
            Slot = request.Slot,
            Source = AppointmentSource.Workshop,
            // La que anota el taller nace confirmada: la está acordando con el cliente al teléfono.
            Status = AppointmentStatus.Confirmed,
            ConfirmedAt = clock.UtcNow
        };

        db.Appointments.Add(cita);
        await db.SaveChangesAsync(ct);

        return await GetAsync(cita.Id, ct);
    }

    public async Task<AppointmentDto> ConfirmAsync(
        Guid id, ConfirmAppointmentRequest request, CancellationToken ct = default)
    {
        var cita = await DelTallerAsync(id, ct);

        if (cita.Status is AppointmentStatus.Attended or AppointmentStatus.Cancelled)
            throw new ConflictException("Esta cita ya se cerró.");

        if (request.Day is { } dia) cita.Day = DiaValido(dia);
        if (request.Slot is { } franja) cita.Slot = franja;

        cita.Status = AppointmentStatus.Confirmed;
        cita.ConfirmedAt = clock.UtcNow;
        cita.ResponseNote = request.Note?.Trim();

        await db.SaveChangesAsync(ct);
        await AvisarAlClienteAsync(cita, "Su cita quedó confirmada",
            $"{Cuando(cita)}. {cita.ResponseNote}".Trim(), ct);

        return await GetAsync(cita.Id, ct);
    }

    public async Task<AppointmentDto> RejectAsync(
        Guid id, RejectAppointmentRequest request, CancellationToken ct = default)
    {
        var cita = await DelTallerAsync(id, ct);

        if (cita.Status == AppointmentStatus.Attended)
            throw new ConflictException("Esa cita ya se atendió.");

        cita.Status = AppointmentStatus.Rejected;
        cita.ResponseNote = Texto(request.Reason, "Diga por qué, para que el cliente lo sepa.");

        await db.SaveChangesAsync(ct);
        await AvisarAlClienteAsync(cita, "No se pudo agendar su cita", cita.ResponseNote!, ct);

        return await GetAsync(cita.Id, ct);
    }

    public async Task<AppointmentDto> MarkAttendedAsync(
        Guid id, Guid? workOrderId, CancellationToken ct = default)
    {
        var cita = await DelTallerAsync(id, ct);

        if (workOrderId is { } orderId
            && !await db.WorkOrders.AnyAsync(o => o.Id == orderId, ct))
            throw new NotFoundException("La orden de trabajo no existe.");

        cita.Status = AppointmentStatus.Attended;
        cita.WorkOrderId = workOrderId;

        await db.SaveChangesAsync(ct);

        return await GetAsync(cita.Id, ct);
    }

    // ---------- Sin sesión ----------

    public async Task<BookingPageDto> BookingPageAsync(Guid bookingToken, CancellationToken ct = default)
    {
        var tenant = await TallerPorTokenAsync(bookingToken, ct);

        var sucursales = await db.Branches.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenant.Id && b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BookingBranchDto(b.Id, b.Name, b.Address, b.Phone))
            .ToListAsync(ct);

        return new BookingPageDto(
            tenant.Name,
            tenant.LogoStorageKey is not null,
            tenant.BookingEnabled,
            sucursales,
            clock.Today().AddDays(AppointmentRules.DiasQueSeAgendan));
    }

    public async Task<PublicAppointmentDto> BookAsync(
        Guid bookingToken, BookAppointmentRequest request, CancellationToken ct = default)
    {
        var tenant = await TallerPorTokenAsync(bookingToken, ct);

        if (!tenant.BookingEnabled)
            throw new ConflictException("Este taller no está agendando citas por ahora.");

        var sucursal = await db.Branches.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == request.BranchId && b.TenantId == tenant.Id && b.IsActive, ct)
            ?? throw new NotFoundException("La sucursal no existe.");

        var telefono = Telefono(request.ContactPhone);
        var dia = DiaValido(request.Day);

        // El enlace es público: sin tope, un aburrido llena la agenda del taller en un minuto.
        var abiertas = await db.Appointments.IgnoreQueryFilters().CountAsync(
            a => a.TenantId == tenant.Id
                 && a.ContactPhone == telefono
                 && (a.Status == AppointmentStatus.Requested || a.Status == AppointmentStatus.Confirmed),
            ct);

        if (abiertas >= AppointmentRules.CitasAbiertasPorTelefono)
        {
            throw new ConflictException(
                "Ya tiene citas pendientes con este taller. Espere a que le contesten o llámelos.");
        }

        var cita = new Appointment
        {
            TenantId = tenant.Id,
            BranchId = sucursal.Id,
            ContactName = Texto(request.ContactName, "Hace falta su nombre."),
            ContactPhone = telefono,
            VehicleNote = request.VehicleNote?.Trim(),
            Reason = Texto(request.Reason, "Diga qué necesita para poder apartarle el día."),
            Day = dia,
            Slot = request.Slot,
            Source = AppointmentSource.PublicLink,
            Status = AppointmentStatus.Requested
        };

        db.Appointments.Add(cita);

        // El filtro global no aplica a esta petición, así que el tenant se fija a mano antes de
        // guardar: lo necesitan el interceptor de auditoría y la propagación.
        tenantContext.SetTenant(tenant.Id);
        await db.SaveChangesAsync(ct);

        await AvisarAlTallerAsync(cita, ct);

        return await PublicAsync(cita.PublicToken, ct);
    }

    public Task<PublicAppointmentDto> GetPublicAsync(Guid token, CancellationToken ct = default) =>
        PublicAsync(token, ct);

    public async Task<PublicAppointmentDto> CancelPublicAsync(Guid token, CancellationToken ct = default)
    {
        var cita = await db.Appointments.IgnoreQueryFilters()
                       .FirstOrDefaultAsync(a => a.PublicToken == token, ct)
                   ?? throw new NotFoundException("La cita no existe o el enlace ya no es válido.");

        if (cita.Status == AppointmentStatus.Attended)
            throw new ConflictException("Esa cita ya se atendió.");

        cita.Status = AppointmentStatus.Cancelled;

        tenantContext.SetTenant(cita.TenantId);
        await db.SaveChangesAsync(ct);

        await notifications.NotifyOwnersAsync(
            cita.TenantId,
            new NotificationDraft(
                NotificationType.AppointmentAnswered,
                "Cita cancelada",
                $"{cita.ContactName} canceló su cita {Cuando(cita)}."),
            ct);

        return await PublicAsync(token, ct);
    }

    public async Task<TenantLogo?> LogoPublicAsync(Guid bookingToken, CancellationToken ct = default)
    {
        var tenant = await TallerPorTokenAsync(bookingToken, ct);
        return await tenants.GetLogoAsync(tenant.Id, ct);
    }

    // ---------- Interno ----------

    private IQueryable<Appointment> Scoped(AccessScope scope)
    {
        var q = db.Appointments.AsNoTracking();

        // El cliente ve las suyas; el técnico no agenda: recibe el vehículo cuando llega.
        if (scope.IsCustomer) return q.Where(a => a.CustomerId == scope.CustomerId);
        if (scope.IsTechnician) return q.Where(_ => false);

        if (scope.BranchFilter() is { } suyas) return q.Where(a => suyas.Contains(a.BranchId));

        return q;
    }

    private IQueryable<AppointmentDto> Project(IQueryable<Appointment> q) =>
        q.Select(a => new AppointmentDto(
            a.Id,
            a.BranchId,
            a.Branch.Name,
            a.CustomerId,
            a.VehicleId,
            a.ContactName,
            a.ContactPhone,
            a.Vehicle != null
                ? a.Vehicle.Brand + " " + a.Vehicle.Model + (a.Vehicle.Plate != null ? " · " + a.Vehicle.Plate : "")
                : a.VehicleNote ?? "No lo dijo",
            a.Reason,
            a.Day,
            a.Slot,
            a.Status,
            a.Source,
            a.ResponseNote,
            a.WorkOrderId,
            a.WorkOrderId != null
                ? db.WorkOrders.Where(o => o.Id == a.WorkOrderId).Select(o => o.Number).FirstOrDefault()
                : null,
            a.PublicToken,
            a.CreatedAt));

    /// <summary>La cita, comprobando que quien la toca manda y que la sucursal es suya.</summary>
    private async Task<Appointment> DelTallerAsync(Guid id, CancellationToken ct)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        var cita = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct)
                   ?? throw new NotFoundException("La cita no existe.");

        scope.EnsureBranchAllowed(cita.BranchId);

        return cita;
    }

    private async Task<Tenant> TallerPorTokenAsync(Guid bookingToken, CancellationToken ct) =>
        await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.BookingToken == bookingToken && t.IsActive, ct)
        ?? throw new NotFoundException("El enlace no es válido.");

    private async Task<PublicAppointmentDto> PublicAsync(Guid token, CancellationToken ct)
    {
        var cita = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
                       .Include(a => a.Branch)
                       .Include(a => a.Vehicle)
                       .FirstOrDefaultAsync(a => a.PublicToken == token, ct)
                   ?? throw new NotFoundException("La cita no existe o el enlace ya no es válido.");

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == cita.TenantId, ct);

        return new PublicAppointmentDto(
            cita.PublicToken,
            tenant.Name,
            cita.Branch.Name,
            cita.Branch.Address,
            cita.Branch.Phone ?? tenant.Phone,
            cita.ContactName,
            cita.Vehicle is not null
                ? $"{cita.Vehicle.Brand} {cita.Vehicle.Model}"
                : cita.VehicleNote ?? "—",
            cita.Reason,
            cita.Day,
            cita.Slot,
            cita.Status,
            cita.ResponseNote);
    }

    private async Task EnsureBranchExistsAsync(Guid branchId, CancellationToken ct)
    {
        if (!await db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, ct))
            throw new NotFoundException("La sucursal no existe.");
    }

    /// <summary>
    /// Ni ayer ni el año que viene. El día se compara contra el de Honduras, no contra el del
    /// servidor: en UTC, «hoy» cambia a las seis de la tarde de acá y la cita de hoy ya no
    /// se podría pedir desde el mediodía.
    /// </summary>
    private DateOnly DiaValido(DateOnly dia)
    {
        var hoy = clock.Today();

        if (dia < hoy) throw new AppException("Ese día ya pasó.");

        if (!AppointmentRules.EsAgendable(dia, hoy))
        {
            throw new AppException(
                $"Solo se agenda hasta {AppointmentRules.DiasQueSeAgendan} días adelante.");
        }

        return dia;
    }

    private static string Texto(string? valor, string siFalta) =>
        string.IsNullOrWhiteSpace(valor) ? throw new AppException(siFalta) : valor.Trim();

    private static string Telefono(string? valor) =>
        AppointmentRules.Telefono(valor)
        ?? throw new AppException("Hace falta un teléfono para poder contestarle.");

    private static string Cuando(Appointment cita) =>
        $"el {cita.Day:dd/MM/yyyy} por la {(cita.Slot == AppointmentSlot.Morning ? "mañana" : "tarde")}";

    private Task AvisarAlTallerAsync(Appointment cita, CancellationToken ct) =>
        notifications.NotifyOwnersAsync(
            cita.TenantId,
            new NotificationDraft(
                NotificationType.AppointmentRequested,
                "Cita por confirmar",
                $"{cita.ContactName} pide cita {Cuando(cita)}: {cita.Reason}"),
            ct);

    private Task AvisarAlClienteAsync(Appointment cita, string titulo, string cuerpo, CancellationToken ct) =>
        cita.CustomerId is { } customerId
            ? notifications.NotifyCustomerAsync(
                cita.TenantId,
                customerId,
                new NotificationDraft(NotificationType.AppointmentAnswered, titulo, cuerpo),
                ct)
            // Quien pidió por el enlace no tiene a dónde recibir un aviso: se le escribe por
            // WhatsApp desde la pantalla de citas, con un toque.
            : Task.CompletedTask;
}
