using Garaj.Application.Abstractions;
using Garaj.Application.Media;
using Garaj.Application.Tenants;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// Arma la ficha de recepción de una orden. Vive aparte de los servicios porque la piden dos:
/// la orden, que la manda suelta, y la cotización, que la cose delante del presupuesto.
/// </summary>
public class ReceptionDocuments(
    GarajDbContext db,
    ITenantService tenants,
    IMediaService media,
    IStorageService storage)
{
    /// <summary>Cuántas fotos de la orden entran en la ficha.</summary>
    private const int FotosEnLaFicha = 6;

    /// <summary>El documento de la ficha, o null si la orden no tiene recepción llenada.</summary>
    public async Task<IDocument?> TryBuildAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var datos = await db.VehicleReceptions.AsNoTracking()
            .Where(r => r.WorkOrderId == workOrderId)
            .Select(r => new
            {
                Ficha = new DatosDeRecepcion(
                    r.WorkOrder.Number,
                    r.WorkOrder.Branch.Name,
                    r.WorkOrder.Vehicle.Brand + " " + r.WorkOrder.Vehicle.Model,
                    r.WorkOrder.Vehicle.Plate,
                    r.WorkOrder.Vehicle.Customer.FullName,
                    r.WorkOrder.Vehicle.Customer.Phone,
                    r.WorkOrder.MileageIn,
                    r.WorkOrder.OpenedAt,
                    r.WorkOrder.Description,
                    r.FuelLevel,
                    r.Damages,
                    r.Belongings,
                    r.Notes,
                    r.DeliveredByName,
                    null),
                r.SignatureStorageKey,
                r.ReceivedByUserId,
                r.WorkOrder.TenantId
            })
            .FirstOrDefaultAsync(ct);

        if (datos is null) return null;

        var recibio = datos.ReceivedByUserId is { } userId
            ? await db.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            : null;

        var tenant = await db.Tenants.AsNoTracking()
            .FirstAsync(t => t.Id == datos.TenantId, ct);

        var logo = await tenants.TryGetLogoBytesAsync(tenant.Id, ct);
        var firma = await FirmaAsync(datos.SignatureStorageKey, ct);
        var fotos = await media.DownloadThumbnailsAsync(
            MediaOwnerType.WorkOrder, workOrderId, tenant.Id, FotosEnLaFicha, ct);

        return ReceptionPdf.Build(
            datos.Ficha with { ReceivedByName = recibio },
            tenant.Name, tenant.LegalName, tenant.Phone, tenant.TaxId, logo, firma, fotos);
    }

    private async Task<byte[]?> FirmaAsync(string? key, CancellationToken ct)
    {
        if (key is null) return null;

        using var stream = await storage.DownloadAsync(key, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
