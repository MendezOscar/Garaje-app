using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assembly;

/// <summary>
/// El catálogo del contrato de armado: contratistas con su tabla de precios, los modelos que
/// se arman y las bodegas a las que va el técnico.
/// </summary>
/// <remarks>
/// Todo esto es del Dueño. Dos cosas que no son obvias y que importan:
///
/// Nada se borra. Un contratista o un modelo que ya tiene motos armadas colgando es historia
/// facturada, y borrarlo dejaría filas sin nombre en el reporte del mes pasado. Se desactivan,
/// que es lo que de verdad quiere decir «ya no trabajamos con ellos».
///
/// Y el módulo apagado responde <b>404 y no 403</b>: a un taller que no lo tiene contratado no
/// se le dice que existe.
/// </remarks>
public class AssemblyCatalogService(GarajDbContext db, ITenantContext tenantContext)
    : IAssemblyCatalogService
{
    public async Task<AssemblySettingsDto> SettingsAsync(CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        var gasto = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.AssemblyExpense)
            .FirstOrDefaultAsync(ct);

        return new AssemblySettingsDto(gasto);
    }

    public async Task<AssemblySettingsDto> SaveSettingsAsync(
        SaveAssemblySettingsRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        if (request.ExpensePerMotorcycle < 0)
            throw new AppException("El gasto no puede ser negativo.");

        var taller = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
            ?? throw new NotFoundException();

        // Cambiarlo no toca lo ya registrado: cada moto se llevó el gasto del día en que se
        // armó. Un mes cerrado no puede cambiar de margen porque hoy suba el transporte.
        taller.AssemblyExpense = request.ExpensePerMotorcycle;

        await db.SaveChangesAsync(ct);

        return new AssemblySettingsDto(taller.AssemblyExpense);
    }

    public async Task<IReadOnlyList<AssemblyContractorDto>> ContractorsAsync(
        CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        var contratistas = await db.AssemblyContractors
            .AsNoTracking()
            .Include(c => c.Rates).ThenInclude(r => r.Model)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return contratistas.Select(Dto).ToList();
    }

    public async Task<AssemblyContractorDto> SaveContractorAsync(
        Guid? id, SaveAssemblyContractorRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        var nombre = request.Name.Trim();

        var repetido = await db.AssemblyContractors
            .AnyAsync(c => c.Name == nombre && (id == null || c.Id != id), ct);

        if (repetido)
            throw new ConflictException($"Ya hay un contratista que se llama {nombre}.");

        AssemblyContractor contratista;

        if (id is { } existente)
        {
            contratista = await db.AssemblyContractors
                .FirstOrDefaultAsync(c => c.Id == existente, ct)
                ?? throw new NotFoundException("El contratista no existe.");
        }
        else
        {
            contratista = new AssemblyContractor();
            db.AssemblyContractors.Add(contratista);
        }

        contratista.Name = nombre;
        contratista.Notes = request.Notes?.Trim();
        contratista.IsActive = request.IsActive;

        // La sucursal del contrato. Con una sola en el taller no se pregunta: queda puesta
        // sola y el técnico nunca ve el tema.
        if (request.BranchId is { } pedida)
        {
            contratista.BranchId = pedida;
        }
        else if (contratista.BranchId is null)
        {
            var sucursales = await db.Branches.AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => b.Id)
                .Take(2)
                .ToListAsync(ct);

            if (sucursales.Count == 1) contratista.BranchId = sucursales[0];
        }

        await db.SaveChangesAsync(ct);

        return await ContractorAsync(contratista.Id, ct);
    }

    public async Task<AssemblyContractorDto> SaveRatesAsync(
        Guid contractorId, SaveAssemblyRatesRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        var existe = await db.AssemblyContractors.AnyAsync(c => c.Id == contractorId, ct);
        if (!existe) throw new NotFoundException("El contratista no existe.");

        // Los modelos tienen que ser de este taller. Es la comprobación que impide que un id
        // copiado de otra parte meta una fila que después nadie sabe de dónde salió.
        var modelos = await db.AssemblyModels.AsNoTracking()
            .Select(m => m.Id)
            .ToListAsync(ct);

        var lineas = request.Rates
            .Where(r => modelos.Contains(r.ModelId))
            .GroupBy(r => r.ModelId)
            .Select(g => g.Last())
            .ToList();

        if (lineas.Any(l => l.Charge < 0 || l.TechnicianPay < 0))
            throw new AppException("Ni el cobro ni el pago pueden ser negativos.");

        // Lo que se le paga al técnico no puede pasar de lo que se cobra: sería trabajar
        // perdiendo, y cuando pasa es porque se tecleó un número en la columna equivocada.
        if (lineas.Any(l => l.TechnicianPay > l.Charge))
            throw new AppException(
                "Hay un modelo donde el pago al técnico supera lo que se le cobra al " +
                "contratista. Revise si los números están cambiados de columna.");

        var actuales = await db.AssemblyRates
            .Where(r => r.ContractorId == contractorId)
            .ToListAsync(ct);

        db.AssemblyRates.RemoveRange(
            actuales.Where(a => lineas.All(l => l.ModelId != a.ModelId)));

        foreach (var linea in lineas)
        {
            var fila = actuales.FirstOrDefault(a => a.ModelId == linea.ModelId);

            if (fila is null)
            {
                // Add explícito: `BaseEntity` asigna el Id al construir, así que una fila nueva
                // bajo un padre ya guardado se seguiría como modificada y el UPDATE no
                // afectaría ninguna fila.
                fila = new AssemblyRate { ContractorId = contractorId, ModelId = linea.ModelId };
                db.AssemblyRates.Add(fila);
            }

            fila.Charge = linea.Charge;
            fila.TechnicianPay = linea.TechnicianPay;
            fila.IsActive = true;
        }

        await db.SaveChangesAsync(ct);

        return await ContractorAsync(contractorId, ct);
    }

    public async Task<IReadOnlyList<AssemblyModelDto>> ModelsAsync(CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureManagement();

        return await db.AssemblyModels.AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new AssemblyModelDto(m.Id, m.Name, m.IsActive, m.VinPrefix))
            .ToListAsync(ct);
    }

    public async Task<AssemblyModelDto> SaveModelAsync(
        Guid? id, SaveAssemblyModelRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        var nombre = request.Name.Trim();

        var repetido = await db.AssemblyModels
            .AnyAsync(m => m.Name == nombre && (id == null || m.Id != id), ct);

        if (repetido) throw new ConflictException($"El modelo {nombre} ya está en la lista.");

        AssemblyModel modelo;

        if (id is { } existente)
        {
            modelo = await db.AssemblyModels.FirstOrDefaultAsync(m => m.Id == existente, ct)
                ?? throw new NotFoundException("El modelo no existe.");
        }
        else
        {
            modelo = new AssemblyModel();
            db.AssemblyModels.Add(modelo);
        }

        modelo.Name = nombre;
        modelo.IsActive = request.IsActive;

        // Se guarda normalizado como la serie: mayúsculas y sin separadores, porque es un
        // trozo de serie y se compara contra series ya normalizadas.
        var prefijo = VinFormatter.Normalize(request.VinPrefix);
        modelo.VinPrefix = string.IsNullOrEmpty(prefijo) ? null : prefijo;

        await db.SaveChangesAsync(ct);

        return new AssemblyModelDto(modelo.Id, modelo.Name, modelo.IsActive, modelo.VinPrefix);
    }

    public async Task<IReadOnlyList<AssemblyWarehouseDto>> WarehousesAsync(
        CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureManagement();

        return await db.AssemblyWarehouses.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new AssemblyWarehouseDto(w.Id, w.Name, w.IsActive))
            .ToListAsync(ct);
    }

    public async Task<AssemblyWarehouseDto> SaveWarehouseAsync(
        Guid? id, SaveAssemblyWarehouseRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        var nombre = request.Name.Trim();

        var repetida = await db.AssemblyWarehouses
            .AnyAsync(w => w.Name == nombre && (id == null || w.Id != id), ct);

        if (repetida) throw new ConflictException($"Ya hay una bodega que se llama {nombre}.");

        AssemblyWarehouse bodega;

        if (id is { } existente)
        {
            bodega = await db.AssemblyWarehouses.FirstOrDefaultAsync(w => w.Id == existente, ct)
                ?? throw new NotFoundException("La bodega no existe.");
        }
        else
        {
            bodega = new AssemblyWarehouse();
            db.AssemblyWarehouses.Add(bodega);
        }

        bodega.Name = nombre;
        bodega.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        return new AssemblyWarehouseDto(bodega.Id, bodega.Name, bodega.IsActive);
    }

    private async Task<AssemblyContractorDto> ContractorAsync(Guid id, CancellationToken ct)
    {
        var contratista = await db.AssemblyContractors
            .AsNoTracking()
            .Include(c => c.Rates).ThenInclude(r => r.Model)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("El contratista no existe.");

        return Dto(contratista);
    }

    private static AssemblyContractorDto Dto(AssemblyContractor c) =>
        new(c.Id, c.Name, c.Notes, c.IsActive,
            c.Rates
                .OrderBy(r => r.Model.Name)
                .Select(r => new AssemblyRateDto(
                    r.ModelId, r.Model.Name, r.Charge, r.TechnicianPay))
                .ToList(),
            c.BranchId);

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
