using System.Globalization;
using Garaj.Application.Platform;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// El documento que le mandamos al taller por su mensualidad.
/// </summary>
/// <remarks>
/// Es el espejo de <see cref="InvoicePdf"/> con los papeles cambiados: aquí el emisor somos
/// nosotros y el cliente es el taller. Se mantiene aparte y no se reutiliza aquella, porque
/// la del taller lleva vehículo, orden, garantía y abonos —nada de eso existe en una
/// mensualidad— y mezclarlas convertiría las dos en una sola llena de condicionales.
///
/// Con CAI el encabezado dice «Factura»; sin él, «Comprobante de pago», y no se imprime
/// nada que parezca un dato fiscal. Un comprobante disfrazado de factura es un problema del
/// que se entera uno en la auditoría.
/// </remarks>
public static class SubscriptionInvoicePdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    public static byte[] Render(SubscriptionInvoiceDto factura, PlatformSettingsDto emisor)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header => Header(header, factura, emisor));
                page.Content().PaddingVertical(1, Unit.Centimetre)
                    .Element(content => Content(content, factura));
                page.Footer().Element(footer => Footer(footer, factura, emisor));
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(
        IContainer container, SubscriptionInvoiceDto factura, PlatformSettingsDto emisor)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(emisor.Name).FontSize(16).Bold();

                if (!string.IsNullOrWhiteSpace(emisor.LegalName))
                    column.Item().Text(emisor.LegalName).FontSize(9);
                if (!string.IsNullOrWhiteSpace(emisor.TaxId))
                    column.Item().Text($"RTN {emisor.TaxId}").FontSize(9);
                if (!string.IsNullOrWhiteSpace(emisor.Address))
                    column.Item().Text(emisor.Address).FontSize(8).FontColor(Colors.Grey.Darken1);
                if (!string.IsNullOrWhiteSpace(emisor.Phone))
                    column.Item().Text($"Tel. {emisor.Phone}").FontSize(9);
                if (!string.IsNullOrWhiteSpace(emisor.Email))
                    column.Item().Text(emisor.Email).FontSize(9);
            });

            row.ConstantItem(200).AlignRight().Column(column =>
            {
                column.Item().Text(factura.Fiscal ? "FACTURA" : "COMPROBANTE DE PAGO")
                    .FontSize(14).Bold();
                column.Item().Text(factura.Number).FontSize(12).SemiBold();
                column.Item().Text(factura.IssuedOn.ToString("dd/MM/yyyy", Culture)).FontSize(9);

                if (factura.IsVoided)
                    column.Item().PaddingTop(4).Text("ANULADA").Bold().FontColor(Colors.Red.Darken2);

                // Lo que el régimen pide impreso, y solo cuando de verdad hay CAI.
                if (factura.Fiscal)
                {
                    column.Item().PaddingTop(6).Text($"CAI: {factura.FiscalCai}").FontSize(8);

                    if (factura.FiscalRangeText is { } rango)
                        column.Item().Text($"Rango autorizado: {rango}").FontSize(8);

                    if (factura.FiscalIssueDeadline is { } limite)
                    {
                        column.Item()
                            .Text($"Fecha límite de emisión: {limite.ToLocalTime():dd/MM/yyyy}")
                            .FontSize(8);
                    }
                }
            });
        });
    }

    private static void Content(IContainer container, SubscriptionInvoiceDto factura)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Cliente").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(factura.CustomerLegalName ?? factura.CustomerName).SemiBold();

                    if (factura.CustomerLegalName is not null)
                        c.Item().Text(factura.CustomerName).FontSize(9);

                    // En la factura fiscal el RTN del cliente es lo que le permite deducirla.
                    if (factura.Fiscal)
                    {
                        c.Item().Text(factura.CustomerTaxId is { } rtn
                            ? $"RTN {rtn}"
                            : "Consumidor final").FontSize(9);
                    }
                });
            });

            if (factura.IsVoided && factura.VoidReason is { } motivo)
                column.Item().Text($"Motivo de la anulación: {motivo}").FontSize(9).Italic();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(90);
                    columns.ConstantColumn(90);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Concepto");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Cant.");
                    header.Cell().Element(HeaderCell).AlignRight()
                        .Text(factura.TaxRate > 0 ? "P. unit. sin ISV" : "P. unit.");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Total");
                });

                foreach (var linea in factura.Lines)
                {
                    // Solo se le saca el impuesto a la línea cuando el precio lo llevaba
                    // adentro. Cobrando el ISV aparte, la línea ya viene neta.
                    var desglosar = factura.TaxRate > 0 && factura.PricesIncludedTax;
                    var unitario = desglosar
                        ? Domain.Rules.Isv.Base(linea.UnitPrice, factura.TaxRate)
                        : linea.UnitPrice;
                    var importe = desglosar
                        ? Domain.Rules.Isv.Base(linea.Total, factura.TaxRate)
                        : linea.Total;

                    table.Cell().Element(BodyCell).Text(linea.Description);
                    table.Cell().Element(BodyCell).AlignRight().Text(Quantity(linea.Quantity));
                    table.Cell().Element(BodyCell).AlignRight()
                        .Text(Money(unitario, factura.Currency));
                    table.Cell().Element(BodyCell).AlignRight()
                        .Text(Money(importe, factura.Currency));
                }
            });

            column.Item().AlignRight().Width(240).Column(totals =>
            {
                if (factura.TaxRate > 0)
                {
                    Total(totals, "Neto", Money(factura.Total - factura.TaxTotal, factura.Currency));
                    Total(totals, $"ISV {factura.TaxRate:0.##}%",
                        Money(factura.TaxTotal, factura.Currency));
                }
                else
                {
                    Total(totals, "Subtotal", Money(factura.Subtotal, factura.Currency));
                }

                totals.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Darken1)
                    .PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("TOTAL").Bold().FontSize(11);
                        row.ConstantItem(110).AlignRight()
                            .Text(Money(factura.Total, factura.Currency)).Bold().FontSize(13);
                    });
            });

            // El valor en letras es obligatorio en la factura fiscal, y de paso estorba la
            // corrección a mano de la cifra.
            if (factura.Fiscal)
            {
                column.Item().PaddingTop(4)
                    .Text($"Son: {NumeroEnLetras.Moneda(factura.Total, factura.Currency)}")
                    .FontSize(9).SemiBold();
            }

            if (!string.IsNullOrWhiteSpace(factura.Note))
                column.Item().Text(factura.Note).FontSize(9).Italic();
        });
    }

    private static void Footer(
        IContainer container, SubscriptionInvoiceDto factura, PlatformSettingsDto emisor)
    {
        container.Column(column =>
        {
            column.Item().Text(factura.Fiscal
                    ? "Documento fiscal autorizado por el SAR."
                    : "Este documento es un comprobante de pago y no sustituye una factura fiscal.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            column.Item().Text($"{emisor.Name} · Servicio de software para talleres")
                .FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void Total(ColumnDescriptor column, string label, string value) =>
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(9);
            row.ConstantItem(110).AlignRight().Text(value).FontSize(9);
        });

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten3).PaddingVertical(5).PaddingHorizontal(4)
            .DefaultTextStyle(x => x.SemiBold().FontSize(9));

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(4).PaddingHorizontal(4).DefaultTextStyle(x => x.FontSize(9));

    private static string Money(decimal value, string currency) =>
        $"{(currency == "HNL" ? "L" : currency)} {value.ToString("N2", Culture)}";

    /// <summary>Sin decimales cuando no hacen falta: «1» se lee mejor que «1.00».</summary>
    private static string Quantity(decimal value) =>
        value == Math.Floor(value) ? value.ToString("0", Culture) : value.ToString("0.##", Culture);
}
