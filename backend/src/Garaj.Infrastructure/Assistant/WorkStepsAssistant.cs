using System.Text.Json;
using Garaj.Application.Abstractions;
using Garaj.Application.Assistant;
using Garaj.Application.Common;
using Garaj.Application.WorkOrders;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assistant;

/// <summary>
/// «Proponer los pasos». Arma el caso, llama al modelo y valida lo que vuelve.
/// </summary>
/// <remarks>
/// La respuesta del modelo se trata <b>como si viniera de un cliente hostil</b>, porque a
/// efectos prácticos viene de afuera: los códigos tienen que existir y ser de este taller, las
/// cantidades positivas, las horas dentro de rango y los textos recortados. Lo que no cuadra se
/// descarta en silencio —no se guarda y no se enseña—; vale más una propuesta corta que una con
/// un repuesto que el taller no tiene, porque quien la revisa está apurado y acepta lo que ve.
/// </remarks>
public class WorkStepsAssistant(
    GarajDbContext db,
    ITenantContext tenantContext,
    IAssistant assistant,
    IAssistantQuota quota,
    IAssistantCatalog catalog,
    IWorkOrderService workOrders,
    IDateTimeProvider clock) : IWorkStepsAssistant
{
    /// <summary>Topes de lo que se acepta del modelo. Ni un taller cobra veinte pasos.</summary>
    private const int MaximoPasos = 20;

    private const int MaximoRepuestos = 30;
    private const decimal MaximoHoras = 40m;
    private const decimal MaximaCantidad = 100m;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<WorkStepsProposalDto> ProposeAsync(
        Guid workOrderId, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        await EnsureEnabledAsync(ct);

        var orden = await db.WorkOrders
            .AsNoTracking()
            .Where(o => o.Id == workOrderId)
            .Select(o => new { o.BranchId, o.Diagnosis })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        scope.EnsureBranchAllowed(orden.BranchId);

        if (string.IsNullOrWhiteSpace(orden.Diagnosis))
            return Sin("Escriba primero el diagnóstico: de ahí salen los pasos.", 0);

        var permiso = await quota.CheckAsync(AssistantFeature.WorkSteps, ct);

        if (!permiso.Allowed)
            return Sin(permiso.Reason!, Restantes(permiso.Usage));

        var material = await catalog.ForWorkOrderAsync(workOrderId, ct);

        var prompt = new AssistantPrompt(
            WorkStepsPrompt.Instructions(material.Kind),
            WorkStepsPrompt.Context(material),
            WorkStepsPrompt.Question(material.Subject, orden.Diagnosis, await CodigosAsync(workOrderId, ct)),
            WorkStepsPrompt.Schema,
            MaxTokens: 8000);

        var salida = await assistant.AskAsync(prompt, ct);

        // Se registra siempre, salga bien o mal: un reintento consume tokens igual, y sin esto
        // el techo del mes sería una idea.
        var llamadaId = await quota.RecordAsync(
            AssistantFeature.WorkSteps, salida, workOrderId, ct);

        var uso = await quota.UsageAsync(ct);

        if (!salida.Succeeded || salida.Json is null)
            return Sin(salida.Error ?? "El asistente no pudo responder.", Restantes(uso));

        var cruda = Leer(salida.Json);

        if (cruda is null)
            return Sin("El asistente devolvió algo que no se entendió. Intente de nuevo.", Restantes(uso));

        var propuesta = new AssistantProposal
        {
            WorkOrderId = workOrderId,
            Feature = AssistantFeature.WorkSteps,
            Status = AssistantProposalStatus.Draft,

            // Se guarda el envío entero: cuando una propuesta salga mal hay que poder ver qué
            // vio el modelo, y sin eso no se distingue un prompt flojo de un catálogo mal
            // filtrado.
            Request = JsonSerializer.Serialize(new
            {
                prompt.Instructions,
                prompt.Context,
                prompt.Question
            }),
            Response = salida.Json,
            AssistantCallId = llamadaId
        };

        db.AssistantProposals.Add(propuesta);
        await db.SaveChangesAsync(ct);

        var (pasos, repuestos, faltantes) =
            await ValidarAsync(cruda, material, orden.BranchId, ct);

        return new WorkStepsProposalDto(
            propuesta.Id, true, null, pasos, repuestos, Nota(cruda.Nota), Restantes(uso),
            faltantes);
    }

    public async Task<AcceptWorkStepsResultDto> AcceptAsync(
        Guid workOrderId, Guid proposalId, AcceptWorkStepsRequest request,
        CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();

        await EnsureEnabledAsync(ct);

        var propuesta = await db.AssistantProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.WorkOrderId == workOrderId, ct)
            ?? throw new NotFoundException("La propuesta no existe.");

        if (propuesta.Status != AssistantProposalStatus.Draft)
            throw new ConflictException("Esta propuesta ya se aceptó.");

        if (request.Tasks.Count == 0)
            throw new ConflictException("No quedó ningún paso que agregar.");

        var branchId = await db.WorkOrders.AsNoTracking()
            .Where(o => o.Id == workOrderId)
            .Select(o => (Guid?)o.BranchId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        scope.EnsureBranchAllowed(branchId);

        // Se anexan, no se reemplazan: un trabajo real es «aceite y frenos», y la orden pudo
        // traer pasos de antes.
        var creados = new List<WorkOrderTaskDto>();

        foreach (var paso in request.Tasks.Take(MaximoPasos))
            creados.Add(await workOrders.AddTaskAsync(workOrderId, paso, ct));

        propuesta.Status = AssistantProposalStatus.Accepted;
        propuesta.AcceptedByUserId = tenantContext.UserId;
        propuesta.AcceptedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        // Los repuestos se vuelven a resolver contra el catálogo de ahora, no contra el de
        // cuando se propuso: entre una cosa y la otra pudieron darse de baja.
        var cruda = Leer(propuesta.Response);
        var sugeridos = cruda is null
            ? []
            : await SugerirAsync(cruda.Repuestos, branchId, ct);

        return new AcceptWorkStepsResultDto(creados, sugeridos);
    }

    /// <summary>
    /// El taller sin asistente no sabe que esto existe: <b>404, no 403</b>. Un 403 confirmaría
    /// que la ruta existe y qué hace.
    /// </summary>
    private async Task EnsureEnabledAsync(CancellationToken ct)
    {
        var encendido = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.AssistantEnabled)
            .FirstOrDefaultAsync(ct);

        if (!encendido) throw new NotFoundException();
    }

    /// <summary>Los códigos de la última lectura del escáner, si la orden tiene una.</summary>
    private async Task<string?> CodigosAsync(Guid workOrderId, CancellationToken ct)
    {
        var ultima = await db.VehicleScans
            .AsNoTracking()
            .Where(s => s.WorkOrderId == workOrderId)
            .OrderByDescending(s => s.ScannedAt)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (ultima == Guid.Empty) return null;

        var codigos = await db.VehicleScanCodes
            .AsNoTracking()
            .Where(c => c.VehicleScanId == ultima)
            .OrderBy(c => c.Code)
            .Select(c => c.Description == null ? c.Code : c.Code + " (" + c.Description + ")")
            .ToListAsync(ct);

        return codigos.Count == 0 ? null : string.Join("; ", codigos);
    }

    private static RespuestaDelAsistente? Leer(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RespuestaDelAsistente>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<(List<ProposedTaskDto>, List<ProposedPartDto>, List<MissingPartDto>)>
        ValidarAsync(
            RespuestaDelAsistente cruda, AssistantCatalogDto material, Guid branchId,
            CancellationToken ct)
    {
        var servicios = material.Services.ToDictionary(
            s => s.Code, s => s, StringComparer.OrdinalIgnoreCase);

        var pasos = new List<ProposedTaskDto>();
        var conServicio = new List<(int Indice, Guid ServicioId)>();

        foreach (var paso in cruda.Pasos.Take(MaximoPasos))
        {
            var titulo = Recorte(paso.Titulo, 200);
            if (titulo is null) continue;

            AssistantServiceDto? servicio = null;

            if (!string.IsNullOrWhiteSpace(paso.CodigoManoDeObra)
                && servicios.TryGetValue(paso.CodigoManoDeObra.Trim(), out var encontrado))
                servicio = encontrado;

            if (servicio is not null) conServicio.Add((pasos.Count, servicio.Id));

            pasos.Add(new ProposedTaskDto(
                titulo,
                Recorte(paso.Descripcion, 1000),
                servicio?.Id,
                servicio?.Name,
                Math.Clamp(paso.Horas, 0m, MaximoHoras)));
        }

        // Lo que el taller ha cobrado por cada uno de estos trabajos. Lo calcula el servidor:
        // es un número de sus propias órdenes, no uno que el modelo se saque de la manga.
        var historial = await HistorialAsync(conServicio.Select(x => x.ServicioId).ToList(), ct);

        foreach (var (indice, servicioId) in conServicio)
        {
            if (historial.TryGetValue(servicioId, out var h))
                pasos[indice] = pasos[indice] with { History = h };
        }

        var repuestos = await SugerirAsync(cruda.Repuestos, branchId, ct);

        return (
            pasos,
            repuestos
                .Select(r => new ProposedPartDto(
                    r.PartId!.Value, r.Sku, r.PartName, r.Unit, r.Quantity, r.Available,
                    r.Description ?? ""))
                .ToList(),
            Faltantes(cruda.Faltantes, material));
    }

    /// <summary>
    /// Lo que el trabajo lleva y el taller no tiene. Va aparte de los repuestos del catálogo
    /// para que nadie los confunda con existencia.
    /// </summary>
    /// <remarks>
    /// Si el modelo escribe aquí algo que sí está en el catálogo, se descarta: ese ya salió
    /// —o debió salir— por el otro lado, y verlo dos veces haría dudar de la lista entera.
    /// </remarks>
    private static List<MissingPartDto> Faltantes(
        List<FaltantePropuesto>? propuestos, AssistantCatalogDto material)
    {
        if (propuestos is null or { Count: 0 }) return [];

        var enCatalogo = new HashSet<string>(
            material.Parts.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var salida = new List<MissingPartDto>();

        foreach (var falta in propuestos.Take(MaximoRepuestos))
        {
            var nombre = Recorte(falta.Nombre, 160);

            if (nombre is null || falta.Cantidad <= 0 || enCatalogo.Contains(nombre)) continue;

            salida.Add(new MissingPartDto(
                nombre,
                Math.Min(falta.Cantidad, MaximaCantidad),
                Recorte(falta.ParaElPaso, 200) ?? ""));
        }

        return salida;
    }

    /// <summary>
    /// Qué ha cobrado este taller por cada uno de estos servicios, de sus órdenes entregadas
    /// del último año.
    /// </summary>
    /// <remarks>
    /// Las filas se traen y la cuenta se hace aquí porque el precio de un paso no está
    /// guardado: sale del precio a mano si lo tiene, y si no del servicio con las horas que
    /// llevó —<see cref="WorkOrderTask.PriceWith"/>—. Traducir esa regla a SQL sería tener la
    /// misma cuenta escrita en dos sitios, que es como empiezan a no coincidir.
    ///
    /// Solo entregadas: una orden abierta todavía puede cambiar de precio, y una cancelada no
    /// se cobró.
    /// </remarks>
    private async Task<Dictionary<Guid, TaskHistoryDto>> HistorialAsync(
        List<Guid> servicioIds, CancellationToken ct)
    {
        if (servicioIds.Count == 0) return [];

        var desde = clock.UtcNow.AddYears(-1);

        var filas = await db.WorkOrderTasks
            .AsNoTracking()
            .Where(t => t.LaborServiceId != null
                && servicioIds.Contains(t.LaborServiceId.Value)
                && t.WorkOrder.Status == WorkOrderStatus.Delivered
                && t.WorkOrder.ClosedAt >= desde)
            .Select(t => new
            {
                ServicioId = t.LaborServiceId!.Value,
                t.ManualLaborPrice,
                t.ActualHours,
                t.EstimatedHours
            })
            .Take(500)
            .ToListAsync(ct);

        if (filas.Count == 0) return [];

        var servicios = await db.LaborServices
            .AsNoTracking()
            .Where(s => servicioIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var historial = new Dictionary<Guid, TaskHistoryDto>();

        foreach (var grupo in filas.GroupBy(f => f.ServicioId))
        {
            servicios.TryGetValue(grupo.Key, out var servicio);

            var precios = grupo
                .Select(f => f.ManualLaborPrice ?? servicio?.PriceFor(f.ActualHours ?? f.EstimatedHours))
                .Where(p => p is > 0)
                .Select(p => p!.Value)
                .ToList();

            var horas = grupo
                .Select(f => f.ActualHours)
                .Where(h => h is > 0)
                .Select(h => h!.Value)
                .ToList();

            if (precios.Count == 0 && horas.Count == 0) continue;

            historial[grupo.Key] = new TaskHistoryDto(
                grupo.Count(),
                precios.Count == 0 ? null : precios.Min(),
                precios.Count == 0 ? null : precios.Max(),
                horas.Count == 0 ? null : Math.Round(horas.Average(), 2));
        }

        return historial;
    }

    /// <summary>
    /// Los repuestos propuestos, resueltos contra el catálogo del taller. Un código que no
    /// exista —o que sea de otro taller, que el filtro por tenant ya impide— sencillamente no
    /// aparece.
    /// </summary>
    private async Task<List<SuggestedPartDto>> SugerirAsync(
        List<RepuestoPropuesto> propuestos, Guid branchId, CancellationToken ct)
    {
        var skus = propuestos
            .Select(r => r.Sku?.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximoRepuestos)
            .ToList();

        if (skus.Count == 0) return [];

        var catalogo = await db.Parts
            .AsNoTracking()
            .Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, StringComparer.OrdinalIgnoreCase, ct);

        var ids = catalogo.Values.Select(p => p.Id).ToList();

        var existencias = await db.StockItems
            .AsNoTracking()
            .Where(s => s.BranchId == branchId && ids.Contains(s.PartId))
            .ToDictionaryAsync(s => s.PartId, s => s.Quantity, ct);

        var salida = new List<SuggestedPartDto>();

        foreach (var propuesto in propuestos.Take(MaximoRepuestos))
        {
            var sku = propuesto.Sku?.Trim();

            if (string.IsNullOrEmpty(sku) || !catalogo.TryGetValue(sku, out var parte))
                continue;

            if (propuesto.Cantidad <= 0) continue;

            salida.Add(new SuggestedPartDto(
                parte.Id, parte.Sku, parte.Name, parte.Unit,
                Math.Min(propuesto.Cantidad, MaximaCantidad),
                parte.SalePrice,
                existencias.GetValueOrDefault(parte.Id),
                Recorte(propuesto.ParaElPaso, 200)));
        }

        return salida;
    }

    private static string? Recorte(string? texto, int largo)
    {
        var limpio = texto?.Trim();

        if (string.IsNullOrEmpty(limpio)) return null;

        return limpio.Length <= largo ? limpio : limpio[..largo];
    }

    private static string? Nota(string? nota) => Recorte(nota, 600);

    private static int Restantes(AssistantUsageDto uso) =>
        Math.Max(0, uso.CreditsPerDay - uso.CreditsToday);

    private static WorkStepsProposalDto Sin(string mensaje, int restantes) =>
        new(null, false, mensaje, [], [], null, restantes);
}
