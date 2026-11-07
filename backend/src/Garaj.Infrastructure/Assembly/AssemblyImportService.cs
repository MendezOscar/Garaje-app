using ClosedXML.Excel;
using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assembly;

/// <summary>
/// Traerse el Excel que el taller lleva hoy, para no arrancar con la libreta en blanco.
/// </summary>
/// <remarks>
/// Es de una sola vez, y por eso es deliberadamente conservador: <b>no crea nada</b>. Los
/// modelos, las bodegas y los técnicos tienen que existir ya; la fila que nombre algo que no
/// está se rechaza con el motivo escrito. Crear catálogo a partir de una hoja ajena es cómo
/// se terminan con «FT150», «Ft 150» y «ft-150» como tres modelos distintos, y el precio
/// colgando de uno solo.
///
/// Siempre se enseña primero y se guarda después. Lo que entra, entra con el cobro de la
/// hoja —es lo que de verdad se facturó— y el pago al técnico de la tarifa de hoy.
/// </remarks>
public class AssemblyImportService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IAssemblyImportService
{
    /// <summary>Un histórico razonable. Más que esto es otra cosa y conviene mirarla.</summary>
    private const int MaximoDeFilas = 5000;

    public async Task<AssemblyImportPreviewDto> PreviewAsync(
        Stream excel, Guid contractorId, CancellationToken ct = default)
        => (await LeerAsync(excel, contractorId, ct)).Vista;

    public async Task<AssemblyImportPreviewDto> CommitAsync(
        Stream excel, Guid contractorId, CancellationToken ct = default)
    {
        var (vista, buenas) = await LeerAsync(excel, contractorId, ct);

        var scope = AccessScope.From(tenantContext);

        // La sucursal del histórico: la del dueño si tiene una sola, y si no, la primera
        // activa. No se pregunta porque una hoja vieja no la trae.
        var sucursal = scope.BranchIds.Count == 1
            ? scope.BranchIds.First()
            : await db.Branches.AsNoTracking()
                .Where(b => b.IsActive).OrderBy(b => b.Code)
                .Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
                ?? throw new AppException("El taller no tiene sucursales.");

        foreach (var fila in buenas)
        {
            db.MotorcycleAssemblies.Add(new MotorcycleAssembly
            {
                BranchId = sucursal,
                ContractorId = contractorId,
                ModelId = fila.ModelId,
                WarehouseId = fila.WarehouseId,
                TechnicianUserId = fila.TechnicianId,
                AssembledOn = fila.AssembledOn,
                Serial = fila.Serial,
                Charge = fila.Charge,
                TechnicianPay = fila.Pay,
                Notes = "Importada del Excel anterior"
            });
        }

        await db.SaveChangesAsync(ct);

        return vista;
    }

    private async Task<(AssemblyImportPreviewDto Vista, List<Buena> Buenas)> LeerAsync(
        Stream excel, Guid contractorId, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        AccessScope.From(tenantContext).EnsureOwner();

        var existeContratista = await db.AssemblyContractors
            .AnyAsync(c => c.Id == contractorId, ct);

        if (!existeContratista) throw new NotFoundException("El contratista no existe.");

        var modelos = await db.AssemblyModels.AsNoTracking()
            .ToDictionaryAsync(m => Clave(m.Name), m => m.Id, ct);

        var bodegas = await db.AssemblyWarehouses.AsNoTracking()
            .ToDictionaryAsync(w => Clave(w.Name), w => w.Id, ct);

        var tecnicos = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(ct);

        var tarifas = await db.AssemblyRates.AsNoTracking()
            .Where(r => r.ContractorId == contractorId)
            .ToDictionaryAsync(r => r.ModelId, r => r.TechnicianPay, ct);

        var yaEstan = (await db.MotorcycleAssemblies.AsNoTracking()
            .Select(m => m.Serial)
            .ToListAsync(ct)).ToHashSet();

        using var libro = new XLWorkbook(excel);
        var hoja = libro.Worksheets.First();

        var filas = new List<AssemblyImportRowDto>();
        var buenas = new List<Buena>();
        var enEstaHoja = new HashSet<string>();
        var hoy = clock.Today();

        // Se salta la primera fila: toda hoja de estas trae encabezados.
        foreach (var fila in hoja.RowsUsed().Skip(1).Take(MaximoDeFilas))
        {
            ct.ThrowIfCancellationRequested();

            var numero = fila.RowNumber();
            var fecha = Fecha(fila.Cell(1));
            var serie = VinFormatter.Normalize(fila.Cell(2).GetString());
            var modelo = fila.Cell(3).GetString().Trim();
            var tecnico = fila.Cell(4).GetString().Trim();
            var cobro = Numero(fila.Cell(5));
            var bodega = fila.Cell(6).GetString().Trim();

            if (serie.Length == 0 && modelo.Length == 0 && tecnico.Length == 0) continue;

            string? problema = null;

            if (serie.Length < 6) problema = "La serie está vacía o muy corta.";
            else if (yaEstan.Contains(serie)) problema = "Esa serie ya está registrada.";
            else if (!enEstaHoja.Add(serie)) problema = "Esa serie se repite en esta misma hoja.";
            else if (fecha is null) problema = "No se entendió la fecha.";
            else if (fecha > hoy) problema = "La fecha es del futuro.";
            else if (!modelos.TryGetValue(Clave(modelo), out _))
                problema = $"El modelo «{modelo}» no está en el catálogo.";
            else if (!bodegas.TryGetValue(Clave(bodega), out _))
                problema = $"La bodega «{bodega}» no está en el catálogo.";
            else if (BuscarTecnico(tecnicos.Select(t => (t.Id, t.FullName)), tecnico) is null)
                problema = $"No hay un usuario que se llame «{tecnico}».";
            else if (cobro is null or <= 0) problema = "El cobro está vacío o en cero.";

            filas.Add(new AssemblyImportRowDto(
                numero, serie, modelo, tecnico, bodega, fecha, cobro, problema));

            if (problema is not null)
            {
                enEstaHoja.Remove(serie);
                continue;
            }

            var modeloId = modelos[Clave(modelo)];

            buenas.Add(new Buena(
                serie, modeloId, bodegas[Clave(bodega)],
                BuscarTecnico(tecnicos.Select(t => (t.Id, t.FullName)), tecnico)!.Value,
                fecha!.Value, cobro!.Value,
                tarifas.GetValueOrDefault(modeloId)));
        }

        var vista = new AssemblyImportPreviewDto(
            buenas.Count, filas.Count(f => f.Problem is not null), filas);

        return (vista, buenas);
    }

    /// <summary>
    /// El nombre del técnico de la hoja contra los usuarios del taller. Exacto primero, y si
    /// no, el único que contenga lo escrito: la hoja dice «Marvin» y el usuario «Marvin
    /// Zelaya». Si hay dos Marvin, no se adivina.
    /// </summary>
    private static Guid? BuscarTecnico(IEnumerable<(Guid Id, string Nombre)> usuarios, string buscado)
    {
        var clave = Clave(buscado);
        if (clave.Length == 0) return null;

        var lista = usuarios.ToList();

        var exacto = lista.Where(u => Clave(u.Nombre) == clave).ToList();
        if (exacto.Count == 1) return exacto[0].Id;

        var contiene = lista.Where(u => Clave(u.Nombre).Contains(clave)).ToList();
        return contiene.Count == 1 ? contiene[0].Id : null;
    }

    /// <summary>Minúsculas y sin espacios: «FT150», «Ft 150» y «ft-150» son lo mismo.</summary>
    private static string Clave(string? valor) =>
        new((valor ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static DateOnly? Fecha(IXLCell celda)
    {
        if (celda.TryGetValue<DateTime>(out var fecha)) return DateOnly.FromDateTime(fecha);

        var texto = celda.GetString().Trim();

        return DateOnly.TryParse(texto, out var leida) ? leida : null;
    }

    private static decimal? Numero(IXLCell celda) =>
        celda.TryGetValue<decimal>(out var valor) ? valor : null;

    private async Task EnsureEnabledAsync(CancellationToken ct)
    {
        var encendido = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.AssemblyEnabled)
            .FirstOrDefaultAsync(ct);

        if (!encendido) throw new NotFoundException();
    }

    private readonly record struct Buena(
        string Serial,
        Guid ModelId,
        Guid WarehouseId,
        Guid TechnicianId,
        DateOnly AssembledOn,
        decimal Charge,
        decimal Pay);
}
