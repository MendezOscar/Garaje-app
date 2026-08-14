using System.Globalization;
using Garaj.Application.Sales;
using Garaj.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// El cierre de caja del día: lo cobrado, por forma de pago, por quién lo recibió, y el detalle
/// abono por abono.
/// </summary>
/// <remarks>
/// Está pensado para imprimirse y cuadrarlo contra el efectivo de la caja, así que el orden es
/// el de quien cuenta: primero los totales por forma de pago —que es lo que se compara con lo
/// que hay en el cajón—, después quién recibió cada cosa, y al final el detalle para buscar la
/// diferencia cuando no cuadra.
/// </remarks>
public static class CashClosePdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    public static byte[] Render(
        CashCloseDto cierre, string tenantName, string? legalName, string? phone,
        byte[]? logo = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header =>
                    Header(header, cierre, tenantName, legalName, phone, logo));
                page.Content().PaddingVertical(1, Unit.Centimetre)
                    .Element(content => Content(content, cierre));
                page.Footer().Element(Footer);
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(
        IContainer container, CashCloseDto cierre,
        string tenantName, string? legalName, string? phone, byte[]? logo)
    {
        container.Row(row =>
        {
            if (logo is not null)
                row.ConstantItem(64).Height(48).AlignMiddle().Image(logo).FitArea();

            row.RelativeItem().PaddingLeft(logo is null ? 0 : 10).Column(column =>
            {
                column.Item().Text(tenantName).FontSize(16).Bold();
                if (!string.IsNullOrWhiteSpace(legalName)) column.Item().Text(legalName).FontSize(9);
                if (!string.IsNullOrWhiteSpace(phone)) column.Item().Text($"Tel. {phone}").FontSize(9);

                column.Item().Text(cierre.BranchName ?? "Todas las sucursales")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(200).Column(column =>
            {
                column.Item().AlignRight().Text("CIERRE DE CAJA").FontSize(14).Bold();
                column.Item().AlignRight().Text(cierre.DayLabel)
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void Content(IContainer container, CashCloseDto cierre)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            // Lo primero, porque es lo que se compara con el dinero que hay en el cajón.
            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
            {
                c.Item().Text("Cobrado en el día").FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text(Money(cierre.Total, cierre.Currency)).FontSize(18).Bold();
                c.Item().Text($"{cierre.PaymentCount} abono(s)")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            if (cierre.PaymentCount == 0)
            {
                column.Item().Text("No se recibió ningún pago este día.").FontSize(11);
            }
            else
            {
                column.Item().Element(item => Methods(item, cierre));
                column.Item().Element(item => Receivers(item, cierre));
                column.Item().Element(item => Detail(item, cierre));
            }

            if (cierre.VoidedCount > 0)
            {
                column.Item().Text(text =>
                {
                    text.Span("Se dejaron fuera ").FontSize(9);
                    text.Span($"{cierre.VoidedCount} abono(s) por "
                              + $"{Money(cierre.VoidedAmount, cierre.Currency)}").FontSize(9).SemiBold();
                    text.Span(" de ventas anuladas.").FontSize(9);
                });
            }
        });
    }

    private static void Methods(IContainer container, CashCloseDto cierre)
    {
        container.Column(column =>
        {
            column.Item().Text("Por forma de pago").SemiBold();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(100);
                });

                foreach (var method in cierre.ByMethod)
                {
                    table.Cell().PaddingVertical(2).Text(PaymentLabel(method.Method));
                    table.Cell().PaddingVertical(2).AlignRight()
                        .Text($"{method.Count}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    table.Cell().PaddingVertical(2).AlignRight()
                        .Text(Money(method.Total, cierre.Currency));
                }
            });
        });
    }

    private static void Receivers(IContainer container, CashCloseDto cierre)
    {
        container.Column(column =>
        {
            column.Item().Text("Quién lo recibió").SemiBold();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(100);
                });

                foreach (var receiver in cierre.ByReceiver)
                {
                    table.Cell().PaddingVertical(2).Text(receiver.ReceiverName);
                    table.Cell().PaddingVertical(2).AlignRight()
                        .Text($"{receiver.Count}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    table.Cell().PaddingVertical(2).AlignRight()
                        .Text(Money(receiver.Total, cierre.Currency));
                }
            });
        });
    }

    /// <summary>El detalle, que es donde se busca la diferencia cuando la caja no cuadra.</summary>
    private static void Detail(IContainer container, CashCloseDto cierre)
    {
        container.Column(column =>
        {
            column.Item().Text("Detalle").SemiBold();

            column.Item().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(3)
                .Row(row =>
                {
                    row.ConstantItem(42).Text("Hora").FontSize(8).SemiBold();
                    row.ConstantItem(95).Text("Factura").FontSize(8).SemiBold();
                    row.RelativeItem().Text("Cliente").FontSize(8).SemiBold();
                    row.ConstantItem(70).Text("Forma").FontSize(8).SemiBold();
                    row.ConstantItem(80).AlignRight().Text("Monto").FontSize(8).SemiBold();
                });

            foreach (var payment in cierre.Payments)
            {
                // Filas y no una tabla: así `ShowEntire` mantiene cada abono junto. En una
                // tabla, un abono a final de página se partía y la hoja siguiente empezaba con
                // la sucursal de un pago cuyo monto quedó en la anterior.
                column.Item().ShowEntire().PaddingVertical(2).Row(row =>
                {
                    row.ConstantItem(42).Text($"{payment.PaidAt.ToLocalTime():HH:mm}").FontSize(9);
                    row.ConstantItem(95).Text(payment.SaleNumber).FontSize(9);

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(payment.CustomerName ?? "Mostrador").FontSize(9);

                        var extra = new[] { payment.BranchName, payment.Reference }
                            .Where(x => !string.IsNullOrWhiteSpace(x));

                        c.Item().Text(string.Join(" · ", extra))
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    row.ConstantItem(70).Column(c =>
                    {
                        c.Item().Text(PaymentLabel(payment.Method)).FontSize(9);
                        c.Item().Text(payment.ReceiverName)
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });

                    row.ConstantItem(80).AlignRight()
                        .Text(Money(payment.Amount, cierre.Currency)).FontSize(9);
                });
            }

            column.Item().PaddingTop(6).AlignRight().Width(240).BorderTop(1)
                .BorderColor(Colors.Grey.Darken1).PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL COBRADO").Bold();
                    row.ConstantItem(110).AlignRight()
                        .Text(Money(cierre.Total, cierre.Currency)).Bold().FontSize(13);
                });
        });
    }

    private static void Footer(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter()
                .Text("Lo cobrado en el día, que no es lo mismo que lo facturado: una venta a "
                      + "crédito suma aquí el día que el cliente paga.")
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

    private static string Money(decimal value, string currency) =>
        $"{Symbol(currency)} {value.ToString("N2", Culture)}";

    private static string Symbol(string currency) => currency switch
    {
        "HNL" => "L",
        "USD" => "$",
        _ => currency
    };

    private static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Transfer => "Transferencia",
        _ => "Otro"
    };
}
