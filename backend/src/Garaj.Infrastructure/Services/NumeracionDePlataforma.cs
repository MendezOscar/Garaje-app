using System.Runtime.CompilerServices;
using Garaj.Application.Common;
using Garaj.Domain.Entities;
using Garaj.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// Cómo se toma el número de un documento nuestro: el autorizado por el SAR y el correlativo
/// propio del comprobante.
/// </summary>
/// <remarks>
/// Vive aparte del servicio porque es la única pieza del proyecto con un problema de
/// concurrencia de verdad, y porque así se puede probar sola: la prueba llama a esto desde
/// diez tareas a la vez y comprueba que salen diez números distintos.
///
/// El número se toma con <b>un solo UPDATE atómico</b> y no leyendo y sumando en memoria.
/// Mientras las facturas las emitía una persona de a una daba igual; con los portales de los
/// otros servicios pidiendo número por la API, dos emisiones a la vez leerían el mismo
/// <c>next_number</c> y saldrían dos documentos con el mismo número del SAR —y uno de los dos
/// no existiría para el SAR—.
/// </remarks>
internal static class NumeracionDePlataforma
{
    /// <summary>
    /// Toma el siguiente número del rango y lo avanza. Devuelve null cuando ya no quedaba:
    /// el rango se agotó, venció o lo desactivaron entre que se leyó y se fue a tomar.
    /// </summary>
    /// <remarks>
    /// Las condiciones del tope y de la fecha van en el <c>WHERE</c> del UPDATE a propósito:
    /// Postgres las vuelve a evaluar sobre la fila ya bloqueada, así que el segundo en llegar
    /// no se pasa del rango aunque haya leído un valor viejo.
    /// </remarks>
    internal static async Task<int?> TomarAutorizadoAsync(
        GarajDbContext db, Guid rangoId, DateTimeOffset ahora, CancellationToken ct)
    {
        var tomados = await db.Database
            .SqlQuery<int>(FormattableStringFactory.Create(
                "UPDATE platform_fiscal_ranges SET next_number = next_number + 1 "
                + "WHERE id = {0} AND is_active AND next_number <= range_end "
                + "AND issue_deadline >= {1} RETURNING next_number - 1",
                rangoId, ahora))
            .ToListAsync(ct);

        return tomados.Count > 0 ? tomados[0] : null;
    }

    /// <summary>
    /// El número autorizado para este documento, con el CAI y el rango ya copiados en él. Se
    /// niega —en vez de emitir un comprobante por su cuenta— cuando no hay rango, venció o se
    /// acabó: quien pidió factura con CAI tiene que enterarse ahí, no al imprimir.
    /// </summary>
    /// <remarks>
    /// La lectura de arriba es solo para poder decir qué falta en vez de un «no se pudo». El
    /// número se toma abajo, atómico.
    ///
    /// Se toma antes de guardar la factura, así que si el guardado falla queda un hueco en la
    /// numeración. Es a propósito: un hueco se explica —«ese número no se usó»— y un número
    /// repetido no.
    /// </remarks>
    internal static async Task<string> AutorizadoAsync(
        GarajDbContext db, SubscriptionInvoice factura, DateTimeOffset ahora, CancellationToken ct)
    {
        var rango = await db.PlatformFiscalRanges
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new AppException(
                "Todavía no hay un CAI registrado. Regístrelo en Ajustes de facturación, o "
                + "emita el comprobante sin CAI.");

        if (rango.IssueDeadline < ahora)
            throw new AppException("El CAI registrado venció. Registre el rango nuevo del SAR.");

        if (rango.NextNumber > rango.RangeEnd)
            throw new AppException("Se agotó el rango autorizado. Registre el rango nuevo del SAR.");

        var numero = await TomarAutorizadoAsync(db, rango.Id, ahora, ct)
            ?? throw new ConflictException(
                "Otra emisión se llevó el último número del rango autorizado. Registre el rango "
                + "nuevo del SAR.");

        factura.PlatformFiscalRangeId = rango.Id;
        factura.FiscalCai = rango.Cai;
        factura.FiscalRangeText = rango.RangeText;
        factura.FiscalIssueDeadline = rango.IssueDeadline;

        return rango.Format(numero);
    }

    /// <summary>
    /// El correlativo nuestro, el del comprobante sin CAI: «C-000007».
    /// </summary>
    internal static async Task<string> PropioAsync(
        GarajDbContext db, PlatformSettings settings, CancellationToken ct)
    {
        // La fila nace sola la primera vez, y todavía no está en la base que se pueda actualizar.
        if (db.Entry(settings).State == EntityState.Added)
        {
            var primero = settings.NextInvoiceNumber;
            settings.NextInvoiceNumber++;
            return $"{settings.InvoicePrefix}-{primero:D6}";
        }

        var numero = await TomarPropioAsync(db, settings.Id, ct) ?? settings.NextInvoiceNumber;

        // Lo que quedó en memoria ya no vale: el bueno es el de la base, y no hay que
        // reescribirlo al guardar.
        var propiedad = db.Entry(settings).Property(s => s.NextInvoiceNumber);
        propiedad.CurrentValue = numero + 1;
        propiedad.IsModified = false;

        return $"{settings.InvoicePrefix}-{numero:D6}";
    }

    /// <summary>
    /// Toma el siguiente correlativo propio, el del comprobante sin CAI. Devuelve null si la
    /// fila de ajustes no está en la base todavía.
    /// </summary>
    internal static async Task<int?> TomarPropioAsync(
        GarajDbContext db, Guid ajustesId, CancellationToken ct)
    {
        var tomados = await db.Database
            .SqlQuery<int>(FormattableStringFactory.Create(
                "UPDATE platform_settings SET next_invoice_number = next_invoice_number + 1 "
                + "WHERE id = {0} RETURNING next_invoice_number - 1",
                ajustesId))
            .ToListAsync(ct);

        return tomados.Count > 0 ? tomados[0] : null;
    }
}
