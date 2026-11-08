using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assembly;

/// <summary>
/// Registrar una moto armada, y lo que cada técnico lleva armado.
/// </summary>
/// <remarks>
/// Esto es lo que reemplaza la foto al grupo de WhatsApp y el Excel de la noche. Tres cosas
/// que no son evidentes y que son el centro de todo:
///
/// La serie es la llave. Repetida significa una moto cobrada dos veces; la restricción está
/// en la base, y aquí solo se traduce el choque a una frase que se entienda en una bodega.
///
/// Los dos importes se <b>copian</b> de la tarifa al registrar. Lo que se facturó en enero no
/// cambia porque en marzo se renegocie el contrato.
///
/// Y lo que se le cobra al contratista <b>no sale del servidor</b> cuando pregunta un técnico.
/// No se esconde en la pantalla: no viaja.
/// </remarks>
public class MotorcycleAssemblyService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IMotorcycleAssemblyService
{
    /// <summary>
    /// Cuántos caracteres del chasis identifican al modelo: el fabricante y la descripción
    /// del vehículo, que es lo que el estándar pone al principio. Lo que sigue es el año, la
    /// planta y el correlativo, que cambian de una moto a otra.
    /// </summary>
    private const int LargoDelPrefijo = 8;

    public async Task<AssemblyPickListsDto> PickListsAsync(CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureDelTaller();

        // Sin un solo precio: estas listas las lee el técnico.
        var contratistas = await db.AssemblyContractors.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new AssemblyNameDto(c.Id, c.Name))
            .ToListAsync(ct);

        var modelos = await db.AssemblyModels.AsNoTracking()
            .Where(m => m.IsActive).OrderBy(m => m.Name)
            .Select(m => new AssemblyNameDto(m.Id, m.Name))
            .ToListAsync(ct);

        var bodegas = await db.AssemblyWarehouses.AsNoTracking()
            .Where(w => w.IsActive).OrderBy(w => w.Name)
            .Select(w => new AssemblyNameDto(w.Id, w.Name))
            .ToListAsync(ct);

        // A quién apuntarle la moto. Al técnico le va vacía: solo registra lo suyo, y en su
        // pantalla no aparece ni la pregunta. El dueño la necesita porque sigue recibiendo
        // fotos por WhatsApp y registra él lo que armó otro.
        // UsersInTenant y no db.Users: AppUser no lleva filtro de taller, así que preguntar
        // por db.Users aquí listaría empleados de otro taller.
        var tecnicos = scope.IsManagement
            ? await (
                from u in db.UsersInTenant.AsNoTracking()
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where u.IsActive && (r.Name == AppRoles.Technician || u.Id == scope.UserId)
                orderby u.FullName
                select new AssemblyNameDto(u.Id, u.FullName)).ToListAsync(ct)
            : [];

        return new AssemblyPickListsDto(contratistas, modelos, bodegas, tecnicos);
    }

    public async Task<MotorcycleAssemblyDto> RegisterAsync(
        RegisterMotorcycleRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureDelTaller();

        var serie = VinFormatter.Normalize(request.Serial);

        if (serie.Length < 6)
            throw new AppException("La serie está muy corta. Cópiela completa del chasis.");

        var sucursal = await ResolverSucursalAsync(scope, request, ct);

        var tarifa = await db.AssemblyRates.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.ContractorId == request.ContractorId
                    && r.ModelId == request.ModelId
                    && r.IsActive,
                ct);

        // Sin tarifa no se registra. Guardarla en cero haría que esa moto se armara gratis y
        // nadie lo notaría hasta cuadrar el mes.
        if (tarifa is null)
            throw new AppException(
                "Ese modelo no tiene precio con ese contratista. Pídale al dueño que lo ponga.");

        var bodegaExiste = await db.AssemblyWarehouses
            .AnyAsync(w => w.Id == request.WarehouseId, ct);

        if (!bodegaExiste) throw new NotFoundException("La bodega no existe.");

        // El día lo pone el servidor con el calendario de Honduras, no el teléfono: una moto
        // registrada a las siete de la noche del 31 sigue siendo de ese mes.
        var dia = request.AssembledOn ?? clock.Today();

        if (dia > clock.Today())
            throw new AppException("No se puede registrar una moto con fecha de mañana.");

        var repetida = await db.MotorcycleAssemblies.AsNoTracking()
            .AnyAsync(m => m.Serial == serie, ct);

        if (repetida)
            throw new ConflictException(
                $"La serie {serie} ya está registrada. Revise que sea la moto correcta.");

        var moto = new MotorcycleAssembly
        {
            BranchId = sucursal,
            ContractorId = request.ContractorId,
            ModelId = request.ModelId,
            WarehouseId = request.WarehouseId,
            TechnicianUserId = await ResolverTecnicoAsync(scope, request.TechnicianUserId, ct),
            AssembledOn = dia,
            Serial = serie,
            Charge = tarifa.Charge,
            TechnicianPay = tarifa.TechnicianPay,
            Notes = request.Notes?.Trim()
        };

        db.MotorcycleAssemblies.Add(moto);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos técnicos registrando la misma serie a la vez desde dos bodegas: el índice
            // único de la base es el que de verdad lo impide, y aquí se traduce a castellano.
            // El filtro no puede ir en el `when` porque no admite await.
            if (await YaExisteAsync(serie, moto.Id, ct))
            {
                throw new ConflictException(
                    $"La serie {serie} ya está registrada. Revise que sea la moto correcta.");
            }

            throw;
        }

        return await UnaAsync(moto.Id, scope, ct);
    }

    public async Task<MyAssembliesDto> MineAsync(
        DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureDelTaller();

        var hoy = clock.Today();

        // Por omisión, el mes corriente: es el periodo con el que se le paga.
        var desde = from ?? new DateOnly(hoy.Year, hoy.Month, 1);
        var hasta = to ?? hoy;

        var q = db.MotorcycleAssemblies.AsNoTracking()
            .Where(m => m.TechnicianUserId == scope.UserId
                && m.AssembledOn >= desde
                && m.AssembledOn <= hasta);

        var items = await Proyectar(q.OrderByDescending(m => m.AssembledOn)
            .ThenByDescending(m => m.CreatedAt), scope, ct);

        return new MyAssembliesDto(
            desde, hasta, items.Count, items.Sum(i => i.TechnicianPay), items);
    }

    public async Task<ModelGuessDto> GuessModelAsync(string serial, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        EnsureDelTaller();

        var serie = VinFormatter.Normalize(serial);

        if (serie.Length < LargoDelPrefijo) return new ModelGuessDto(null, null, 0);

        var prefijo = serie[..LargoDelPrefijo];

        // Primero, lo que el dueño dejó escrito en el catálogo. Así la propuesta funciona
        // desde la primera moto de un modelo, sin esperar a que haya historia que aprender.
        var delCatalogo = await db.AssemblyModels.AsNoTracking()
            .Where(m => m.IsActive && m.VinPrefix != null && m.VinPrefix != ""
                && serie.StartsWith(m.VinPrefix))
            .OrderByDescending(m => m.VinPrefix!.Length)
            .Select(m => new { m.Id, m.Name })
            .FirstOrDefaultAsync(ct);

        if (delCatalogo is not null)
            return new ModelGuessDto(delCatalogo.Id, delCatalogo.Name, 0);

        // Y si no, lo aprendido: el modelo con el que más veces se registró ese prefijo. Más
        // veces y no la última: un error de tecleo aislado no debe arrastrar a las siguientes.
        var conteo = await db.MotorcycleAssemblies.AsNoTracking()
            .Where(m => m.Serial.StartsWith(prefijo))
            .GroupBy(m => m.ModelId)
            .Select(g => new { ModelId = g.Key, Veces = g.Count() })
            .OrderByDescending(x => x.Veces)
            .FirstOrDefaultAsync(ct);

        if (conteo is null) return new ModelGuessDto(null, null, 0);

        var nombre = await db.AssemblyModels.AsNoTracking()
            .Where(m => m.Id == conteo.ModelId && m.IsActive)
            .Select(m => m.Name)
            .FirstOrDefaultAsync(ct);

        return nombre is null
            ? new ModelGuessDto(null, null, 0)
            : new ModelGuessDto(conteo.ModelId, nombre, conteo.Veces);
    }

    private async Task<MotorcycleAssemblyDto> UnaAsync(
        Guid id, AccessScope scope, CancellationToken ct)
    {
        var items = await Proyectar(
            db.MotorcycleAssemblies.AsNoTracking().Where(m => m.Id == id), scope, ct);

        return items.FirstOrDefault() ?? throw new NotFoundException("La moto no existe.");
    }

    /// <summary>
    /// La proyección, con el cobro fuera cuando pregunta un técnico. Está en un solo sitio
    /// para que no haya una segunda consulta que se olvide de quitarlo.
    /// </summary>
    private async Task<List<MotorcycleAssemblyDto>> Proyectar(
        IQueryable<MotorcycleAssembly> q, AccessScope scope, CancellationToken ct)
    {
        var veElCobro = scope.IsManagement;

        var filas = await q
            .Select(m => new
            {
                m.Id,
                m.AssembledOn,
                m.Serial,
                m.ModelId,
                ModelName = m.Model.Name,
                m.ContractorId,
                ContractorName = m.Contractor.Name,
                m.WarehouseId,
                WarehouseName = m.Warehouse.Name,
                m.TechnicianUserId,
                m.TechnicianPay,
                m.Charge,
                m.Notes
            })
            .ToListAsync(ct);

        var ids = filas.Select(f => f.TechnicianUserId).Distinct().ToList();

        var nombres = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return filas.Select(f => new MotorcycleAssemblyDto(
            f.Id, f.AssembledOn, f.Serial, f.ModelId, f.ModelName,
            f.ContractorId, f.ContractorName, f.WarehouseId, f.WarehouseName,
            f.TechnicianUserId, nombres.GetValueOrDefault(f.TechnicianUserId, "—"),
            f.TechnicianPay, f.Notes,
            veElCobro ? f.Charge : null)).ToList();
    }

    private async Task<bool> YaExisteAsync(string serie, Guid propia, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return await db.MotorcycleAssemblies.AsNoTracking()
            .AnyAsync(m => m.Serial == serie && m.Id != propia, ct);
    }

    /// <summary>
    /// A quién se le apunta la moto.
    /// </summary>
    /// <remarks>
    /// Un técnico solo registra lo suyo: lo que venga en la petición se ignora. Quien manda
    /// puede apuntársela a otro, que es el caso del dueño cargando la que le reportaron por
    /// teléfono, y entonces tiene que ser alguien de su taller.
    /// </remarks>
    private async Task<Guid> ResolverTecnicoAsync(
        AccessScope scope, Guid? pedido, CancellationToken ct)
    {
        if (!scope.IsManagement || pedido is not { } id || id == scope.UserId)
            return scope.UserId;

        // UsersInTenant: db.Users no lleva filtro de taller, y por ahí se le podía apuntar
        // una moto —y su pago— a un empleado de otro taller.
        var existe = await db.UsersInTenant.AsNoTracking()
            .AnyAsync(u => u.Id == id && u.IsActive, ct);

        if (!existe) throw new NotFoundException("Ese usuario no existe en el taller.");

        return id;
    }

    /// <summary>
    /// En qué sucursal del taller queda la moto.
    /// </summary>
    /// <remarks>
    /// <b>No se le pregunta a nadie.</b> El armado es un contrato y el contrato lo lleva una
    /// sucursal, así que sale del contratista. Antes se le pedía a quien registraba, y al
    /// técnico con dos sucursales le quedaba un callejón sin salida: está parado en la bodega
    /// del contratista —que no es una sucursal del taller— y no tiene cómo contestar.
    ///
    /// Si el contrato todavía no la tiene puesta se cae a lo que mande quien registra —el
    /// panel manda la suya—, de ahí a su única sucursal, y de ahí a la primera del taller.
    /// Nunca falla: una moto sin registrar por una pregunta de papeleo es una moto regalada.
    /// </remarks>
    private async Task<Guid> ResolverSucursalAsync(
        AccessScope scope, RegisterMotorcycleRequest request, CancellationToken ct)
    {
        var delContrato = await db.AssemblyContractors.AsNoTracking()
            .Where(c => c.Id == request.ContractorId)
            .Select(c => c.BranchId)
            .FirstOrDefaultAsync(ct);

        if (delContrato is { } suya) return suya;

        if (request.BranchId is { } pedida)
        {
            scope.EnsureBranchAllowed(pedida);
            return pedida;
        }

        if (scope.BranchIds.Count >= 1) return scope.BranchIds.First();

        return await db.Branches.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => b.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>El cliente no entra aquí: esto es trabajo del taller.</summary>
    private AccessScope EnsureDelTaller()
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer) throw new NotFoundException();

        return scope;
    }

    /// <summary>404 y no 403: al taller que no lo tiene contratado no se le dice que existe.</summary>
    private async Task EnsureEnabledAsync(CancellationToken ct)
    {
        var encendido = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.AssemblyEnabled)
            .FirstOrDefaultAsync(ct);

        if (!encendido) throw new NotFoundException();
    }
}
