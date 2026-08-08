using System.Globalization;
using Garaj.Application.Quotes;
using Garaj.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// PDF de la cotización: lo que el cliente imprime, reenvía o lleva al banco. Se genera en
/// memoria y no se guarda: la cotización es la fila de la base, el PDF solo su presentación.
/// </summary>
public static class QuotePdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    public static byte[] Render(
        QuoteDetailDto quote, string tenantName, string? legalName, string? phone, string? taxId)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header => Header(header, quote, tenantName, legalName, phone, taxId));
                page.Content().PaddingVertical(1, Unit.Centimetre).Element(content => Content(content, quote));
                page.Footer().Element(footer => Footer(footer, quote));
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(
        IContainer container, QuoteDetailDto quote,
        string tenantName, string? legalName, string? phone, string? taxId)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(tenantName).FontSize(16).Bold();
                if (!string.IsNullOrWhiteSpace(legalName)) column.Item().Text(legalName).FontSize(9);
                if (!string.IsNullOrWhiteSpace(taxId)) column.Item().Text($"RTN {taxId}").FontSize(9);
                if (!string.IsNullOrWhiteSpace(phone)) column.Item().Text($"Tel. {phone}").FontSize(9);
                column.Item().Text(quote.BranchName).FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(200).Column(column =>
            {
                column.Item().AlignRight().Text("COTIZACIÓN").FontSize(14).Bold();
                column.Item().AlignRight().Text(quote.Number).FontSize(12);
                column.Item().AlignRight().Text($"Fecha: {quote.CreatedAt.ToLocalTime():dd/MM/yyyy}").FontSize(9);

                if (quote.ValidUntil is { } valid)
                    column.Item().AlignRight().Text($"Válida hasta: {valid.ToLocalTime():dd/MM/yyyy}").FontSize(9);

                column.Item().AlignRight().PaddingTop(4)
                    .Text(StatusLabel(quote.Status))
                    .FontSize(9).Bold().FontColor(StatusColor(quote.Status));
            });
        });
    }

    private static void Content(IContainer container, QuoteDetailDto quote)
    {
        container.Column(column =>
        {
            column.Spacing(12);

            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Cliente").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(quote.CustomerName).SemiBold();
                    c.Item().Text(quote.CustomerPhone).FontSize(9);
                });

                if (quote.VehicleLabel is not null)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Vehículo").FontSize(8).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(quote.VehicleLabel).SemiBold();
                        if (quote.Plate is not null) c.Item().Text(quote.Plate).FontSize(9);
                    });
                }

                if (quote.WorkOrderNumber is not null)
                {
                    row.ConstantItem(120).Column(c =>
                    {
                        c.Item().Text("Orden").FontSize(8).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(quote.WorkOrderNumber).SemiBold();
                    });
                }
            });

            if (!string.IsNullOrWhiteSpace(quote.Notes))
                column.Item().Text(quote.Notes).FontSize(9).Italic();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(20);
                    columns.RelativeColumn();
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(75);
                    columns.ConstantColumn(75);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("#");
                    header.Cell().Element(HeaderCell).Text("Descripción");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Cant.");
                    header.Cell().Element(HeaderCell).AlignRight().Text("P. unit.");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Total");
                });

                var index = 0;
                foreach (var line in quote.Lines)
                {
                    index++;
                    table.Cell().Element(BodyCell).Text(index.ToString());
                    table.Cell().Element(BodyCell).Column(c =>
                    {
                        c.Item().Text(line.Description);
                        // El cliente distingue de un vistazo qué está pagando en piezas y
                        // qué en trabajo, que es la pregunta que siempre hace.
                        c.Item().Text(line.LineType == LineType.Part ? "Repuesto" : "Mano de obra")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    table.Cell().Element(BodyCell).AlignRight().Text(Quantity(line.Quantity));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(line.UnitPrice, quote.Currency));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(line.Total, quote.Currency));
                }
            });

            column.Item().AlignRight().Width(240).Column(totals =>
            {
                Total(totals, "Subtotal", Money(quote.Subtotal, quote.Currency));

                if (quote.DiscountTotal > 0)
                    Total(totals, "Descuento", $"−{Money(quote.DiscountTotal, quote.Currency)}");

                if (quote.TaxRate > 0)
                    Total(totals, $"ISV {quote.TaxRate:0.##}%", Money(quote.TaxTotal, quote.Currency));

                totals.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Darken1)
                    .PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("TOTAL").Bold();
                        row.ConstantItem(110).AlignRight()
                            .Text(Money(quote.Total, quote.Currency)).Bold().FontSize(13);
                    });
            });

            if (quote.RespondedAt is { } responded)
            {
                column.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                {
                    c.Item().Text(
                        quote.Status == QuoteStatus.Approved
                            ? $"Aprobada por el cliente el {responded.ToLocalTime():dd/MM/yyyy HH:mm}"
                            : $"Rechazada por el cliente el {responded.ToLocalTime():dd/MM/yyyy HH:mm}")
                        .SemiBold().FontSize(9);

                    if (quote.CustomerResponseNote is { } note)
                        c.Item().Text($"«{note}»").FontSize(9).Italic();
                });
            }
        });
    }

    private static void Footer(IContainer container, QuoteDetailDto quote)
    {
        container.Column(column =>
        {
            if (quote.PublicUrl is { } url)
            {
                column.Item().AlignCenter()
                    .Text($"Puede ver y aprobar esta cotización en línea: {url}")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            }

            column.Item().AlignCenter().PaddingTop(2).Text(text =>
            {
                text.Span("Página ").FontSize(8).FontColor(Colors.Grey.Darken1);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                text.Span(" de ").FontSize(8).FontColor(Colors.Grey.Darken1);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void Total(ColumnDescriptor column, string label, string value) =>
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(9).FontColor(Colors.Grey.Darken2);
            row.ConstantItem(110).AlignRight().Text(value).FontSize(9);
        });

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(4)
            .DefaultTextStyle(x => x.SemiBold().FontSize(9));

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);

    private static string Money(decimal value, string currency) =>
        $"{currency} {value.ToString("N2", Culture)}";

    private static string Quantity(decimal value) =>
        value == Math.Truncate(value)
            ? value.ToString("N0", Culture)
            : value.ToString("0.##", Culture);

    private static string StatusLabel(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "BORRADOR",
        QuoteStatus.Sent => "ENVIADA",
        QuoteStatus.Approved => "APROBADA",
        QuoteStatus.Rejected => "RECHAZADA",
        QuoteStatus.Expired => "VENCIDA",
        _ => status.ToString().ToUpperInvariant()
    };

    private static string StatusColor(QuoteStatus status) => status switch
    {
        QuoteStatus.Approved => Colors.Green.Darken2,
        QuoteStatus.Rejected => Colors.Red.Darken2,
        QuoteStatus.Sent => Colors.Blue.Darken2,
        _ => Colors.Grey.Darken1
    };
}
