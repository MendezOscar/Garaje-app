using Garaj.Application.Abstractions;
using Garaj.Application.Assembly;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Garaj.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Assembly;

/// <summary>
/// El lado del dueño: revisar lo armado, corregir lo que venga mal y cerrar los cortes.
/// </summary>
/// <remarks>
/// El corte es el centro de esto. Contesta la pregunta que una hoja de cálculo nunca pudo
/// contestar —«¿esto ya lo cobré?»— y, al cerrarse, congela las motos que incluyó: del otro
/// lado hay una factura emitida o una plata entregada, y corregir una fila después sería
/// cambiar el pasado. Se puede reabrir, que es la única manera honesta de arreglar un corte
/// mal hecho.
/// </remarks>
public class AssemblyAdminService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IAssemblyAdminService
{
    public async Task<AssemblyPageDto> ListAsync(
        AssemblyQuery query, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureManda();

        var q = Filtrar(query, scope);

        // Los totales son los del filtro entero, no los de la página: quien va a facturar
        // necesita el total del periodo, no el de las primeras veinticinco.
        var totales = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cuantas = g.Count(),
                Cobro = g.Sum(m => m.Charge),
                Pago = g.Sum(m => m.TechnicianPay),
                Gasto = g.Sum(m => m.Expense)
            })
            .FirstOrDefaultAsync(ct);

        var filas = await q
            .OrderByDescending(m => m.AssembledOn).ThenByDescending(m => m.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
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
                m.Charge,
                m.TechnicianPay,
                m.Expense,
                m.Notes,
                Billed = m.BillingSettlementId != null,
                Paid = m.PaySettlementId != null
            })
            .ToListAsync(ct);

        var nombres = await NombresAsync(filas.Select(f => f.TechnicianUserId), ct);

        var items = filas.Select(f => new AssemblyRowDto(
            f.Id, f.AssembledOn, f.Serial, f.ModelName, f.ContractorName, f.WarehouseName,
            nombres.GetValueOrDefault(f.TechnicianUserId, "—"),
            f.Charge, f.TechnicianPay, f.Notes, f.Billed, f.Paid, f.Expense,
            f.ModelId, f.ContractorId, f.WarehouseId, f.TechnicianUserId)).ToList();

        return new AssemblyPageDto(
            items,
            totales?.Cuantas ?? 0,
            query.Page,
            query.PageSize,
            totales?.Cobro ?? 0m,
            totales?.Pago ?? 0m,
            totales?.Gasto ?? 0m);
    }

    public async Task<AssemblyRowDto> CorrectAsync(
        Guid id, CorrectMotorcycleRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureManda();

        var moto = await db.MotorcycleAssemblies.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("La moto no existe.");

        scope.EnsureBranchAllowed(moto.BranchId);
        EnsureNoCongelada(moto);

        var serie = VinFormatter.Normalize(request.Serial);

        if (serie.Length < 6)
            throw new AppException("La serie está muy corta. Cópiela completa del chasis.");

        if (serie != moto.Serial)
        {
            var repetida = await db.MotorcycleAssemblies.AsNoTracking()
                .AnyAsync(m => m.Serial == serie && m.Id != id, ct);

            if (repetida)
                throw new ConflictException($"La serie {serie} ya está en otra moto.");
        }

        if (request.AssembledOn > clock.Today())
            throw new AppException("No se puede poner una fecha de mañana.");

        // Los importes: los que vengan escritos a mano, y si no vienen, los de la tarifa de
        // hoy. Es lo que se quiere cuando la moto se registró con el modelo equivocado.
        var cobro = request.Charge;
        var pago = request.TechnicianPay;

        if (cobro is null || pago is null)
        {
            var tarifa = await db.AssemblyRates.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.ContractorId == request.ContractorId && r.ModelId == request.ModelId,
                    ct);

            if (tarifa is null)
                throw new AppException(
                    "Ese modelo no tiene precio con ese contratista. Póngalo en la ficha del " +
                    "contratista, o escriba los montos a mano aquí.");

            cobro ??= tarifa.Charge;

            // El técnico contratado a tanto por moto cobra lo suyo, no lo del modelo. Si al
            // corregir se cambia de técnico, el pago se recalcula con el trato del nuevo.
            pago ??= await db.UsersInTenant.AsNoTracking()
                .Where(u => u.Id == request.TechnicianUserId)
                .Select(u => u.AssemblyFixedPay)
                .FirstOrDefaultAsync(ct) ?? tarifa.TechnicianPay;
        }

        if (cobro < 0 || pago < 0)
            throw new AppException("Ni el cobro ni el pago pueden ser negativos.");

        moto.Serial = serie;
        moto.ModelId = request.ModelId;
        moto.ContractorId = request.ContractorId;
        moto.WarehouseId = request.WarehouseId;
        moto.TechnicianUserId = request.TechnicianUserId;
        moto.AssembledOn = request.AssembledOn;
        moto.Notes = request.Notes?.Trim();
        moto.Charge = cobro.Value;
        moto.TechnicianPay = pago.Value;

        await db.SaveChangesAsync(ct);

        var pagina = await ListAsync(new AssemblyQuery { PageSize = 1, Serial = serie }, ct);

        return pagina.Items.FirstOrDefault()
            ?? throw new NotFoundException("La moto no existe.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureManda();
        scope.EnsureOwner();

        var moto = await db.MotorcycleAssemblies.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("La moto no existe.");

        EnsureNoCongelada(moto);

        db.MotorcycleAssemblies.Remove(moto);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AssemblySettlementDto>> SettlementsAsync(
        CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        EnsureManda();

        var cortes = await db.AssemblySettlements.AsNoTracking()
            .OrderByDescending(s => s.PeriodTo).ThenByDescending(s => s.CreatedAt)
            .Take(200)
            .Select(s => new
            {
                s.Id, s.Kind, s.PeriodFrom, s.PeriodTo,
                s.ContractorId, ContractorName = s.Contractor!.Name,
                s.TechnicianUserId, s.Count, s.Total, s.Reference, s.CreatedAt,
                s.ClosedByUserId, s.CollectedOn, s.CollectedReference
            })
            .ToListAsync(ct);

        var ids = cortes.SelectMany(c => new[] { c.TechnicianUserId, c.ClosedByUserId })
            .Where(x => x is not null).Select(x => x!.Value);

        var nombres = await NombresAsync(ids, ct);

        return cortes.Select(c => new AssemblySettlementDto(
            c.Id, c.Kind, c.PeriodFrom, c.PeriodTo,
            c.ContractorId, c.ContractorName,
            c.TechnicianUserId,
            c.TechnicianUserId is { } t ? nombres.GetValueOrDefault(t, "—") : null,
            c.Count, c.Total, c.Reference, c.CreatedAt,
            c.ClosedByUserId is { } u ? nombres.GetValueOrDefault(u) : null,
            c.CollectedOn, c.CollectedReference)).ToList();
    }

    public async Task<AssemblySettlementDto> CloseAsync(
        CloseSettlementRequest request, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureManda();
        scope.EnsureOwner();

        if (request.To < request.From)
            throw new AppException("El periodo termina antes de empezar.");

        var esFacturacion = request.Kind == AssemblySettlementKind.Billing;

        if (esFacturacion && request.ContractorId is null)
            throw new AppException("Diga a qué contratista se le facturó.");

        if (!esFacturacion && request.TechnicianUserId is null)
            throw new AppException("Diga a qué técnico se le pagó.");

        // Solo lo que todavía no entró en un corte del mismo tipo. Es lo que impide cobrar o
        // pagar dos veces el mismo mes, y por eso el filtro no es negociable.
        var q = db.MotorcycleAssemblies
            .Where(m => m.AssembledOn >= request.From && m.AssembledOn <= request.To);

        q = esFacturacion
            ? q.Where(m => m.BillingSettlementId == null && m.ContractorId == request.ContractorId)
            : q.Where(m => m.PaySettlementId == null && m.TechnicianUserId == request.TechnicianUserId);

        var motos = await q.ToListAsync(ct);

        if (motos.Count == 0)
            throw new AppException("No hay nada pendiente en ese periodo. Ya se cerró o no hubo.");

        if (esFacturacion && request.Total is not null)
            throw new AppException(
                "Lo que se le factura al contratista sale de las motos, no se escribe a mano.");

        if (request.Total is < 0)
            throw new AppException("El pago no puede ser negativo.");

        // La suma de sus motos es la propuesta; lo que de verdad se le paga lo decide el
        // dueño, porque hay acuerdos que no caben en ninguna tarifa.
        var total = esFacturacion
            ? motos.Sum(m => m.Charge)
            : request.Total ?? motos.Sum(m => m.TechnicianPay);

        var corte = new AssemblySettlement
        {
            Kind = request.Kind,
            PeriodFrom = request.From,
            PeriodTo = request.To,
            ContractorId = esFacturacion ? request.ContractorId : null,
            TechnicianUserId = esFacturacion ? null : request.TechnicianUserId,
            Count = motos.Count,
            Total = total,
            Reference = request.Reference?.Trim(),
            ClosedByUserId = scope.UserId
        };

        db.AssemblySettlements.Add(corte);

        foreach (var moto in motos)
        {
            if (esFacturacion) moto.BillingSettlementId = corte.Id;
            else moto.PaySettlementId = corte.Id;
        }

        // Cerrar el pago es sacar plata de la caja: queda como gasto de salario a nombre del
        // técnico, sin que nadie lo teclee aparte. Así el estado de resultados del periodo
        // cuadra con lo que de verdad se pagó, y no con lo que las tarifas decían.
        if (!esFacturacion && total > 0)
        {
            var nombre = (await db.UsersInTenant.AsNoTracking()
                .Where(u => u.Id == request.TechnicianUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct)) ?? "el técnico";

            var gasto = new Expense
            {
                BranchId = motos[0].BranchId,
                Category = ExpenseCategory.Salaries,
                Description =
                    $"Armado de motos · {nombre} · {request.From:dd/MM/yyyy} a {request.To:dd/MM/yyyy}",
                Amount = total,
                PaymentMethod = PaymentMethod.Cash,
                SupplierName = nombre,
                EmployeeUserId = request.TechnicianUserId,
                ExpenseDate = clock.UtcNow,
                Notes = $"{motos.Count} motos. Generado al cerrar el corte de pago."
            };

            db.Expenses.Add(gasto);
            corte.ExpenseId = gasto.Id;
        }

        await db.SaveChangesAsync(ct);

        var cortes = await SettlementsAsync(ct);

        return cortes.First(c => c.Id == corte.Id);
    }

    /// <summary>
    /// Anotar que el contratista pagó esa factura.
    /// </summary>
    /// <remarks>
    /// Cerrar el corte es facturar; esto es cobrar, y entre las dos cosas pasan semanas. Sin
    /// separarlas, un corte cerrado parecía plata entrada y no lo era.
    /// </remarks>
    public async Task<AssemblySettlementDto> CollectAsync(
        Guid id, CollectSettlementRequest request, CancellationToken ct = default)
    {
        var corte = await ParaCobrarAsync(id, ct);

        var dia = request.On ?? clock.Today();

        if (dia > clock.Today())
            throw new AppException("No se puede anotar un pago con fecha de mañana.");

        if (dia < corte.PeriodFrom)
            throw new AppException("El pago no puede ser anterior a las motos que paga.");

        corte.CollectedOn = dia;
        corte.CollectedReference = request.Reference?.Trim();

        await db.SaveChangesAsync(ct);

        return (await SettlementsAsync(ct)).First(c => c.Id == id);
    }

    public async Task<AssemblySettlementDto> UncollectAsync(
        Guid id, CancellationToken ct = default)
    {
        var corte = await ParaCobrarAsync(id, ct);

        corte.CollectedOn = null;
        corte.CollectedReference = null;

        await db.SaveChangesAsync(ct);

        return (await SettlementsAsync(ct)).First(c => c.Id == id);
    }

    private async Task<AssemblySettlement> ParaCobrarAsync(Guid id, CancellationToken ct)
    {
        await EnsureEnabledAsync(ct);
        EnsureManda().EnsureOwner();

        var corte = await db.AssemblySettlements.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("El corte no existe.");

        // En un corte de pago, cerrar ya es entregar la plata: la hoja se firma en el momento
        // y no hay nada que quedarse esperando.
        if (corte.Kind != AssemblySettlementKind.Billing)
            throw new AppException("Un corte de pago no se cobra: al cerrarlo ya se pagó.");

        return corte;
    }

    public async Task ReopenAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        var scope = EnsureManda();
        scope.EnsureOwner();

        var corte = await db.AssemblySettlements.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("El corte no existe.");

        // Una factura ya cobrada no se reabre de un clic: del otro lado entró plata contra
        // ese papel. Si de verdad hay que corregirla, primero se quita el cobro, que obliga a
        // mirar lo que se está deshaciendo.
        if (corte.Cobrado)
            throw new ConflictException(
                "Ese corte ya está cobrado. Quítele el cobro antes de reabrirlo.");

        var motos = corte.Kind == AssemblySettlementKind.Billing
            ? await db.MotorcycleAssemblies.Where(m => m.BillingSettlementId == id).ToListAsync(ct)
            : await db.MotorcycleAssemblies.Where(m => m.PaySettlementId == id).ToListAsync(ct);

        foreach (var moto in motos)
        {
            if (corte.Kind == AssemblySettlementKind.Billing) moto.BillingSettlementId = null;
            else moto.PaySettlementId = null;
        }

        // El gasto que generó se va con él: si no, quedaría restando una plata que ya no se
        // pagó, y el dueño lo buscaría en Gastos sin saber de dónde salió.
        if (corte.ExpenseId is { } gastoId)
        {
            var gasto = await db.Expenses.FirstOrDefaultAsync(e => e.Id == gastoId, ct);
            if (gasto is not null) db.Expenses.Remove(gasto);
        }

        db.AssemblySettlements.Remove(corte);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AssemblyNameDto>> TechniciansAsync(
        CancellationToken ct = default)
    {
        await EnsureEnabledAsync(ct);
        EnsureManda();

        // Quiénes han armado, no quiénes son técnicos: a quien nunca armó una moto no hay
        // nada que pagarle, y en el filtro sería ruido.
        var ids = await db.MotorcycleAssemblies.AsNoTracking()
            .Select(m => m.TechnicianUserId)
            .Distinct()
            .ToListAsync(ct);

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new AssemblyNameDto(u.Id, u.FullName))
            .ToListAsync(ct);
    }

    /// <summary>El filtro de la lista. Lo comparten la página, los totales y los reportes.</summary>
    internal IQueryable<MotorcycleAssembly> Filtrar(AssemblyQuery query, AccessScope scope)
    {
        var q = db.MotorcycleAssemblies.AsNoTracking();

        // El Encargado ve lo de sus sucursales. El reporte se corta por sucursal, así que
        // esto no es cosmético.
        if (scope.BranchFilter() is { } sucursales)
            q = q.Where(m => sucursales.Contains(m.BranchId));

        if (query.From is { } desde) q = q.Where(m => m.AssembledOn >= desde);
        if (query.To is { } hasta) q = q.Where(m => m.AssembledOn <= hasta);
        if (query.ContractorId is { } c) q = q.Where(m => m.ContractorId == c);
        if (query.TechnicianUserId is { } t) q = q.Where(m => m.TechnicianUserId == t);
        if (query.WarehouseId is { } w) q = q.Where(m => m.WarehouseId == w);
        if (query.ModelId is { } mo) q = q.Where(m => m.ModelId == mo);
        // Las de un corte: el id es único, así que da igual de qué tipo sea el corte.
        if (query.SettlementId is { } corte)
            q = q.Where(m => m.BillingSettlementId == corte || m.PaySettlementId == corte);

        if (query.OnlyUnbilled) q = q.Where(m => m.BillingSettlementId == null);
        if (query.OnlyUnpaid) q = q.Where(m => m.PaySettlementId == null);

        if (!string.IsNullOrWhiteSpace(query.Serial))
        {
            var serie = VinFormatter.Normalize(query.Serial);
            if (serie.Length >= 3) q = q.Where(m => m.Serial.Contains(serie));
        }

        return q;
    }

    internal async Task<Dictionary<Guid, string>> NombresAsync(
        IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();

        return await db.Users.AsNoTracking()
            .Where(u => lista.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private static void EnsureNoCongelada(MotorcycleAssembly moto)
    {
        if (!moto.Congelada) return;

        throw new ConflictException(
            moto.BillingSettlementId is not null
                ? "Esta moto ya se facturó. Reabra el corte si de verdad hay que corregirla."
                : "Esta moto ya se pagó. Reabra el corte si de verdad hay que corregirla.");
    }

    private AccessScope EnsureManda()
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureManagement();
        return scope;
    }

    private async Task EnsureEnabledAsync(CancellationToken ct)
    {
        var encendido = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantContext.TenantId)
            .Select(t => t.AssemblyEnabled)
            .FirstOrDefaultAsync(ct);

        if (!encendido) throw new NotFoundException();
    }
}
