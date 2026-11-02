using Garaj.Application.Abstractions;
using Garaj.Application.Notifications;
using Garaj.Domain.Enums;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Garaj.Infrastructure.Jobs;

/// <summary>
/// Lo que avisa el calendario y no un clic: el servicio que toca, la orden que lleva días
/// parada, la cita de mañana y el saldo vencido.
/// </summary>
/// <remarks>
/// Hasta ahora todos los avisos nacían de algo que alguien hacía —asignar una orden, aprobar
/// una cotización—, así que no hacía falta nada corriendo solo. Estos cuatro no los provoca
/// nadie: pasan porque el día cambió. Por eso hay un repaso que mira una vez al día.
///
/// Corre de madrugada no: corre a la hora en que el taller abre, porque un aviso a las 3 de
/// la mañana se lee entre veinte más y no se actúa sobre él. Y corre una sola vez al día: lo
/// que se avisa aquí no es urgente —es una lista de pendientes— y sonar dos veces por lo
/// mismo enseña a no mirar la campana.
///
/// Lo que ya se avisó no se repite: cada aviso lleva una clave que describe el hecho, y
/// <see cref="Garaj.Domain.Entities.Notification.DedupeKey"/> explica por qué.
/// </remarks>
public class RepasoDiario(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<RepasoDiario> logger) : BackgroundService
{
    /// <summary>Honduras, UTC-6. La misma resta fija que usa el resto del sistema.</summary>
    private const int HusoHoras = -6;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!configuration.GetValue("Notifications:DailyPass", true))
        {
            logger.LogInformation("El repaso diario de avisos está apagado por configuración.");
            return;
        }

        var hora = configuration.GetValue("Notifications:DailyPassHour", 7);

        while (!ct.IsCancellationRequested)
        {
            var espera = HastaLaProxima(hora);
            logger.LogInformation("Próximo repaso de avisos en {Horas:0.#} h.", espera.TotalHours);

            try
            {
                await Task.Delay(espera, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await PasarAsync(ct);
            }
            catch (Exception ex)
            {
                // Que falle el repaso no puede tumbar el servicio: mañana se vuelve a intentar,
                // y mientras tanto el taller sigue trabajando sin enterarse.
                logger.LogError(ex, "Falló el repaso diario de avisos.");
            }
        }
    }

    /// <summary>Cuánto falta para la próxima hora de apertura en Honduras.</summary>
    private static TimeSpan HastaLaProxima(int hora)
    {
        var aqui = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(HusoHoras));

        var proxima = new DateTimeOffset(
            aqui.Year, aqui.Month, aqui.Day, hora, 0, 0, TimeSpan.FromHours(HusoHoras));

        if (proxima <= aqui) proxima = proxima.AddDays(1);

        return proxima - aqui;
    }

    private async Task PasarAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepasoDeAvisos>().PasarAsync(ct);
    }
}

/// <summary>
/// El repaso en sí: las cinco cosas que se miran una vez al día. Separado del planificador
/// para poder dispararlo a mano desde el panel de plataforma y ver el resultado sin esperar
/// a mañana.
/// </summary>
public class RepasoDeAvisos(
    GarajDbContext db,
    INotificationPublisher avisos,
    IDateTimeProvider clock,
    ITenantContext tenantContext) : IRepasoDeAvisos
{
    /// <summary>Honduras, UTC-6. La misma resta fija que usa el resto del sistema.</summary>
    private const int HusoHoras = -6;

    /// <summary>Cuántos días sin que nadie la toque convierten una orden en una orden parada.</summary>
    private const int DiasParaDarlaPorParada = 3;

    /// <summary>
    /// Hasta cuántos días atrás se avisa de un servicio que tocaba. Sin este tope, el primer
    /// repaso después de instalar esto desenterraría cada servicio pendiente desde que el
    /// taller abrió.
    /// </summary>
    private const int DiasDeGraciaDelServicio = 30;

    public async Task PasarAsync(CancellationToken ct = default)
    {
        // El repaso mira todos los talleres a la vez, así que no hay un tenant de la petición
        // del que colgarse. Cada consulta lleva el TenantId a mano y cada aviso se emite con
        // el taller al que pertenece el registro que lo provocó.
        tenantContext.BypassTenantFilter = true;

        // Un taller dado de baja no recibe avisos: ya no trabaja, y sus órdenes viejas
        // seguirían pareciendo paradas para siempre.
        var activos = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (activos.Count == 0) return;

        await ServiciosQueTocanAsync(activos, ct);
        await OrdenesParadasAsync(activos, ct);
        await TiemposPasadosAsync(activos, ct);
        await CitasDeManianaAsync(activos, ct);
        await SaldosVencidosAsync(activos, ct);
    }

    // ---------- El trabajo que se pasó de las horas ----------

    /// <summary>
    /// Lo mismo que avisa el cronómetro al pararse, para el que no lo para.
    /// </summary>
    /// <remarks>
    /// Al parar el cronómetro ya se compara contra lo presupuestado, pero el técnico que lo
    /// deja corriendo tres días no para nunca, y ese es justo el trabajo que el dueño quiere
    /// ver. La clave de deduplicación es la misma que usa el aviso del cronómetro, así que la
    /// orden que ya avisó allá no vuelve a avisar aquí.
    /// </remarks>
    private async Task TiemposPasadosAsync(
        IReadOnlyCollection<Guid> talleres, CancellationToken ct)
    {
        var abiertas = await db.WorkOrders.IgnoreQueryFilters()
            .Where(w => talleres.Contains(w.TenantId)
                        && w.Status != WorkOrderStatus.Delivered
                        && w.Status != WorkOrderStatus.Cancelled)
            .Select(w => new
            {
                w.Id,
                w.TenantId,
                w.BranchId,
                w.Number,
                Presupuesto = w.Tasks.Sum(t => (decimal?)(t.ActualHours ?? t.EstimatedHours)) ?? 0m,
                Activo = w.Vehicle != null
                    ? w.Vehicle.Brand + " " + w.Vehicle.Model
                    : (w.Equipment != null ? w.Equipment.Brand + " " + w.Equipment.Model : "")
            })
            .Where(w => w.Presupuesto > 0)
            .ToListAsync(ct);

        if (abiertas.Count == 0) return;

        var ids = abiertas.Select(w => w.Id).ToList();

        var tramos = await db.WorkOrderTimeEntries.IgnoreQueryFilters()
            .Where(t => ids.Contains(t.WorkOrderId))
            .ToListAsync(ct);

        var ahora = clock.UtcNow;

        var trabajado = tramos
            .GroupBy(t => t.WorkOrderId)
            .ToDictionary(g => g.Key, g => (int)g.Sum(t => t.Duracion(ahora).TotalMinutes));

        foreach (var orden in abiertas)
        {
            if (!trabajado.TryGetValue(orden.Id, out var minutos)) continue;

            var presupuesto = (int)(orden.Presupuesto * 60);
            if (minutos <= presupuesto) continue;

            await avisos.NotifyBranchManagementAsync(
                orden.TenantId,
                orden.BranchId,
                new NotificationDraft(
                    NotificationType.TimeOverBudget,
                    $"Orden {orden.Number} pasó del tiempo",
                    $"{orden.Activo}: {Horas(minutos)} trabajadas contra "
                    + $"{orden.Presupuesto:0.##} h presupuestadas, {Horas(minutos - presupuesto)} de más.",
                    WorkOrderId: orden.Id,
                    DedupeKey: $"tiempo:{orden.Id}"),
                ct);
        }
    }

    /// <summary>«2 h 35 min», que es como se dice en el taller.</summary>
    private static string Horas(int minutos)
    {
        if (minutos < 60) return $"{minutos} min";

        var horas = minutos / 60;
        var resto = minutos % 60;

        return resto == 0 ? $"{horas} h" : $"{horas} h {resto} min";
    }

    // ---------- El servicio que toca ----------

    /// <summary>
    /// Al dueño le llega un resumen —cuántos clientes— porque el WhatsApp lo manda él desde
    /// la pantalla de recordatorios, y una lista es lo que necesita para sentarse a mandarlos.
    /// Al cliente que tiene la app le llega el suyo, una sola vez.
    /// </summary>
    private async Task ServiciosQueTocanAsync(
        IReadOnlyCollection<Guid> talleres, CancellationToken ct)
    {
        var hoy = clock.Today();
        var hasta = clock.StartOfToday().AddDays(1);
        var desde = clock.StartOfToday().AddDays(-DiasDeGraciaDelServicio);

        var tocan = await db.WorkOrders.IgnoreQueryFilters()
            .Where(w => talleres.Contains(w.TenantId)
                        && w.NextServiceAt != null
                        && w.NextServiceAt < hasta
                        && w.NextServiceAt >= desde
                        // El que ya recibió su WhatsApp no vuelve a entrar: el taller ya lo
                        // llamó, y el aviso sería pedirle que lo llame otra vez.
                        && w.NextServiceRemindedAt == null)
            .Select(w => new
            {
                w.Id,
                w.TenantId,
                w.NextServiceAt,
                CustomerId = w.Vehicle != null ? w.Vehicle.CustomerId : w.Equipment!.CustomerId,
                Cliente = w.Vehicle != null ? w.Vehicle.Customer!.FullName : w.Equipment!.Customer!.FullName,
                Activo = w.Vehicle != null
                    ? w.Vehicle.Brand + " " + w.Vehicle.Model
                    : w.Equipment!.Brand + " " + w.Equipment.Model
            })
            .ToListAsync(ct);

        foreach (var porTaller in tocan.GroupBy(x => x.TenantId))
        {
            var lista = porTaller.ToList();

            var quienes = string.Join(", ", lista.Take(3).Select(x => x.Cliente));
            var resto = lista.Count - Math.Min(3, lista.Count);

            await avisos.NotifyOwnersAsync(
                porTaller.Key,
                new NotificationDraft(
                    NotificationType.ServiceReminderDue,
                    lista.Count == 1 ? "A un cliente le toca servicio" : $"A {lista.Count} clientes les toca servicio",
                    quienes + (resto > 0 ? $" y {resto} más." : ".")
                    + " Están en Recordatorios, listos para mandarles el mensaje.",
                    DedupeKey: $"servicio:{hoy:yyyy-MM-dd}"),
                ct);

            foreach (var orden in lista)
            {
                var cuando = orden.NextServiceAt!.Value.ToOffset(TimeSpan.FromHours(HusoHoras));

                await avisos.NotifyCustomerAsync(
                    porTaller.Key,
                    orden.CustomerId,
                    new NotificationDraft(
                        NotificationType.ServiceReminderDue,
                        "Le toca servicio",
                        $"A su {orden.Activo} le {(cuando.Date < DateTime.Today ? "tocaba" : "toca")} "
                        + $"servicio el {cuando:dd/MM}. ¿Le agendamos una cita?",
                        WorkOrderId: orden.Id,
                        DedupeKey: $"servicio:{orden.Id}"),
                    ct);
            }
        }
    }

    // ---------- La orden que nadie toca ----------

    private async Task OrdenesParadasAsync(
        IReadOnlyCollection<Guid> talleres, CancellationToken ct)
    {
        var limite = clock.StartOfToday().AddDays(-DiasParaDarlaPorParada);

        var paradas = await db.WorkOrders.IgnoreQueryFilters()
            .Where(w => talleres.Contains(w.TenantId)
                        && w.Status != WorkOrderStatus.Delivered
                        && w.Status != WorkOrderStatus.Cancelled
                        && (w.UpdatedAt ?? w.CreatedAt) < limite)
            .Select(w => new
            {
                w.Id,
                w.TenantId,
                w.BranchId,
                w.Number,
                w.Status,
                Movimiento = w.UpdatedAt ?? w.CreatedAt,
                Activo = w.Vehicle != null
                    ? w.Vehicle.Brand + " " + w.Vehicle.Model
                    : (w.Equipment != null ? w.Equipment.Brand + " " + w.Equipment.Model : "")
            })
            .ToListAsync(ct);

        foreach (var orden in paradas)
        {
            var dias = (int)(clock.UtcNow - orden.Movimiento).TotalDays;

            await avisos.NotifyBranchManagementAsync(
                orden.TenantId,
                orden.BranchId,
                new NotificationDraft(
                    NotificationType.WorkOrderStalled,
                    $"Orden {orden.Number} lleva {dias} días sin moverse",
                    $"{orden.Activo} · sigue en «{Describe(orden.Status)}».",
                    WorkOrderId: orden.Id,
                    // El día del último movimiento entra en la clave: mientras siga igual es
                    // el mismo parón y no se repite, pero si la orden avanza y se vuelve a
                    // dormir, eso es un parón nuevo y sí se avisa.
                    DedupeKey: $"parada:{orden.Id}:{orden.Movimiento.UtcDateTime:yyyyMMdd}"),
                ct);
        }
    }

    private static string Describe(WorkOrderStatus status) => status switch
    {
        WorkOrderStatus.Received => "Recibida",
        WorkOrderStatus.Diagnosing => "En diagnóstico",
        WorkOrderStatus.WaitingApproval => "Esperando aprobación",
        WorkOrderStatus.WaitingParts => "Esperando repuestos",
        WorkOrderStatus.InProgress => "En reparación",
        WorkOrderStatus.Testing => "En pruebas",
        WorkOrderStatus.Ready => "Lista para entregar",
        _ => status.ToString()
    };

    // ---------- La cita de mañana ----------

    private async Task CitasDeManianaAsync(
        IReadOnlyCollection<Guid> talleres, CancellationToken ct)
    {
        var maniana = clock.Today().AddDays(1);

        var citas = await db.Appointments.IgnoreQueryFilters()
            .Where(a => talleres.Contains(a.TenantId)
                        && a.Day == maniana
                        && a.Status == AppointmentStatus.Confirmed)
            .Select(a => new { a.Id, a.TenantId, a.BranchId, a.CustomerId, a.ContactName, a.Slot })
            .ToListAsync(ct);

        foreach (var porTaller in citas.GroupBy(x => x.TenantId))
        {
            foreach (var porSucursal in porTaller.GroupBy(x => x.BranchId))
            {
                var cuantas = porSucursal.Count();

                await avisos.NotifyBranchManagementAsync(
                    porTaller.Key,
                    porSucursal.Key,
                    new NotificationDraft(
                        NotificationType.AppointmentTomorrow,
                        cuantas == 1 ? "Mañana tiene una cita" : $"Mañana tiene {cuantas} citas",
                        string.Join(", ", porSucursal.Take(4).Select(c => $"{c.ContactName} ({Franja(c.Slot)})"))
                        + (cuantas > 4 ? $" y {cuantas - 4} más." : "."),
                        DedupeKey: $"citas:{porSucursal.Key}:{maniana:yyyy-MM-dd}"),
                    ct);
            }

            foreach (var cita in porTaller.Where(c => c.CustomerId is not null))
            {
                await avisos.NotifyCustomerAsync(
                    porTaller.Key,
                    cita.CustomerId!.Value,
                    new NotificationDraft(
                        NotificationType.AppointmentTomorrow,
                        "Su cita es mañana",
                        $"Lo esperamos mañana por la {Franja(cita.Slot)}.",
                        AppointmentId: cita.Id,
                        DedupeKey: $"cita:{cita.Id}"),
                    ct);
            }
        }
    }

    private static string Franja(AppointmentSlot slot) =>
        slot == AppointmentSlot.Morning ? "mañana" : "tarde";

    // ---------- El saldo que venció ----------

    /// <summary>
    /// Una sola vez por venta: el saldo vencido no cambia de día en día, y repetirlo cada
    /// mañana hasta que el cliente pague sería castigar al dueño por no haber cobrado.
    /// </summary>
    private async Task SaldosVencidosAsync(
        IReadOnlyCollection<Guid> talleres, CancellationToken ct)
    {
        var hoy = clock.StartOfToday();

        var vencidas = await db.Sales.IgnoreQueryFilters()
            .Where(s => talleres.Contains(s.TenantId)
                        && !s.IsVoided
                        && s.DueDate != null
                        && s.DueDate < hoy
                        && s.Total > (s.Payments.Sum(p => (decimal?)p.Amount) ?? 0))
            .Select(s => new
            {
                s.Id,
                s.TenantId,
                s.BranchId,
                s.Number,
                s.CustomerId,
                s.DueDate,
                Cliente = s.CustomerName
                          ?? db.Customers.Where(c => c.Id == s.CustomerId).Select(c => c.FullName).FirstOrDefault(),
                Saldo = s.Total - (s.Payments.Sum(p => (decimal?)p.Amount) ?? 0)
            })
            .ToListAsync(ct);

        foreach (var venta in vencidas)
        {
            var vencio = venta.DueDate!.Value.ToOffset(TimeSpan.FromHours(HusoHoras));
            var clave = $"cobro:{venta.Id}:{vencio:yyyyMMdd}";

            await avisos.NotifyBranchManagementAsync(
                venta.TenantId,
                venta.BranchId,
                new NotificationDraft(
                    NotificationType.PaymentOverdue,
                    $"Saldo vencido · {venta.Number}",
                    $"{venta.Cliente ?? "Cliente"} debe L {venta.Saldo:N2} desde el {vencio:dd/MM}.",
                    DedupeKey: clave),
                ct);

            if (venta.CustomerId is { } customerId)
                await avisos.NotifyCustomerAsync(
                    venta.TenantId,
                    customerId,
                    new NotificationDraft(
                        NotificationType.PaymentOverdue,
                        "Su saldo venció",
                        $"Quedó un saldo de L {venta.Saldo:N2} de la factura {venta.Number}, "
                        + $"acordado para el {vencio:dd/MM}.",
                        DedupeKey: clave),
                    ct);
        }
    }
}
