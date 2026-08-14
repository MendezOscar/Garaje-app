using System.Globalization;
using Garaj.Application.Sales;
using Garaj.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// El estado de cuenta de un cliente: lo que debe hoy, factura por factura, con los abonos que
/// ha hecho en cada una.
/// </summary>
/// <remarks>
/// Responde a «¿cuánto le debo?», que es la pregunta con la que un cliente llama al taller. No
/// es un documento fiscal y lo dice en el pie: la factura es la de cada venta, y esta hoja solo
/// las resume.
///
/// Trae únicamente las facturas con saldo. Meter también las pagadas la convertiría en un
/// historial de todo lo que el cliente ha gastado en su vida, que es otra cosa y nadie la lee.
/// </remarks>
public static class StatementPdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    public static byte[] Render(
        CustomerStatementDto statement, string tenantName, string? legalName, string? phone,
        string? taxId, string? email = null, string? address = null, byte[]? logo = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header =>
                    Header(header, statement, tenantName, legalName, phone, taxId, email, address, logo));
                page.Content().PaddingVertical(1, Unit.Centimetre)
                    .Element(content => Content(content, statement));
                page.Footer().Element(Footer);
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(
        IContainer container, CustomerStatementDto statement,
        string tenantName, string? legalName, string? phone, string? taxId,
        string? email, string? address, byte[]? logo)
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
                if (!string.IsNullOrWhiteSpace(email)) column.Item().Text(email).FontSize(9);
                if (!string.IsNullOrWhiteSpace(address))
                    column.Item().Text(address).FontSize(8).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(200).Column(column =>
            {
                column.Item().AlignRight().Text("ESTADO DE CUENTA").FontSize(14).Bold();
                column.Item().AlignRight()
                    .Text($"Al {statement.AsOf.ToLocalTime():dd/MM/yyyy}")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void Content(IContainer container, CustomerStatementDto statement)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            // A quién se le debe cobrar. El nombre de facturación manda si lo tiene: es el
            // que el cliente reconoce en sus facturas.
            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
            {
                c.Item().Text("Cliente").FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text(statement.BillingName ?? statement.CustomerName).SemiBold();

                if (statement.BillingName is not null)
                {
                    c.Item().Text(statement.CustomerName)
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                }

                c.Item().Text(statement.Phone).FontSize(9);
                if (statement.TaxId is { } rtn) c.Item().Text($"RTN {rtn}").FontSize(9);
            });

            if (statement.Sales.Count == 0)
            {
                column.Item().Text("No tiene saldo pendiente. Todo lo facturado está pagado.")
                    .FontSize(11);
                return;
            }

            foreach (var sale in statement.Sales)
                column.Item().Element(item => Sale(item, sale, statement.Currency));

            column.Item().AlignRight().Width(240).Column(totals =>
            {
                totals.Item().BorderTop(1).BorderColor(Colors.Grey.Darken1).PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL ADEUDADO").Bold();
                    row.ConstantItem(110).AlignRight()
                        .Text(Money(statement.Total, statement.Currency)).Bold().FontSize(13);
                });

                if (statement.Overdue > 0)
                {
                    totals.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Vencido").FontSize(9);
                        row.ConstantItem(110).AlignRight()
                            .Text(Money(statement.Overdue, statement.Currency))
                            .FontSize(9).FontColor(Colors.Red.Darken2);
                    });
                }
            });
        });
    }

    /// <summary>Una factura con su saldo y, debajo, los abonos que se le han hecho.</summary>
    private static void Sale(IContainer container, StatementSaleDto sale, string currency)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(text =>
                    {
                        text.Span(sale.Number).SemiBold();

                        if (sale.WorkOrderNumber is { } orden)
                            text.Span($" · Orden {orden}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    c.Item().Text(text =>
                    {
                        text.Span($"{sale.SaleDate.ToLocalTime():dd/MM/yyyy} · {sale.BranchName}")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);

                        if (sale.DueDate is { } vence)
                        {
                            var etiqueta = sale.IsOverdue ? "venció" : "vence";
                            text.Span($" · {etiqueta} {vence.ToLocalTime():dd/MM/yyyy}")
                                .FontSize(8)
                                .FontColor(sale.IsOverdue ? Colors.Red.Darken2 : Colors.Grey.Darken1);
                        }
                        else
                        {
                            text.Span(" · sin fecha acordada")
                                .FontSize(8).FontColor(Colors.Grey.Darken1);
                        }
                    });
                });

                row.ConstantItem(90).AlignRight().Text(Money(sale.Total, currency)).FontSize(9);
            });

            foreach (var payment in sale.Payments)
            {
                column.Item().PaddingLeft(14).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span($"Abono {payment.PaidAt.ToLocalTime():dd/MM/yyyy}").FontSize(9);
                        text.Span($" · {PaymentLabel(payment.Method)}")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);

                        if (!string.IsNullOrWhiteSpace(payment.Reference))
                        {
                            text.Span($" · {payment.Reference}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                        }
                    });

                    row.ConstantItem(90).AlignRight()
                        .Text($"-{Money(payment.Amount, currency)}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }

            if (sale.Payments.Count == 0)
            {
                column.Item().PaddingLeft(14)
                    .Text("Sin abonos").FontSize(9).FontColor(Colors.Grey.Darken1);
            }

            column.Item().PaddingLeft(14).BorderTop(1).BorderColor(Colors.Grey.Lighten2)
                .PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().Text("Saldo").FontSize(9).SemiBold();
                    row.ConstantItem(90).AlignRight()
                        .Text(Money(sale.Balance, currency)).FontSize(9).SemiBold();
                });
        });
    }

    private static void Footer(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter()
                .Text("Resumen de cuenta. No es documento fiscal: la factura es la de cada venta.")
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
