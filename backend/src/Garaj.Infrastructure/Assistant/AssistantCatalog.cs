using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// Qué del catálogo del taller le sirve a esta orden. Es el trabajo real de la dirección 1.
/// </summary>
/// <remarks>
/// Todo sale de consultas que pasan por el filtro por tenant: nada de <c>BypassTenantFilter</c>,
/// que hay un usuario detrás de cada llamada. Y nada de lo que devuelve identifica al cliente.
/// </remarks>
public class AssistantCatalog(GarajDbContext db, ITenantContext tenant) : IAssistantCatalog
{
    /// <summary>
    /// Tope de repuestos que se mandan. Si el filtro por vehículo deja más que esto, el
    /// catálogo del taller no tiene las aplicaciones cargadas y mandarlo entero sería caro sin
    /// mejorar nada. Se prefiere una lista corta y buena.
    /// </summary>
    private const int MaximoRepuestos = 150;

    public async Task<AssistantCatalogDto> ForWorkOrderAsync(
        Guid workOrderId, CancellationToken ct = default)
    {
        var orden = await db.WorkOrders
            .AsNoTracking()
            .Where(o => o.Id == workOrderId)
            .Select(o => new
            {
                o.MileageIn,
                Marca = o.Vehicle != null ? o.Vehicle.Brand : o.Equipment!.Brand,
                Modelo = o.Vehicle != null ? o.Vehicle.Model : o.Equipment!.Model,
                Anio = o.Vehicle != null ? o.Vehicle.Year : null,
                Kilometraje = o.Vehicle != null ? o.Vehicle.Mileage : null
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("La orden no existe.");

        var tipo = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenant.TenantId)
            .Select(t => t.WorksWith)
            .FirstAsync(ct);

        var sujeto = new AssistantSubjectDto(
            orden.Marca, orden.Modelo, orden.Anio, orden.MileageIn ?? orden.Kilometraje, null);

        return new AssistantCatalogDto(
            tipo, sujeto, await RepuestosAsync(sujeto, ct), await ServiciosAsync(ct));
    }

    /// <summary>
    /// Los repuestos que le sirven a este vehículo, más los que no tienen aplicaciones
    /// cargadas.
    /// </summary>
    /// <remarks>
    /// Los que no las tienen entran a propósito: en un taller que viene empezando son casi
    /// todos, y dejarlos fuera convertiría la función en inútil justo donde más se necesita.
    /// Lo que se excluye es lo que está marcado para <b>otro</b> vehículo, que es la única
    /// afirmación que el dato permite hacer con certeza.
    /// </remarks>
    private async Task<IReadOnlyList<AssistantPartDto>> RepuestosAsync(
        AssistantSubjectDto sujeto, CancellationToken ct)
    {
        var consulta = db.Parts.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(sujeto.Brand))
        {
            var marca = sujeto.Brand.Trim();
            var modelo = sujeto.Model?.Trim();
            var anio = sujeto.Year;

            consulta = consulta.Where(p =>
                !p.Applications.Any()
                || p.Applications.Any(a =>
                    a.Brand.ToLower() == marca.ToLower()
                    && (a.Model == null || modelo == null || a.Model.ToLower() == modelo.ToLower())
                    && (anio == null || a.YearFrom == null || a.YearFrom <= anio)
                    && (anio == null || a.YearTo == null || a.YearTo >= anio)));
        }

        // Orden estable por código: el catálogo va dentro del prefijo cacheado, y si el orden
        // cambia entre dos llamadas el caché deja de servir sin avisar de nada.
        return await consulta
            .OrderBy(p => p.Sku)
            .Take(MaximoRepuestos)
            .Select(p => new AssistantPartDto(
                p.Id, p.Sku, p.Name, p.Brand, p.Category, p.Unit))
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<AssistantServiceDto>> ServiciosAsync(CancellationToken ct) =>
        await db.LaborServices
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Code)
            .Select(s => new AssistantServiceDto(
                s.Id, s.Code, s.Name, s.Category, s.StandardHours))
            .ToListAsync(ct);
}
