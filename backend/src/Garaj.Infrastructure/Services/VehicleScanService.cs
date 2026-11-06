using System.Text.RegularExpressions;
using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Tenants;
using Garaj.Application.VehicleScans;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Las lecturas del adaptador OBD2. Guarda los códigos uno por uno —para poder buscarlos
/// después— y arma el informe en PDF que se le enseña al cliente.
/// </summary>
public partial class VehicleScanService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IStorageService storage,
    ITenantService tenants,
    ILogger<VehicleScanService> logger) : IVehicleScanService
{
    /// <summary>
    /// Tope de códigos por lectura. Un carro con problemas de verdad reporta ocho o diez; de
    /// cincuenta para arriba lo que hay es un adaptador barato devolviendo basura, y eso no
    /// puede llenar la base ni el PDF.
    /// </summary>
    private const int MaxCodes = 50;

    /// <summary>
    /// La forma que manda la norma SAE J2012: letra del sistema, un dígito del 0 al 3 y tres
    /// hexadecimales. Lo que no encaje aquí no es un código y no entra.
    /// </summary>
    [GeneratedRegex("^[PCBU][0-3][0-9A-F]{3}$")]
    private static partial Regex CodigoValido();

    public async Task<VehicleScanDto> CreateAsync(
        CreateVehicleScanRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        // El cliente no escanea su propio carro: la lectura es el dictamen del taller.
        if (scope.IsCustomer)
            throw new ForbiddenException("Solo el taller puede registrar una lectura del vehículo.");

        if (!await ScopedOrders(scope).AnyAsync(w => w.Id == request.WorkOrderId, ct))
            throw new NotFoundException("La orden de trabajo no existe.");

        // Se vuelve a traer con seguimiento —el alcance se comprueba sin él— porque la lectura
        // puede completarle el VIN al vehículo, y para eso EF tiene que estar mirando la fila.
        var order = await db.WorkOrders
            .Include(w => w.Vehicle)
            .FirstAsync(w => w.Id == request.WorkOrderId, ct);

        var codes = Normalizar(request.Codes);

        var scan = new VehicleScan
        {
            WorkOrderId = order.Id,
            VehicleId = order.VehicleId,
            // La hora la pone el teléfono: la lectura puede subirse más tarde, cuando vuelva
            // la señal, y lo que importa es cuándo se leyó el carro.
            ScannedAt = request.ScannedAt == default ? clock.UtcNow : request.ScannedAt,
            ScannedByUserId = scope.UserId,
            Vin = Recortar(request.Vin?.ToUpperInvariant(), 20),
            Mileage = request.Mileage is > 0 ? request.Mileage : null,
            DeviceName = Recortar(request.DeviceName, 100),
            Protocol = Recortar(request.Protocol, 60),
            Notes = Recortar(request.Notes, 1000),
            Codes = codes
        };

        db.VehicleScans.Add(scan);

        // El VIN que dicta la computadora del carro vale más que el que alguien copió a mano,
        // pero no se pisa el que ya está: corregir la ficha es decisión de quien la lleva.
        if (order.Vehicle is { } vehicle
            && string.IsNullOrWhiteSpace(vehicle.Vin)
            && scan.Vin is { Length: 17 })
        {
            vehicle.Vin = scan.Vin;
        }

        await db.SaveChangesAsync(ct);

        var dto = await MapAsync([scan], ct);
        var scanDto = dto.Single();

        var attachmentId = await TryBuildReportAsync(scan, order, scanDto, ct);

        if (attachmentId is not null)
        {
            scan.MediaAttachmentId = attachmentId;
            await db.SaveChangesAsync(ct);
            scanDto = scanDto with { AttachmentId = attachmentId };
        }

        return scanDto;
    }

    public async Task<IReadOnlyList<VehicleScanDto>> ListForWorkOrderAsync(
        Guid workOrderId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (!await ScopedOrders(scope).AnyAsync(w => w.Id == workOrderId, ct))
            throw new NotFoundException("La orden de trabajo no existe.");

        var scans = await db.VehicleScans.AsNoTracking()
            .Include(s => s.Codes)
            .Where(s => s.WorkOrderId == workOrderId)
            .OrderByDescending(s => s.ScannedAt)
            .ToListAsync(ct);

        return await MapAsync(scans, ct);
    }

    public async Task<IReadOnlyList<VehicleScanDto>> ListForVehicleAsync(
        Guid vehicleId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        // El historial del vehículo se arma con las lecturas de las órdenes que el usuario
        // puede ver: un técnico no ve por esta puerta lo que no ve por la otra.
        var orderIds = ScopedOrders(scope).Select(w => w.Id);

        var scans = await db.VehicleScans.AsNoTracking()
            .Include(s => s.Codes)
            .Where(s => s.VehicleId == vehicleId && orderIds.Contains(s.WorkOrderId))
            .OrderByDescending(s => s.ScannedAt)
            .ToListAsync(ct);

        return await MapAsync(scans, ct);
    }

    public async Task<IReadOnlyList<VehicleScanHitDto>> SearchByCodeAsync(
        string code, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var buscado = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (buscado.Length < 3)
            throw new AppException("Escriba al menos tres caracteres del código, por ejemplo P03.");

        var orderIds = ScopedOrders(scope).Select(w => w.Id);

        return await db.VehicleScanCodes.AsNoTracking()
            .Where(c => c.Code.StartsWith(buscado) && orderIds.Contains(c.Scan.WorkOrderId))
            .OrderByDescending(c => c.Scan.ScannedAt)
            .Take(200)
            .Select(c => new VehicleScanHitDto(
                c.VehicleScanId,
                c.Scan.WorkOrderId,
                c.Scan.WorkOrder.Number,
                c.Scan.ScannedAt,
                c.Code,
                c.Status,
                c.Description,
                c.Scan.VehicleId,
                c.Scan.WorkOrder.Vehicle != null ? c.Scan.WorkOrder.Vehicle.Plate : null,
                c.Scan.WorkOrder.Vehicle != null
                    ? c.Scan.WorkOrder.Vehicle.Brand + " " + c.Scan.WorkOrder.Vehicle.Model
                    : "—",
                c.Scan.WorkOrder.Vehicle != null
                    ? c.Scan.WorkOrder.Vehicle.Customer.FullName
                    : "—"))
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        // Borrar una lectura es borrar evidencia: solo quien manda, y nunca el cliente.
        scope.EnsureManagement();

        var scan = await db.VehicleScans.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("La lectura no existe.");

        if (!await ScopedOrders(scope).AnyAsync(w => w.Id == scan.WorkOrderId, ct))
            throw new NotFoundException("La lectura no existe.");

        // El informe adjunto se queda: puede haber salido ya en una cotización que el cliente
        // abrió, y lo que él vio no puede desaparecer. Se quita desde la galería si se quiere.
        db.VehicleScans.Remove(scan);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Arma el PDF de la lectura y lo deja colgado de la orden como un adjunto más, para que
    /// viaje solo a la cotización por el camino que ya existe.
    /// </summary>
    /// <remarks>
    /// Un fallo aquí no tumba la lectura: los códigos ya están guardados, que es lo que no se
    /// puede volver a conseguir —el carro ya no está en la rampa—. El informe se rehace.
    /// </remarks>
    private async Task<Guid?> TryBuildReportAsync(
        VehicleScan scan, WorkOrder order, VehicleScanDto dto, CancellationToken ct)
    {
        try
        {
            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct);

            if (tenant is null) return null;

            var vehicle = order.Vehicle;
            var customerName = vehicle is null
                ? "—"
                : await db.Customers.AsNoTracking()
                    .Where(c => c.Id == vehicle.CustomerId)
                    .Select(c => c.FullName)
                    .FirstOrDefaultAsync(ct) ?? "—";

            var label = vehicle is null
                ? "Vehículo"
                : $"{vehicle.Brand} {vehicle.Model}{(vehicle.Year is { } y ? $" {y}" : "")}";

            var logo = await tenants.TryGetLogoBytesAsync(tenant.Id, ct);

            var bytes = VehicleScanPdf.Render(
                dto, tenant.Name, tenant.LegalName, tenant.Phone, tenant.TaxId,
                order.Number, label, vehicle?.Plate, customerName, logo);

            var key = $"tenants/{tenantContext.TenantId}/work-orders/{order.Id}/{Guid.NewGuid():N}.pdf";

            await using (var stream = new MemoryStream(bytes))
            {
                await storage.UploadAsync(key, stream, MediaService.Pdf, ct);
            }

            var attachment = new MediaAttachment
            {
                OwnerType = MediaOwnerType.WorkOrder,
                OwnerId = order.Id,
                StorageKey = key,
                ContentType = MediaService.Pdf,
                SizeBytes = bytes.Length,
                OriginalFileName =
                    $"Diagnóstico {order.Number} {scan.ScannedAt.ToLocalTime():dd-MM-yyyy}.pdf",
                UploadedByUserId = scan.ScannedByUserId,
                TakenAt = scan.ScannedAt,
                // Nace confirmado: el binario no viene de una subida del cliente, lo acaba de
                // escribir el servidor y ya sabe que está en el bucket.
                IsConfirmed = true,
                IsVisibleToCustomer = true
            };

            db.MediaAttachments.Add(attachment);
            await db.SaveChangesAsync(ct);

            return attachment.Id;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "No se pudo armar el informe de la lectura {ScanId}.", scan.Id);
            return null;
        }
    }

    /// <summary>
    /// Deja los códigos como se van a guardar: en mayúsculas, sin los que no tienen forma de
    /// código, sin repetidos y con tope.
    /// </summary>
    /// <remarks>
    /// Los adaptadores devuelven el mismo código dos veces cuando la falla la reportan dos
    /// módulos del vehículo, y los clones baratos cuelan restos de la línea serie entre las
    /// respuestas. Limpiarlo aquí es lo que evita que el PDF salga con basura delante del
    /// cliente.
    /// </remarks>
    private static List<VehicleScanCode> Normalizar(IReadOnlyList<ScanCodeInput>? codes)
    {
        if (codes is null || codes.Count == 0) return [];

        var vistos = new HashSet<(string, DtcStatus)>();
        var result = new List<VehicleScanCode>();

        foreach (var input in codes)
        {
            var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();

            if (!CodigoValido().IsMatch(code)) continue;
            if (!vistos.Add((code, input.Status))) continue;

            result.Add(new VehicleScanCode
            {
                Code = code,
                Status = input.Status,
                Description = Recortar(input.Description, 300),
                // Genérico es el segundo carácter 0 —los mismos en cualquier marca—; del 1 al
                // 3 el código es del fabricante y su significado lo da el escáner de la marca.
                IsGeneric = code[1] == '0'
            });

            if (result.Count == MaxCodes) break;
        }

        return result;
    }

    private async Task<IReadOnlyList<VehicleScanDto>> MapAsync(
        IReadOnlyList<VehicleScan> scans, CancellationToken ct)
    {
        if (scans.Count == 0) return [];

        var userIds = scans.Select(s => s.ScannedByUserId).Distinct().ToList();
        var names = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return scans.Select(s => new VehicleScanDto(
            s.Id,
            s.WorkOrderId,
            s.ScannedAt,
            names.GetValueOrDefault(s.ScannedByUserId, "—"),
            s.Codes
                .OrderBy(c => c.Status)
                .ThenBy(c => c.Code)
                .Select(c => new VehicleScanCodeDto(c.Code, c.Status, c.Description, c.IsGeneric))
                .ToList(),
            s.Vin,
            s.Mileage,
            s.DeviceName,
            s.Protocol,
            s.Notes,
            s.MediaAttachmentId)).ToList();
    }

    /// <summary>
    /// El mismo alcance que las fotos y que la propia orden: el técnico ve las suyas, el
    /// cliente las de sus vehículos. Si no puede ver la orden, no puede ver su diagnóstico.
    /// </summary>
    private IQueryable<WorkOrder> ScopedOrders(AccessScope scope)
    {
        var q = db.WorkOrders.AsNoTracking();

        if (scope.IsTechnician) return q.Where(w => w.AssignedTechnicianId == scope.UserId);
        if (scope.IsCustomer)
            return q.Where(w =>
                (w.Vehicle != null && w.Vehicle.CustomerId == scope.CustomerId)
                || (w.Equipment != null && w.Equipment.CustomerId == scope.CustomerId));

        return q;
    }

    private static string? Recortar(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}
