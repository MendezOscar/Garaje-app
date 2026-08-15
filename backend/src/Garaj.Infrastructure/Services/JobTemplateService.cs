using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.WorkOrders;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Trabajos frecuentes: los pasos y repuestos de lo que el taller repite, guardados para no
/// volver a teclearlos.
/// </summary>
/// <remarks>
/// Aplicar una plantilla se apoya en <see cref="IWorkOrderService.AddTaskAsync"/> en vez de
/// insertar los pasos por su cuenta. Cuesta una ida a la base por paso, y a cambio la plantilla
/// no puede saltarse ninguna de las reglas de la orden —quién puede editarla, en qué estados
/// admite pasos, cómo se numera la secuencia, de dónde salen las horas estándar—, que es
/// justamente lo que se olvidaría de replicar aquí la próxima vez que alguna cambie.
/// </remarks>
public class JobTemplateService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IWorkOrderService workOrders) : IJobTemplateService
{
    public async Task<IReadOnlyList<JobTemplateDto>> ListAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer)
            throw new ForbiddenException("Los trabajos frecuentes son solo para el taller.");

        var q = Query();
        if (!includeInactive) q = q.Where(t => t.IsActive);

        var templates = await q
            .OrderByDescending(t => t.UsageCount)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        return await MapManyAsync(templates, ct);
    }

    public async Task<JobTemplateDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer)
            throw new ForbiddenException("Los trabajos frecuentes son solo para el taller.");

        return (await MapManyAsync([await FindAsync(id, ct)], ct)).Single();
    }

    public async Task<JobTemplateDto> CreateAsync(
        SaveJobTemplateRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var name = CleanName(request.Name);
        await EnsureNameIsFreeAsync(name, null, ct);

        var template = new JobTemplate { Name = name };
        await ApplyAsync(template, request, ct);

        db.JobTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return await GetAsync(template.Id, ct);
    }

    public async Task<JobTemplateDto> UpdateAsync(
        Guid id, SaveJobTemplateRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var template = await FindAsync(id, ct);
        var name = CleanName(request.Name);
        await EnsureNameIsFreeAsync(name, id, ct);

        template.Name = name;

        // Los pasos y repuestos se reemplazan enteros en vez de conciliarse uno a uno: la
        // pantalla manda siempre la lista completa, y casar altas, bajas y reordenamientos
        // sería mucho código para ahorrar unas filas en una tabla de decenas. Bajas y altas
        // van en el mismo SaveChanges, así que comparten su transacción implícita.
        // Sobre una copia: al marcar cada hijo como borrado, EF lo saca de la navegación, y
        // recorrer la misma colección que se está vaciando revienta a media iteración.
        db.JobTemplateTasks.RemoveRange(template.Tasks.ToList());
        db.JobTemplateParts.RemoveRange(template.Parts.ToList());

        await ApplyAsync(template, request, ct);
        await db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var template = await FindAsync(id, ct);
        template.IsActive = false;

        await db.SaveChangesAsync(ct);
    }

    public async Task<JobTemplateDto> CreateFromWorkOrderAsync(
        CreateJobTemplateFromWorkOrderRequest request, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var name = CleanName(request.Name);
        await EnsureNameIsFreeAsync(name, null, ct);

        var order = await db.WorkOrders.AsNoTracking()
            .Include(w => w.Tasks)
            .Include(w => w.Parts)
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        if (order.Tasks.Count == 0 && order.Parts.Count == 0)
            throw new AppException(
                "Esa orden no tiene pasos ni repuestos: no hay nada que guardar como trabajo frecuente.");

        var template = new JobTemplate
        {
            Name = name,
            Description = Clean(request.Description)
        };

        var sequence = 1;
        foreach (var task in order.Tasks.OrderBy(t => t.Sequence))
        {
            template.Tasks.Add(new JobTemplateTask
            {
                Title = task.Title,
                Description = task.Description,
                Sequence = sequence++,
                LaborServiceId = task.LaborServiceId,
                EstimatedHours = task.ActualHours ?? task.EstimatedHours
            });
        }

        // Se agrupan: si el mismo repuesto se cargó en dos líneas —porque se pidió más a media
        // reparación— la plantilla debe decir cuántos lleva el trabajo, no repetir el renglón.
        var parts = order.Parts
            .GroupBy(p => new { p.PartId, p.Description })
            .Select(g => new JobTemplatePart
            {
                PartId = g.Key.PartId,
                Description = g.Key.Description,
                Quantity = g.Sum(p => p.Quantity)
            });

        foreach (var part in parts) template.Parts.Add(part);

        db.JobTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return await GetAsync(template.Id, ct);
    }

    public async Task<ApplyJobTemplateResultDto> ApplyToWorkOrderAsync(
        Guid workOrderId, ApplyJobTemplateRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        if (scope.IsCustomer)
            throw new ForbiddenException("Un cliente no puede armar la orden de trabajo.");

        var template = await FindAsync(request.TemplateId, ct);

        if (!template.IsActive)
            throw new ConflictException($"«{template.Name}» está dado de baja.");

        // La sucursal decide de qué bodega se mira la existencia, y leerla antes de crear nada
        // deja que un 404 de orden salga sin haber tocado la plantilla.
        var branchId = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == workOrderId)
            .Select(w => (Guid?)w.BranchId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        var created = new List<WorkOrderTaskDto>();

        foreach (var task in template.Tasks.OrderBy(t => t.Sequence))
        {
            created.Add(await workOrders.AddTaskAsync(
                workOrderId,
                new SaveWorkOrderTaskRequest(
                    task.Title, task.Description, null, task.LaborServiceId, task.EstimatedHours),
                ct));
        }

        template.UsageCount++;
        template.LastUsedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return new ApplyJobTemplateResultDto(
            template.Name, created, await SuggestPartsAsync(template, branchId, ct));
    }

    /// <summary>
    /// Los repuestos del trabajo con su precio de hoy y lo que hay en la bodega de esa
    /// sucursal, para que se vea antes de intentar cargarlo que de ese no queda.
    /// </summary>
    private async Task<IReadOnlyList<SuggestedPartDto>> SuggestPartsAsync(
        JobTemplate template, Guid branchId, CancellationToken ct)
    {
        var partIds = template.Parts.Where(p => p.PartId != null).Select(p => p.PartId!.Value).ToList();

        var catalog = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var stock = await db.StockItems.AsNoTracking()
            .Where(s => s.BranchId == branchId && partIds.Contains(s.PartId))
            .ToDictionaryAsync(s => s.PartId, s => s.Quantity, ct);

        return template.Parts
            .Select(line =>
            {
                if (line.PartId is not { } partId || !catalog.TryGetValue(partId, out var part))
                    return new SuggestedPartDto(
                        null, "", line.Description ?? "Repuesto", "unidad",
                        line.Quantity, 0, 0, line.Description);

                return new SuggestedPartDto(
                    part.Id, part.Sku, part.Name, part.Unit,
                    line.Quantity, part.SalePrice,
                    stock.GetValueOrDefault(partId), null);
            })
            .ToList();
    }

    private IQueryable<JobTemplate> Query() =>
        db.JobTemplates.AsNoTracking().Include(t => t.Tasks).Include(t => t.Parts);

    private async Task<JobTemplate> FindAsync(Guid id, CancellationToken ct) =>
        await db.JobTemplates
            .Include(t => t.Tasks)
            .Include(t => t.Parts)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("El trabajo frecuente no existe.");

    private async Task ApplyAsync(
        JobTemplate template, SaveJobTemplateRequest request, CancellationToken ct)
    {
        var tasks = request.Tasks ?? [];
        var parts = request.Parts ?? [];

        if (tasks.Count == 0 && parts.Count == 0)
            throw new AppException("Un trabajo frecuente necesita al menos un paso o un repuesto.");

        template.Description = Clean(request.Description);
        template.IsActive = request.IsActive;

        // Los hijos se agregan por su DbSet con la clave ajena puesta a mano, y **no** colgados
        // de `template.Tasks`. La diferencia no es de estilo: `BaseEntity` asigna el `Id` en el
        // constructor, así que al colgar uno nuevo de la navegación de una plantilla que ya
        // existe, EF ve una clave con valor y lo marca `Modified` en vez de `Added` — intenta
        // un UPDATE de una fila que no está y el guardado revienta. Con `Add` siempre es alta.
        var sequence = 1;
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Title))
                throw new AppException("Cada paso necesita un título.");

            if (task.LaborServiceId is { } serviceId
                && !await db.LaborServices.AnyAsync(s => s.Id == serviceId, ct))
                throw new NotFoundException("El servicio de mano de obra no existe.");

            db.JobTemplateTasks.Add(new JobTemplateTask
            {
                JobTemplateId = template.Id,
                Title = task.Title.Trim(),
                Description = Clean(task.Description),
                Sequence = sequence++,
                LaborServiceId = task.LaborServiceId,
                EstimatedHours = task.EstimatedHours
            });
        }

        foreach (var part in parts)
        {
            if (part.Quantity <= 0)
                throw new AppException("La cantidad de cada repuesto debe ser mayor que cero.");

            if (part.PartId is { } partId)
            {
                if (!await db.Parts.AnyAsync(p => p.Id == partId, ct))
                    throw new NotFoundException("El repuesto no existe.");
            }
            else if (string.IsNullOrWhiteSpace(part.Description))
            {
                throw new AppException(
                    "Un repuesto fuera del catálogo necesita que se escriba de qué se trata.");
            }

            db.JobTemplateParts.Add(new JobTemplatePart
            {
                JobTemplateId = template.Id,
                PartId = part.PartId,
                Description = part.PartId is null ? Clean(part.Description) : null,
                Quantity = part.Quantity
            });
        }

            }

    /// <summary>
    /// El precio sale del catálogo en cada consulta y no se guarda en la plantilla: subir
    /// mañana el precio de un repuesto no puede dejar veinte trabajos frecuentes mintiendo.
    /// </summary>
    private async Task<IReadOnlyList<JobTemplateDto>> MapManyAsync(
        IReadOnlyList<JobTemplate> templates, CancellationToken ct)
    {
        var serviceIds = templates
            .SelectMany(t => t.Tasks)
            .Where(t => t.LaborServiceId != null)
            .Select(t => t.LaborServiceId!.Value)
            .Distinct()
            .ToList();

        var partIds = templates
            .SelectMany(t => t.Parts)
            .Where(p => p.PartId != null)
            .Select(p => p.PartId!.Value)
            .Distinct()
            .ToList();

        var services = await db.LaborServices.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var parts = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        return templates.Select(template =>
        {
            var tasks = template.Tasks.OrderBy(t => t.Sequence).Select(t =>
            {
                var service = t.LaborServiceId is { } id ? services.GetValueOrDefault(id) : null;

                return new JobTemplateTaskDto(
                    t.Id, t.Title, t.Description, t.Sequence,
                    t.LaborServiceId, service?.Name, t.EstimatedHours,
                    service?.PriceFor(t.EstimatedHours));
            }).ToList();

            var lines = template.Parts.Select(p =>
            {
                var part = p.PartId is { } id ? parts.GetValueOrDefault(id) : null;

                return new JobTemplatePartDto(
                    p.Id, p.PartId,
                    part?.Sku ?? "",
                    part?.Name ?? p.Description ?? "Repuesto",
                    part?.Unit ?? "unidad",
                    p.Quantity,
                    part?.SalePrice ?? 0,
                    p.Quantity * (part?.SalePrice ?? 0));
            }).ToList();

            var labor = tasks.Sum(t => t.Price ?? 0);
            var partsTotal = lines.Sum(l => l.Total);

            return new JobTemplateDto(
                template.Id, template.Name, template.Description, template.IsActive,
                template.UsageCount, template.LastUsedAt,
                tasks, lines, labor, partsTotal, labor + partsTotal);
        }).ToList();
    }

    private async Task EnsureNameIsFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.JobTemplates
            .AnyAsync(t => t.Name == name && (exceptId == null || t.Id != exceptId), ct);

        if (taken) throw new ConflictException($"Ya existe un trabajo frecuente llamado «{name}».");
    }

    private static string CleanName(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new AppException("El trabajo frecuente necesita un nombre.")
            : value.Trim();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
