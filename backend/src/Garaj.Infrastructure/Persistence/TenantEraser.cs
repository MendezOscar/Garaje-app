using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Identity;
using Garaj.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Garaj.Infrastructure.Persistence;

/// <summary>
/// Borra un taller entero: sus datos, sus usuarios y sus archivos del bucket.
///
/// No hay endpoint para esto y no lo va a haber. Suspender un taller —que es lo que ofrece el
/// panel de plataforma— deja los datos quietos y se puede deshacer; esto no se deshace, así
/// que vive donde vive el alta: en un comando que se corre a mano, desde la máquina de quien
/// administra, con la cadena de la base en user secrets.
///
/// Sirve para lo que se creó para probar y ya no se usa: el taller de la prueba cerrada de
/// Play, un ambiente de demostración viejo. Para un taller con clientes de verdad, suspender.
/// </summary>
public class TenantEraser(
    GarajDbContext db,
    UserManager<AppUser> userManager,
    ITenantContext tenantContext,
    IServiceProvider servicios)
{
    /// <summary>
    /// El bucket, si está configurado. Quien corre el comando casi siempre tiene la cadena de
    /// la base en user secrets pero no las llaves del almacenamiento, que viven en Render: sin
    /// ellas el borrado sigue adelante y lo dice, en vez de no poder correr.
    /// </summary>
    private IStorageService? Almacenamiento
    {
        get
        {
            try
            {
                return servicios.GetService<IStorageService>();
            }
            catch (AppException)
            {
                return null;
            }
        }
    }

    /// <summary>Si los archivos se quedaron en el bucket, para avisarlo al terminar.</summary>
    public bool ArchivosSinBorrar { get; private set; }

    /// <summary>
    /// A qué bucket apunta esta corrida. Se imprime antes de confirmar porque es la trampa de
    /// este comando: la cadena de la base puede venir de user secrets —producción— mientras el
    /// almacenamiento se quedó con el MinIO de desarrollo, y entonces las fotos de verdad no
    /// se borran y nadie se entera.
    /// </summary>
    public string Almacen =>
        servicios.GetService<IOptions<StorageOptions>>() is { Value: { ServiceUrl.Length: > 0 } o }
            ? $"{o.ServiceUrl} · {o.Bucket}"
            : "sin configurar";

    /// <summary>Lo que hay dentro del taller. Se imprime antes de borrar, para mirarlo.</summary>
    public record Inventario(
        Guid TenantId,
        string Name,
        int Sucursales,
        int Usuarios,
        int Clientes,
        int Ordenes,
        int Ventas,
        int Archivos);

    public async Task<Inventario> ContarAsync(Guid tenantId, CancellationToken ct = default)
    {
        tenantContext.BypassTenantFilter = true;

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException("El taller no existe.");

        return new Inventario(
            tenant.Id,
            tenant.Name,
            await db.Branches.CountAsync(x => x.TenantId == tenantId, ct),
            await db.Users.CountAsync(x => x.TenantId == tenantId, ct),
            await db.Customers.CountAsync(x => x.TenantId == tenantId, ct),
            await db.WorkOrders.CountAsync(x => x.TenantId == tenantId, ct),
            await db.Sales.CountAsync(x => x.TenantId == tenantId, ct),
            await db.MediaAttachments.CountAsync(x => x.TenantId == tenantId, ct));
    }

    /// <summary>Busca por nombre exacto. Es lo que uno tiene a mano: el nombre, no el id.</summary>
    public async Task<Guid> BuscarPorNombreAsync(string nombre, CancellationToken ct = default)
    {
        tenantContext.BypassTenantFilter = true;

        var encontrados = await db.Tenants.AsNoTracking()
            .Where(t => t.Name == nombre)
            .Select(t => t.Id)
            .ToListAsync(ct);

        return encontrados.Count switch
        {
            1 => encontrados[0],
            0 => throw new NotFoundException($"No hay ningún taller llamado «{nombre}»."),
            _ => throw new ConflictException(
                $"Hay {encontrados.Count} talleres llamados «{nombre}». Bórrelo por --id."),
        };
    }

    public async Task<Inventario> RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        tenantContext.BypassTenantFilter = true;

        var inventario = await ContarAsync(tenantId, ct);

        // Primero los archivos: si falla la base, lo peor que queda es un bucket con objetos
        // de más, que no le hace daño a nadie. Al revés —filas borradas y archivos huérfanos
        // sin quién los nombre— no hay forma de limpiarlo después.
        await BorrarArchivosAsync(tenantId, ct);

        // Npgsql reintenta las operaciones que fallan por un corte de red, y una transacción
        // abierta a mano no se puede reintentar sola: hay que entregarle el bloque entero.
        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () => await BorrarTodoAsync(tenantId, ct));

        return inventario;
    }

    private async Task BorrarTodoAsync(Guid tenantId, CancellationToken ct)
    {
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        // Los enlaces cruzados primero: la orden apunta a la venta y la venta a la orden, así
        // que sin romper el círculo no hay orden de borrado que sirva.
        await db.WorkOrders.Where(x => x.TenantId == tenantId).ExecuteUpdateAsync(
            s => s.SetProperty(w => w.SaleId, (Guid?)null)
                  .SetProperty(w => w.ClaimId, (Guid?)null)
                  .SetProperty(w => w.ServiceRequestId, (Guid?)null), ct);

        await db.ServiceRequests.Where(x => x.TenantId == tenantId).ExecuteUpdateAsync(
            s => s.SetProperty(r => r.WorkOrderId, (Guid?)null), ct);

        await db.Appointments.Where(x => x.TenantId == tenantId).ExecuteUpdateAsync(
            s => s.SetProperty(a => a.WorkOrderId, (Guid?)null), ct);

        await db.Claims.Where(x => x.TenantId == tenantId).ExecuteUpdateAsync(
            s => s.SetProperty(c => c.RepairWorkOrderId, (Guid?)null), ct);

        // De las hojas al tronco. El orden importa: cada llave foránea es Restrict, que es lo
        // que impide que un descuido borre media base.
        await db.SalePayments.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.SaleLines.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.QuoteLines.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Quotes.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Claims.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Sales.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.MediaAttachments.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.VehicleReceptions.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.WorkOrderParts.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.WorkOrderTasks.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.WorkOrderStatusHistory.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.WorkOrders.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.ServiceRequests.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Appointments.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.StockMovements.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.StockItems.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Parts.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.JobTemplateTasks.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.JobTemplateParts.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.JobTemplates.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.LaborServices.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.Expenses.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.DeviceTokens.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.SubscriptionPayments.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.FiscalRanges.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.Equipment.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.EquipmentTypes.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Vehicles.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Customers.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        // Los usuarios van por el UserManager y no por SQL: él se lleva también las filas de
        // roles y de claims, que viven en tablas de Identity que no están en este contexto.
        var usuarios = await db.Users.Where(u => u.TenantId == tenantId).ToListAsync(ct);
        var ids = usuarios.Select(u => u.Id).ToList();

        await db.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync(ct);
        await db.UserBranches.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

        foreach (var usuario in usuarios)
        {
            var resultado = await userManager.DeleteAsync(usuario);
            if (!resultado.Succeeded)
                throw new AppException(
                    $"No se pudo borrar el usuario {usuario.Email}: "
                    + string.Join(" ", resultado.Errors.Select(e => e.Description)));
        }

        await db.Branches.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await db.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync(ct);

        await transaccion.CommitAsync(ct);
    }

    /// <summary>
    /// Las fotos de las órdenes, las firmas de recepción y el logo. Se borra uno por uno y sin
    /// detenerse en el que falle: un objeto que ya no está no es razón para dejar el taller a
    /// medio borrar.
    /// </summary>
    private async Task BorrarArchivosAsync(Guid tenantId, CancellationToken ct)
    {
        // Las dos columnas por separado y se aplanan aquí: un arreglo dentro del Select no
        // lo traduce EF.
        var fotos = await db.MediaAttachments.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Select(m => new { m.StorageKey, m.ThumbnailKey })
            .ToListAsync(ct);

        var claves = fotos.SelectMany(f => new[] { f.StorageKey, f.ThumbnailKey }).ToList();

        claves.AddRange(await db.VehicleReceptions.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.SignatureStorageKey != null)
            .Select(r => r.SignatureStorageKey)
            .ToListAsync(ct));

        claves.Add(await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.LogoStorageKey)
            .FirstOrDefaultAsync(ct));

        var storage = Almacenamiento;
        if (storage is null)
        {
            ArchivosSinBorrar = claves.Any(c => !string.IsNullOrWhiteSpace(c));
            return;
        }

        foreach (var clave in claves.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
        {
            try
            {
                await storage.DeleteAsync(clave!, ct);
            }
            catch
            {
                // Sigue: lo que importa es que no quede nada en la base.
            }
        }
    }
}
