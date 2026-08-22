using Garaj.Application.Abstractions;
using Garaj.Application.Common;
using Garaj.Application.Media;
using Garaj.Application.Notifications;
using Garaj.Application.Quotes;
using Garaj.Application.Tenants;
using Garaj.Domain.Entities;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Documents;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Garaj.Infrastructure.Services;

public class QuoteService(
    GarajDbContext db,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    IConfiguration configuration,
    ITenantService tenants,
    IMediaService media,
    INotificationPublisher notifications) : IQuoteService
{
    /// <summary>
    /// Cuántas fotos entran en el PDF. Es un documento que viaja por WhatsApp: con las
    /// primeras se entiende el daño, y con veinte no lo abre nadie.
    /// </summary>
    private const int PhotosInPdf = 6;

    public async Task<PagedResult<QuoteListItemDto>> ListAsync(
        QuoteQuery query, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        var q = Scoped(scope);

        if (query.Status is { } status) q = q.Where(x => x.Status == status);
        if (query.CustomerId is { } customerId) q = q.Where(x => x.CustomerId == customerId);
        if (query.WorkOrderId is { } workOrderId) q = q.Where(x => x.WorkOrderId == workOrderId);
        if (query.From is { } from) q = q.Where(x => x.CreatedAt >= from);
        if (query.To is { } to) q = q.Where(x => x.CreatedAt <= to);

        if (query.BranchId is { } branchId)
        {
            scope.EnsureBranchAllowed(branchId);
            q = q.Where(x => x.BranchId == branchId);
        }

        var total = await q.CountAsync(ct);
        var now = clock.UtcNow;

        var items = await q
            .OrderByDescending(x => x.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(x => new QuoteListItemDto(
                x.Id,
                x.Number,
                x.Status,
                x.BranchId,
                x.Branch.Name,
                x.CustomerId,
                x.Customer.FullName,
                x.Customer.Phone,
                db.Vehicles.Where(v => v.Id == x.VehicleId)
                    .Select(v => v.Brand + " " + v.Model)
                    .FirstOrDefault(),
                x.WorkOrderId,
                db.WorkOrders.Where(w => w.Id == x.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
                x.Total,
                x.ValidUntil,
                x.SentAt,
                x.RespondedAt,
                x.CreatedAt,
                x.ValidUntil != null && x.ValidUntil < now && x.RespondedAt == null))
            .ToListAsync(ct);

        return new PagedResult<QuoteListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<QuoteDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var quote = await Scoped(scope)
            .Include(q => q.Branch)
            .Include(q => q.Customer)
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == id, ct)
            ?? throw new NotFoundException("La cotización no existe.");

        return await MapAsync(quote, ct);
    }

    public async Task<QuoteDetailDto> CreateAsync(
        CreateQuoteRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new NotFoundException("El cliente no existe.");

        var branchId = await ResolveBranchAsync(request.BranchId, request.WorkOrderId, ct);
        var quote = new Quote
        {
            CustomerId = customer.Id,
            VehicleId = request.VehicleId,
            BranchId = branchId,
            ServiceRequestId = request.ServiceRequestId,
            WorkOrderId = request.WorkOrderId,
            Number = await NextNumberAsync(branchId, ct),
            Status = QuoteStatus.Draft,
            ValidUntil = request.ValidUntil?.ToUniversalTime() ?? clock.UtcNow.AddDays(15),
            Notes = request.Notes?.Trim(),
            TaxRate = request.TaxRate ?? SinImpuesto
        };

        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);

        return await GetAsync(quote.Id, ct);
    }

    /// <summary>
    /// La cotización nace sin ISV. Al cotizar nadie sabe todavía si el cliente va a pedir
    /// factura con CAI, y el impuesto solo lo lleva esa factura: cargarlo por adelantado
    /// infla el presupuesto un 15% frente a lo que la mayoría termina pagando. Cuando sí se
    /// va a facturar, se le pone la tasa a esa cotización (<c>PUT /api/quotes/{id}</c>).
    /// </summary>
    private const decimal SinImpuesto = 0m;

    public async Task<QuoteDetailDto> CreateFromWorkOrderAsync(
        QuoteFromWorkOrderRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var order = await db.WorkOrders
            .Include(w => w.Vehicle)
            .Include(w => w.Tasks)
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, ct)
            ?? throw new NotFoundException("La orden de trabajo no existe.");

        var quote = new Quote
        {
            CustomerId = order.Vehicle.CustomerId,
            VehicleId = order.VehicleId,
            BranchId = order.BranchId,
            WorkOrderId = order.Id,
            ServiceRequestId = order.ServiceRequestId,
            Number = await NextNumberAsync(order.BranchId, ct),
            Status = QuoteStatus.Draft,
            ValidUntil = request.ValidUntil?.ToUniversalTime() ?? clock.UtcNow.AddDays(15),
            Notes = request.Notes?.Trim() ?? order.Description,
            TaxRate = SinImpuesto
        };

        db.Quotes.Add(quote);

        var sequence = 0;

        if (request.IncludeParts)
        {
            var parts = await db.WorkOrderParts.AsNoTracking()
                .Where(p => p.WorkOrderId == order.Id)
                .Select(p => new
                {
                    p.PartId,
                    // Los cargados a mano no tienen catálogo del que sacar nombre y código:
                    // van con lo que escribió quien los cargó.
                    Name = p.Part != null ? $"{p.Part.Name} ({p.Part.Sku})" : p.Description!,
                    p.Quantity,
                    p.UnitPrice
                })
                .ToListAsync(ct);

            foreach (var part in parts)
            {
                db.QuoteLines.Add(new QuoteLine
                {
                    QuoteId = quote.Id,
                    LineType = LineType.Part,
                    PartId = part.PartId,
                    Description = part.Name,
                    Sequence = ++sequence,
                    Quantity = part.Quantity,
                    UnitPrice = part.UnitPrice,
                    Total = part.Quantity * part.UnitPrice
                });
            }
        }

        // En modo manual la mano de obra va como una sola línea, igual que en la factura: la
        // cotización tiene que enseñar el mismo precio que después se cobra.
        if (request.IncludeTasks && order.LaborMode == LaborMode.Manual)
        {
            if (order.ManualLaborTotal is { } manual && manual > 0)
            {
                db.QuoteLines.Add(new QuoteLine
                {
                    QuoteId = quote.Id,
                    LineType = LineType.Labor,
                    Description = "Mano de obra",
                    Sequence = ++sequence,
                    Quantity = 1,
                    UnitPrice = manual,
                    Total = manual
                });
            }
        }
        else if (request.IncludeTasks)
        {
            // Solo los pasos con servicio del catálogo: un paso suelto no tiene precio, y
            // meterlo en cero haría que la cotización parezca completa cuando no lo está.
            var laborIds = order.Tasks.Where(t => t.LaborServiceId != null)
                .Select(t => t.LaborServiceId!.Value)
                .Distinct()
                .ToList();

            var services = await db.LaborServices.AsNoTracking()
                .Where(s => laborIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

            foreach (var task in order.Tasks.OrderBy(t => t.Sequence))
            {
                var service = task.LaborServiceId is { } serviceId
                    ? services.GetValueOrDefault(serviceId)
                    : null;

                if (task.PriceWith(service) is not { } price || price <= 0) continue;

                db.QuoteLines.Add(new QuoteLine
                {
                    QuoteId = quote.Id,
                    LineType = LineType.Labor,
                    LaborServiceId = service?.Id,
                    Description = task.Title,
                    Sequence = ++sequence,
                    Quantity = 1,
                    UnitPrice = price,
                    Total = price
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await RecalculateAsync(quote.Id, ct);

        return await GetAsync(quote.Id, ct);
    }

    public async Task<QuoteDetailDto> UpdateAsync(
        Guid id, UpdateQuoteRequest request, CancellationToken ct = default)
    {
        var quote = await FindEditableAsync(id, ct);

        quote.ValidUntil = request.ValidUntil?.ToUniversalTime() ?? quote.ValidUntil;
        quote.Notes = request.Notes?.Trim();
        if (request.TaxRate is { } rate) quote.TaxRate = rate;

        await db.SaveChangesAsync(ct);
        await RecalculateAsync(id, ct);

        return await GetAsync(id, ct);
    }

    public async Task<QuoteDetailDto> AddLineAsync(
        Guid id, SaveQuoteLineRequest request, CancellationToken ct = default)
    {
        var quote = await FindEditableAsync(id, ct);

        var line = new QuoteLine
        {
            QuoteId = quote.Id,
            Sequence = await db.QuoteLines.Where(l => l.QuoteId == id)
                .MaxAsync(l => (int?)l.Sequence, ct) is { } last ? last + 1 : 1
        };

        await ApplyAsync(line, request, ct);

        db.QuoteLines.Add(line);
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(id, ct);

        return await GetAsync(id, ct);
    }

    public async Task<QuoteDetailDto> UpdateLineAsync(
        Guid id, Guid lineId, SaveQuoteLineRequest request, CancellationToken ct = default)
    {
        await FindEditableAsync(id, ct);

        var line = await db.QuoteLines.FirstOrDefaultAsync(l => l.Id == lineId && l.QuoteId == id, ct)
            ?? throw new NotFoundException("La línea no existe en esta cotización.");

        await ApplyAsync(line, request, ct);

        await db.SaveChangesAsync(ct);
        await RecalculateAsync(id, ct);

        return await GetAsync(id, ct);
    }

    public async Task<QuoteDetailDto> RemoveLineAsync(
        Guid id, Guid lineId, CancellationToken ct = default)
    {
        await FindEditableAsync(id, ct);

        var line = await db.QuoteLines.FirstOrDefaultAsync(l => l.Id == lineId && l.QuoteId == id, ct)
            ?? throw new NotFoundException("La línea no existe en esta cotización.");

        db.QuoteLines.Remove(line);
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(id, ct);

        return await GetAsync(id, ct);
    }

    public async Task<WhatsAppLinkDto> SendAsync(Guid id, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);
        scope.EnsureOwner();

        var quote = await db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, ct)
            ?? throw new NotFoundException("La cotización no existe.");

        if (quote.Lines.Count == 0)
            throw new AppException("La cotización no tiene líneas: no hay nada que enviar.");

        if (quote.Status is QuoteStatus.Approved or QuoteStatus.Rejected)
            throw new ConflictException("El cliente ya respondió esta cotización.");

        // Reenviar una ya enviada es normal —el cliente no contestó— y no debe resetear la
        // fecha de envío original, que es lo que se usa para saber cuánto lleva esperando.
        if (quote.Status == QuoteStatus.Draft)
        {
            quote.Status = QuoteStatus.Sent;
            quote.SentAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        // El canal de verdad es el WhatsApp que el Dueño está a punto de enviar. Esto es el
        // respaldo: si el cliente usa la app, la cotización le aparece ahí sin buscar el chat.
        await notifications.NotifyCustomerAsync(quote.TenantId, quote.CustomerId, new NotificationDraft(
            NotificationType.QuoteSent,
            $"Cotización {quote.Number}",
            $"Su cotización por {quote.Total:N2} está lista para revisar.",
            QuoteId: quote.Id,
            WorkOrderId: quote.WorkOrderId), ct);

        return await BuildLinkAsync(quote.Id, ct);
    }

    public async Task<WhatsAppLinkDto> WhatsAppLinkAsync(Guid id, CancellationToken ct = default)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        if (!await db.Quotes.AnyAsync(q => q.Id == id, ct))
            throw new NotFoundException("La cotización no existe.");

        return await BuildLinkAsync(id, ct);
    }

    public async Task<byte[]> PdfAsync(Guid id, CancellationToken ct = default)
    {
        var detail = await GetAsync(id, ct);
        var tenant = await CurrentTenantAsync(ct);
        var logo = await tenants.TryGetLogoBytesAsync(tenant.Id, ct);
        var photos = await media.DownloadThumbnailsAsync(
            MediaOwnerType.Quote, id, tenant.Id, PhotosInPdf, ct);

        return QuotePdf.Render(
            detail, tenant.Name, tenant.LegalName, tenant.Phone, tenant.TaxId, logo, photos);
    }

    public async Task<QuoteDetailDto> RespondAsync(
        Guid id, RespondToQuoteRequest request, CancellationToken ct = default)
    {
        var scope = AccessScope.From(tenantContext);

        var quote = await Scoped(scope).FirstOrDefaultAsync(q => q.Id == id, ct)
            ?? throw new NotFoundException("La cotización no existe.");

        ApplyResponse(quote, request);
        await db.SaveChangesAsync(ct);
        await PropagateApprovalAsync(quote, ct);

        return await GetAsync(id, ct);
    }

    // ---------- Sin autenticación ----------

    public async Task<PublicQuoteDto> GetPublicAsync(Guid token, CancellationToken ct = default)
    {
        var quote = await FindByTokenAsync(token, ct);
        return await MapPublicAsync(quote, ct);
    }

    public async Task<PublicQuoteDto> RespondPublicAsync(
        Guid token, RespondToQuoteRequest request, CancellationToken ct = default)
    {
        var quote = await FindByTokenAsync(token, ct);

        ApplyResponse(quote, request);

        // El filtro global no aplica en esta consulta, así que el tenant se fija a mano
        // antes de guardar: lo necesitan el interceptor de auditoría y la propagación.
        tenantContext.SetTenant(quote.TenantId);
        await db.SaveChangesAsync(ct);
        await PropagateApprovalAsync(quote, ct);

        return await MapPublicAsync(quote, ct);
    }

    public async Task<byte[]> PdfPublicAsync(Guid token, CancellationToken ct = default)
    {
        var quote = await FindByTokenAsync(token, ct);
        tenantContext.SetTenant(quote.TenantId);

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == quote.TenantId, ct);

        var detail = await MapAsync(
            await db.Quotes.IgnoreQueryFilters()
                .Include(q => q.Branch).Include(q => q.Customer).Include(q => q.Lines)
                .FirstAsync(q => q.Id == quote.Id, ct),
            ct);

        var logo = await tenants.TryGetLogoBytesAsync(tenant.Id, ct);
        var photos = await media.DownloadThumbnailsAsync(
            MediaOwnerType.Quote, quote.Id, quote.TenantId, PhotosInPdf, ct);

        return QuotePdf.Render(
            detail, tenant.Name, tenant.LegalName, tenant.Phone, tenant.TaxId, logo, photos);
    }

    public async Task<TenantLogo?> LogoPublicAsync(Guid token, CancellationToken ct = default)
    {
        var quote = await FindByTokenAsync(token, ct);
        return await tenants.GetLogoAsync(quote.TenantId, ct);
    }

    // ---------- Interno ----------

    private IQueryable<Quote> Scoped(AccessScope scope)
    {
        var q = db.Quotes.AsNoTracking();

        // El cliente ve sus cotizaciones; el técnico no participa en la parte comercial.
        if (scope.IsCustomer) return q.Where(x => x.CustomerId == scope.CustomerId);
        if (scope.IsTechnician) return q.Where(_ => false);

        return q;
    }

    private async Task<Quote> FindEditableAsync(Guid id, CancellationToken ct)
    {
        AccessScope.From(tenantContext).EnsureOwner();

        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct)
            ?? throw new NotFoundException("La cotización no existe.");

        // Una cotización respondida es un documento cerrado: si hay que cambiar el precio,
        // se hace otra. Si no, el cliente aprobaría una cosa y recibiría otra.
        if (quote.Status is QuoteStatus.Approved or QuoteStatus.Rejected)
            throw new ConflictException(
                "La cotización ya fue respondida por el cliente. Cree una nueva para cambiarla.");

        return quote;
    }

    /// <summary>
    /// La consulta del link público corre sin filtro de tenant a propósito: el visitante es
    /// anónimo y no hay claims de los que sacar el taller. El token es la credencial.
    /// </summary>
    private async Task<Quote> FindByTokenAsync(Guid token, CancellationToken ct) =>
        await db.Quotes.IgnoreQueryFilters()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.PublicToken == token, ct)
        ?? throw new NotFoundException("La cotización no existe o el enlace ya no es válido.");

    private void ApplyResponse(Quote quote, RespondToQuoteRequest request)
    {
        if (quote.Status == QuoteStatus.Draft)
            throw new ConflictException("La cotización todavía no se ha enviado.");

        if (quote.RespondedAt is not null)
            throw new ConflictException("Esta cotización ya fue respondida.");

        if (request.Approve && IsExpired(quote))
            throw new ConflictException(
                "La cotización venció. Pida al taller que le envíe una actualizada.");

        quote.Status = request.Approve ? QuoteStatus.Approved : QuoteStatus.Rejected;
        quote.RespondedAt = clock.UtcNow;
        quote.CustomerResponseNote = request.Note?.Trim() is { Length: > 0 } note
            ? note[..Math.Min(note.Length, 1000)]
            : null;
    }

    /// <summary>
    /// Una aprobación tiene que verse donde el taller trabaja, no solo en la cotización: el
    /// requerimiento pasa a aprobado y la orden deja constancia en su línea de tiempo.
    /// </summary>
    private async Task PropagateApprovalAsync(Quote quote, CancellationToken ct)
    {
        var approved = quote.Status == QuoteStatus.Approved;

        if (quote.ServiceRequestId is { } requestId)
        {
            var request = await db.ServiceRequests.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == requestId, ct);

            if (request is not null && request.Status is ServiceRequestStatus.Pending or ServiceRequestStatus.Quoted)
            {
                request.Status = approved ? ServiceRequestStatus.Approved : ServiceRequestStatus.Rejected;
                if (!approved) request.RejectionReason = quote.CustomerResponseNote ?? "Cotización rechazada.";
            }
        }

        if (quote.WorkOrderId is { } workOrderId)
        {
            var order = await db.WorkOrders.IgnoreQueryFilters()
                .FirstOrDefaultAsync(w => w.Id == workOrderId, ct);

            if (order is not null)
            {
                db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
                {
                    TenantId = order.TenantId,
                    WorkOrderId = order.Id,
                    FromStatus = order.Status,
                    ToStatus = order.Status,
                    ChangedAt = clock.UtcNow,
                    // Quien responde puede ser anónimo desde el link público; en ese caso se
                    // registra a nombre de quien creó la cotización.
                    ChangedByUserId = tenantContext.UserId ?? quote.CreatedByUserId ?? Guid.Empty,
                    Note = approved
                        ? $"El cliente aprobó la cotización {quote.Number}."
                        : $"El cliente rechazó la cotización {quote.Number}."
                          + (quote.CustomerResponseNote is { } n ? $" «{n}»" : ""),
                    IsVisibleToCustomer = true
                });
            }
        }

        await db.SaveChangesAsync(ct);

        // Aquí llegan también las respuestas anónimas desde el link de WhatsApp, y esas son
        // justamente las que el Dueño no puede ver venir: nadie le avisa por otro medio.
        var customerName = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.Id == quote.CustomerId)
            .Select(c => c.FullName)
            .FirstAsync(ct);

        await notifications.NotifyOwnersAsync(quote.TenantId, new NotificationDraft(
            NotificationType.QuoteAnswered,
            approved ? $"Cotización {quote.Number} aprobada" : $"Cotización {quote.Number} rechazada",
            $"{customerName} {(approved ? "aprobó" : "rechazó")} por {quote.Total:N2}."
            + (quote.CustomerResponseNote is { } note ? $" «{note}»" : ""),
            QuoteId: quote.Id,
            WorkOrderId: quote.WorkOrderId), ct);
    }

    private async Task ApplyAsync(QuoteLine line, SaveQuoteLineRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            throw new AppException("La cantidad debe ser mayor que cero.");

        line.LineType = request.LineType;
        line.Quantity = request.Quantity;
        line.Discount = request.Discount;

        if (request.LineType == LineType.Part)
        {
            var part = request.PartId is { } partId
                ? await db.Parts.FirstOrDefaultAsync(p => p.Id == partId, ct)
                    ?? throw new NotFoundException("El repuesto no existe.")
                : null;

            line.PartId = part?.Id;
            line.LaborServiceId = null;
            line.Description = Describe(request.Description, part is null ? null : $"{part.Name} ({part.Sku})");
            line.UnitPrice = request.UnitPrice ?? part?.SalePrice ?? 0;
        }
        else
        {
            var service = request.LaborServiceId is { } serviceId
                ? await db.LaborServices.FirstOrDefaultAsync(s => s.Id == serviceId, ct)
                    ?? throw new NotFoundException("El servicio no existe.")
                : null;

            line.LaborServiceId = service?.Id;
            line.PartId = null;
            line.Description = Describe(request.Description, service?.Name);
            line.UnitPrice = request.UnitPrice
                ?? service?.PriceFor(null) ?? 0;
        }

        line.Total = Math.Max(0, line.Quantity * line.UnitPrice - line.Discount);
    }

    private static string Describe(string? given, string? fromCatalog) =>
        given?.Trim() is { Length: > 0 } text
            ? text[..Math.Min(text.Length, 500)]
            : fromCatalog ?? throw new AppException("La línea necesita una descripción.");

    /// <summary>
    /// Los totales se recalculan siempre en el servidor a partir de las líneas. Nunca se
    /// aceptan del cliente: es el importe que el cliente aprueba y que después se cobra.
    /// </summary>
    private async Task RecalculateAsync(Guid quoteId, CancellationToken ct)
    {
        var quote = await db.Quotes.Include(q => q.Lines).FirstAsync(q => q.Id == quoteId, ct);

        quote.Subtotal = quote.Lines.Sum(l => l.Quantity * l.UnitPrice);
        quote.DiscountTotal = quote.Lines.Sum(l => l.Discount);

        var taxable = quote.Subtotal - quote.DiscountTotal;
        quote.TaxTotal = Math.Round(taxable * quote.TaxRate / 100m, 2);
        quote.Total = taxable + quote.TaxTotal;

        await db.SaveChangesAsync(ct);
    }

    private async Task<WhatsAppLinkDto> BuildLinkAsync(Guid id, CancellationToken ct)
    {
        var quote = await db.Quotes.AsNoTracking()
            .Include(q => q.Customer)
            .FirstAsync(q => q.Id == id, ct);

        var tenant = await CurrentTenantAsync(ct);
        var url = PublicUrlFor(quote.PublicToken);

        var vehicle = quote.VehicleId is { } vehicleId
            ? await db.Vehicles.Where(v => v.Id == vehicleId)
                .Select(v => v.Brand + " " + v.Model + (v.Plate == null ? "" : " (" + v.Plate + ")"))
                .FirstOrDefaultAsync(ct)
            : null;

        // El mensaje va armado para que el dueño no teclee nada: solo toca enviar.
        var message =
            $"Hola {quote.Customer.FullName}, le saluda {tenant.Name}. "
            + (vehicle is null ? "" : $"Sobre su {vehicle}: ")
            + $"le enviamos la cotización {quote.Number} por {tenant.Currency} {quote.Total:N2}"
            + (quote.ValidUntil is { } valid ? $", válida hasta el {valid:dd/MM/yyyy}" : "")
            + $".\n\nPuede verla y aprobarla aquí:\n{url}";

        return new WhatsAppLinkDto(
            $"https://wa.me/{quote.Customer.Phone}?text={Uri.EscapeDataString(message)}",
            quote.Customer.Phone,
            message);
    }

    /// <summary>
    /// Apunta al web, no a la API: el cliente abre una página, no un JSON. Sale de
    /// <c>PublicBaseUrl</c>, que en producción es el dominio de Cloudflare Pages.
    /// </summary>
    private string PublicUrlFor(Guid token)
    {
        var baseUrl = configuration["PublicBaseUrl"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new AppException(
                "Falta configurar PublicBaseUrl: sin ella el link de la cotización no lleva a ningún lado.");

        return $"{baseUrl}/q/{token}";
    }

    private async Task<Guid> ResolveBranchAsync(Guid? branchId, Guid? workOrderId, CancellationToken ct)
    {
        if (branchId is { } id)
        {
            if (!await db.Branches.AnyAsync(b => b.Id == id, ct))
                throw new NotFoundException("La sucursal no existe.");
            return id;
        }

        if (workOrderId is { } orderId)
        {
            var fromOrder = await db.WorkOrders.Where(w => w.Id == orderId)
                .Select(w => (Guid?)w.BranchId)
                .FirstOrDefaultAsync(ct);

            if (fromOrder is { } resolved) return resolved;
        }

        return await db.Branches.Where(b => b.IsActive).Select(b => b.Id).FirstOrDefaultAsync(ct) is { } first
            && first != Guid.Empty
                ? first
                : throw new AppException("El taller no tiene sucursales activas.");
    }

    /// <summary>Correlativo por sucursal, ej. "COT-MTZ-000012".</summary>
    private async Task<string> NextNumberAsync(Guid branchId, CancellationToken ct)
    {
        var branch = await db.Branches.FirstAsync(b => b.Id == branchId, ct);
        branch.QuoteSequence++;

        var prefix = string.IsNullOrEmpty(branch.Code) ? "COT" : $"COT-{branch.Code}";
        return $"{prefix}-{branch.QuoteSequence:D6}";
    }

    private async Task<Tenant> CurrentTenantAsync(CancellationToken ct) =>
        await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
        ?? throw new NotFoundException("El taller no existe.");

    private bool IsExpired(Quote quote) =>
        quote.ValidUntil is { } valid && valid < clock.UtcNow && quote.RespondedAt is null;

    private async Task<QuoteDetailDto> MapAsync(Quote quote, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(t => t.Id == quote.TenantId, ct);

        var vehicle = quote.VehicleId is { } vehicleId
            ? await db.Vehicles.AsNoTracking().IgnoreQueryFilters()
                .Where(v => v.Id == vehicleId)
                .Select(v => new { Label = v.Brand + " " + v.Model, v.Plate })
                .FirstOrDefaultAsync(ct)
            : null;

        var workOrderNumber = quote.WorkOrderId is { } orderId
            ? await db.WorkOrders.AsNoTracking().IgnoreQueryFilters()
                .Where(w => w.Id == orderId).Select(w => w.Number).FirstOrDefaultAsync(ct)
            : null;

        var editable = quote.Status is QuoteStatus.Draft or QuoteStatus.Sent or QuoteStatus.Expired;

        return new QuoteDetailDto(
            quote.Id,
            quote.Number,
            quote.Status,
            quote.BranchId,
            quote.Branch.Name,
            quote.CustomerId,
            quote.Customer.FullName,
            quote.Customer.Phone,
            quote.VehicleId,
            vehicle?.Label,
            vehicle?.Plate,
            quote.ServiceRequestId,
            quote.WorkOrderId,
            workOrderNumber,
            quote.Notes,
            quote.Subtotal,
            quote.DiscountTotal,
            quote.TaxRate,
            quote.TaxTotal,
            quote.Total,
            tenant.Currency,
            quote.ValidUntil,
            quote.SentAt,
            quote.RespondedAt,
            quote.CustomerResponseNote,
            quote.CreatedAt,
            IsExpired(quote),
            editable,
            // El link solo tiene sentido una vez enviada: antes, compartirlo sería enseñar
            // un borrador.
            quote.Status == QuoteStatus.Draft ? null : PublicUrlFor(quote.PublicToken),
            quote.Lines
                .OrderBy(l => l.Sequence)
                .Select(l => new QuoteLineDto(
                    l.Id, l.LineType, l.PartId, l.LaborServiceId, l.Description,
                    l.Sequence, l.Quantity, l.UnitPrice, l.Discount, l.Total))
                .ToList());
    }

    private async Task<PublicQuoteDto> MapPublicAsync(Quote quote, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(t => t.Id == quote.TenantId, ct);

        var branch = await db.Branches.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(b => b.Id == quote.BranchId, ct);

        var customer = await db.Customers.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(c => c.Id == quote.CustomerId, ct);

        var vehicle = quote.VehicleId is { } vehicleId
            ? await db.Vehicles.AsNoTracking().IgnoreQueryFilters()
                .Where(v => v.Id == vehicleId)
                .Select(v => new { Label = v.Brand + " " + v.Model, v.Plate })
                .FirstOrDefaultAsync(ct)
            : null;

        var photos = (await media.ListForQuotePublicAsync(quote.TenantId, quote.Id, ct))
            .Select(p => new PublicQuotePhotoDto(p.Url, p.ThumbnailUrl, p.Caption))
            .ToList();

        return new PublicQuoteDto(
            quote.Number,
            quote.Status,
            tenant.Name,
            // Bajo el token de la cotización y no bajo el id del taller: en esta página el
            // token es la única credencial y no se filtra ningún id interno.
            tenant.LogoStorageKey is null ? null : $"/public/quotes/{quote.PublicToken}/logo",
            tenant.Phone,
            branch.Name,
            customer.FullName,
            vehicle?.Label,
            vehicle?.Plate,
            quote.Notes,
            quote.Subtotal,
            quote.DiscountTotal,
            quote.TaxRate,
            quote.TaxTotal,
            quote.Total,
            tenant.Currency,
            quote.ValidUntil,
            quote.RespondedAt,
            IsExpired(quote),
            quote.Status == QuoteStatus.Sent && quote.RespondedAt is null && !IsExpired(quote),
            quote.Lines
                .OrderBy(l => l.Sequence)
                .Select(l => new PublicQuoteLineDto(
                    l.LineType, l.Description, l.Quantity, l.UnitPrice, l.Discount, l.Total))
                .ToList(),
            photos);
    }
}
