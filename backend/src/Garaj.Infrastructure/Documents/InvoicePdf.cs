using System.Globalization;
using Garaj.Application.Sales;
using Garaj.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// La factura de una venta: lo que el cliente se lleva al recoger el vehículo.
/// </summary>
/// <remarks>
/// No lleva costo ni margen aunque el DTO los traiga: es el documento del cliente, y esos dos
/// números son del taller. Se genera al vuelo y no se guarda, igual que la cotización: la
/// venta es la fila de la base y el PDF solo su presentación, así que reimprimirla siempre
/// devuelve lo mismo que hay registrado.
/// </remarks>
public static class InvoicePdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    /// <param name="logo">PNG del logo del taller, o null. Ver <see cref="QuotePdf"/>.</param>
    public static byte[] Render(
        SaleDetailDto sale, string tenantName, string? legalName, string? phone, string? taxId,
        byte[]? logo = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header => Header(header, sale, tenantName, legalName, phone, taxId, logo));
                page.Content().PaddingVertical(1, Unit.Centimetre).Element(content => Content(content, sale));
                page.Footer().Element(footer => Footer(footer, sale));
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(
        IContainer container, SaleDetailDto sale,
        string tenantName, string? legalName, string? phone, string? taxId, byte[]? logo)
    {
        container.Row(row =>
        {
            if (logo is not null)
                row.ConstantItem(64).Height(48).AlignMiddle().Image(logo).FitArea();

            row.RelativeItem().PaddingLeft(logo is null ? 0 : 10).Column(column =>
            {
                column.Item().Text(tenantName).FontSize(16).Bold();
                if (!string.IsNullOrWhiteSpace(legalName)) column.Item().Text(legalName).FontSize(9);
                if (!string.IsNullOrWhiteSpace(taxId)) column.Item().Text($"RTN {taxId}").FontSize(9);
                if (!string.IsNullOrWhiteSpace(phone)) column.Item().Text($"Tel. {phone}").FontSize(9);
                column.Item().Text(sale.BranchName).FontSize(9).FontColor(Colors.Grey.Darken1);

                if (sale.BranchAddress is { } direccion)
                    column.Item().Text(direccion).FontSize(8).FontColor(Colors.Grey.Darken1);

                // El CAI y su rango van en el encabezado, junto a los datos del emisor, que es
                // donde los busca quien revisa una factura.
                if (sale.FiscalCai is { } cai)
                {
                    column.Item().PaddingTop(6).Text($"CAI: {cai}").FontSize(8);
                    column.Item().Text($"Rango autorizado: {sale.FiscalRangeText}").FontSize(8);

                    if (sale.FiscalIssueDeadline is { } deadline)
                    {
                        column.Item()
                            .Text($"Fecha límite de emisión: {deadline.ToLocalTime():dd/MM/yyyy}")
                            .FontSize(8);
                    }
                }
            });

            row.ConstantItem(200).Column(column =>
            {
                column.Item().AlignRight().Text("FACTURA").FontSize(14).Bold();

                // Con CAI manda el correlativo fiscal: es el número con el que existe la
                // factura para el SAR. El interno queda debajo, para el taller.
                if (sale.FiscalNumber is { } fiscalNumber)
                {
                    column.Item().AlignRight().Text(fiscalNumber).FontSize(12).Bold();
                    column.Item().AlignRight().Text(sale.Number).FontSize(8)
                        .FontColor(Colors.Grey.Darken1);
                }
                else
                {
                    column.Item().AlignRight().Text(sale.Number).FontSize(12);
                }

                column.Item().AlignRight()
                    .Text($"Fecha: {sale.SaleDate.ToLocalTime():dd/MM/yyyy HH:mm}").FontSize(9);
                column.Item().AlignRight()
                    .Text(sale.Balance > 0 ? "CRÉDITO" : PaymentLabel(sale.PaymentMethod))
                    .FontSize(9).FontColor(Colors.Grey.Darken1);

                if (sale.DueDate is { } due)
                {
                    column.Item().AlignRight()
                        .Text($"Vence: {due.ToLocalTime():dd/MM/yyyy}")
                        .FontSize(9)
                        .FontColor(sale.IsOverdue ? Colors.Red.Darken2 : Colors.Grey.Darken1);
                }

                // Una factura anulada se sigue pudiendo imprimir —el cliente puede tener la
                // copia vieja en la mano— pero tiene que decirlo en grande.
                if (sale.IsVoided)
                {
                    column.Item().AlignRight().PaddingTop(4)
                        .Text("ANULADA").FontSize(12).Bold().FontColor(Colors.Red.Darken2);
                }
            });
        });
    }

    private static void Content(IContainer container, SaleDetailDto sale)
    {
        container.Column(column =>
        {
            column.Spacing(12);

            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Cliente").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(sale.CustomerName ?? "Cliente de mostrador").SemiBold();
                    if (sale.CustomerPhone is not null) c.Item().Text(sale.CustomerPhone).FontSize(9);

                    // En la factura fiscal el RTN es obligatorio cuando el cliente lo pide;
                    // sin RTN se emite a consumidor final, y hay que decirlo.
                    if (sale.FiscalNumber is not null)
                    {
                        c.Item().Text(sale.CustomerTaxId is { } rtn
                                ? $"RTN {rtn}"
                                : "Consumidor final")
                            .FontSize(9);
                    }
                });

                if (sale.VehicleLabel is not null)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Vehículo").FontSize(8).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(sale.VehicleLabel).SemiBold();
                    });
                }

                if (sale.WorkOrderNumber is not null)
                {
                    row.ConstantItem(120).Column(c =>
                    {
                        c.Item().Text("Orden").FontSize(8).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(sale.WorkOrderNumber).SemiBold();
                    });
                }
            });

            if (sale.IsVoided && sale.VoidReason is { } reason)
                column.Item().Text($"Motivo de la anulación: {reason}").FontSize(9).Italic();

            if (!string.IsNullOrWhiteSpace(sale.Notes))
                column.Item().Text(sale.Notes).FontSize(9).Italic();

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
                foreach (var line in sale.Lines)
                {
                    index++;
                    table.Cell().Element(BodyCell).Text(index.ToString());
                    table.Cell().Element(BodyCell).Column(c =>
                    {
                        c.Item().Text(line.Description);
                        c.Item().Text(line.LineType == LineType.Part ? "Repuesto" : "Mano de obra")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    table.Cell().Element(BodyCell).AlignRight().Text(Quantity(line.Quantity));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(line.UnitPrice, sale.Currency));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(line.Total, sale.Currency));
                }
            });

            column.Item().AlignRight().Width(240).Column(totals =>
            {
                Total(totals, "Subtotal", Money(sale.Subtotal, sale.Currency));

                if (sale.DiscountTotal > 0)
                    Total(totals, "Descuento", $"−{Money(sale.DiscountTotal, sale.Currency)}");

                // El desglose que pide el régimen: en un taller todo va gravado, pero el
                // importe exento se imprime igual —en cero— porque el formato lo exige.
                if (sale.FiscalNumber is not null)
                {
                    Total(totals, "Importe exento", Money(0, sale.Currency));
                    Total(totals, "Importe gravado", Money(sale.Subtotal - sale.DiscountTotal, sale.Currency));
                }

                if (sale.TaxRate > 0)
                    Total(totals, $"ISV {sale.TaxRate:0.##}%", Money(sale.TaxTotal, sale.Currency));

                totals.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Grey.Darken1)
                    .PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("TOTAL").Bold();
                        row.ConstantItem(110).AlignRight()
                            .Text(Money(sale.Total, sale.Currency)).Bold().FontSize(13);
                    });

                // El saldo solo se imprime cuando lo hay: en una venta de contado, una línea
                // que dice "saldo 0.00" siembra la duda de si se debe algo.
                if (sale.Balance > 0)
                {
                    Total(totals, "Abonado", Money(sale.AmountPaid, sale.Currency));

                    totals.Item().PaddingTop(2).Row(row =>
                    {
                        row.RelativeItem().Text("SALDO PENDIENTE").Bold().FontSize(10);
                        row.ConstantItem(110).AlignRight()
                            .Text(Money(sale.Balance, sale.Currency))
                            .Bold().FontSize(12).FontColor(Colors.Red.Darken2);
                    });
                }
            });

            // El valor en letras es obligatorio en la factura fiscal, y de paso estorba la
            // corrección a mano de la cifra.
            if (sale.FiscalNumber is not null)
            {
                column.Item().PaddingTop(4)
                    .Text($"Son: {NumeroEnLetras.Moneda(sale.Total, sale.Currency)}")
                    .FontSize(9).SemiBold();
            }

            // Los abonos van impresos: es el comprobante que el cliente lleva encima de lo
            // que ya pagó, y evita la discusión de "yo le aboné hace quince días".
            if (sale.Payments.Count > 1 || sale.Balance > 0)
            {
                column.Item().PaddingTop(4).Column(payments =>
                {
                    payments.Item().Text("Abonos").SemiBold().FontSize(9);

                    foreach (var payment in sale.Payments)
                    {
                        payments.Item().PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem().Text(
                                $"{payment.PaidAt.ToLocalTime():dd/MM/yyyy} · {PaymentLabel(payment.Method)}"
                                + (payment.Reference is null ? "" : $" · {payment.Reference}"))
                                .FontSize(9);
                            row.ConstantItem(110).AlignRight()
                                .Text(Money(payment.Amount, sale.Currency)).FontSize(9);
                        });
                    }
                });
            }
        });
    }

    private static void Footer(IContainer container, SaleDetailDto sale)
    {
        container.Column(column =>
        {
            if (sale.FiscalNumber is not null)
            {
                column.Item().AlignCenter()
                    .Text("Original: Cliente · Copia: Obligado tributario")
                    .FontSize(8).SemiBold();
            }
            else
            {
                // Sin CAI esto no es un documento fiscal, y decirlo aquí evita que alguien lo
                // presente como tal: en la pantalla ya se advierte, en el papel no se veía.
                column.Item().AlignCenter()
                    .Text("Comprobante de entrega. No es documento fiscal.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            }

            column.Item().AlignCenter()
                .Text("Gracias por su preferencia.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

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

    private static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Transfer => "Transferencia",
        _ => "Otro"
    };
}
