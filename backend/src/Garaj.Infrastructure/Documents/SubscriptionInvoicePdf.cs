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

    // Los colores salen del logotipo, no de la paleta de QuestPDF: el papel y la marca tienen
    // que verse de la misma familia.
    private const string Azul = "#0d2a7a";
    private const string Tinta = "#201e1d";
    private const string Gris = "#605d5d";
    private const string Fondo = "#f3f5fb";
    private const string Linea = "#dfe3ee";

    /// <summary>
    /// El logotipo del emisor, incrustado en el ensamblado.
    /// </summary>
    /// <remarks>
    /// No es el logo de un taller —ese se sube y se guarda en el bucket— sino el nuestro, que
    /// es uno solo y no cambia de un despliegue a otro. Leerlo del binario evita que el papel
    /// que ve un cliente dependa de que una descarga salga bien.
    /// </remarks>
    private static readonly byte[] Marca = LeerMarca();

    private static byte[] LeerMarca()
    {
        using var recurso = typeof(SubscriptionInvoicePdf).Assembly
            .GetManifestResourceStream("Garaj.Marca.png")
            ?? throw new InvalidOperationException("Falta el logotipo del emisor en el ensamblado.");

        using var memoria = new MemoryStream();
        recurso.CopyTo(memoria);

        return memoria.ToArray();
    }

    public static byte[] Render(SubscriptionInvoiceDto factura, PlatformSettingsDto emisor)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Tinta));

                page.Header().Element(header => Header(header, factura, emisor));
                page.Content().PaddingVertical(0.7f, Unit.Centimetre)
                    .Element(content => Content(content, factura));
                page.Footer().Element(footer => Footer(footer, factura, emisor));
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(
        IContainer container, SubscriptionInvoiceDto factura, PlatformSettingsDto emisor)
    {
        container.Column(todo =>
        {
            todo.Item().Row(row =>
            {
                row.RelativeItem().PaddingRight(12).Column(column =>
                {
                    // El logotipo lleva el nombre escrito, así que el nombre no se repite
                    // debajo: dos veces lo mismo es ruido en el renglón que todos miran.
                    column.Item().PaddingBottom(8).Width(118).Height(42).Image(Marca).FitArea();

                    if (!string.IsNullOrWhiteSpace(emisor.LegalName))
                        column.Item().Text(emisor.LegalName).FontSize(9).SemiBold();
                    if (!string.IsNullOrWhiteSpace(emisor.TaxId))
                        column.Item().Text($"RTN {emisor.TaxId}").FontSize(8.5f);
                    if (!string.IsNullOrWhiteSpace(emisor.Address))
                        column.Item().Text(emisor.Address).FontSize(8.5f).FontColor(Gris);

                    var contacto = string.Join(
                        " · ",
                        new[] { emisor.Phone, emisor.Email }
                            .Where(x => !string.IsNullOrWhiteSpace(x)));

                    if (contacto.Length > 0)
                        column.Item().Text(contacto).FontSize(8.5f).FontColor(Gris);
                });

                row.ConstantItem(215).AlignRight().Column(column =>
                {
                    column.Item().Text(factura.Fiscal ? "FACTURA" : "COMPROBANTE DE PAGO")
                        .FontSize(10).Bold().FontColor(Azul).LetterSpacing(0.14f);
                    column.Item().Text(factura.Number).FontSize(15).SemiBold();
                    column.Item().PaddingTop(1)
                        .Text($"Emitida el {factura.IssuedOn.ToString("dd/MM/yyyy", Culture)}")
                        .FontSize(8.5f).FontColor(Gris);

                    if (factura.IsVoided)
                    {
                        column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Red.Darken2)
                            .PaddingVertical(2).PaddingHorizontal(8)
                            .Text("ANULADA").Bold().FontSize(10)
                            .FontColor(Colors.Red.Darken2).LetterSpacing(0.14f);
                    }

                    // Lo que el régimen pide impreso, y solo cuando de verdad hay CAI. En su
                    // propio recuadro, porque es el bloque que busca quien revisa el papel.
                    if (factura.Fiscal)
                    {
                        column.Item().PaddingTop(8).Background(Fondo).Padding(7).Column(caja =>
                        {
                            caja.Spacing(3);
                            DatoFiscal(caja, "CAI", factura.FiscalCai);
                            DatoFiscal(caja, "Rango autorizado", factura.FiscalRangeText);
                            DatoFiscal(caja, "Fecha límite de emisión",
                                factura.FiscalIssueDeadline?.ToLocalTime().ToString(
                                    "dd/MM/yyyy", Culture));
                        });
                    }
                });
            });

            todo.Item().PaddingTop(12).LineHorizontal(1.5f).LineColor(Azul);
        });
    }

    /// <summary>Un dato del CAI: el rótulo encima y el valor debajo, que así no se parte.</summary>
    private static void DatoFiscal(ColumnDescriptor column, string rotulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;

        column.Item().Text(rotulo).FontSize(7).FontColor(Gris).LetterSpacing(0.08f);
        column.Item().Text(valor).FontSize(8);
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
                    Rotulo(c, "Cliente");
                    c.Item().Text(factura.CustomerLegalName ?? factura.CustomerName).SemiBold();

                    if (factura.CustomerLegalName is not null)
                        c.Item().Text(factura.CustomerName).FontSize(9).FontColor(Gris);

                    // En la factura fiscal el RTN del cliente es lo que le permite deducirla.
                    if (factura.Fiscal)
                    {
                        c.Item().Text(factura.CustomerTaxId is { } rtn
                            ? $"RTN {rtn}"
                            : "Consumidor final").FontSize(9);
                    }
                });

                // El mismo RTN cobra tres servicios: el papel tiene que decir por cuál se
                // paga sin que haya que leer las líneas.
                if (factura.ProductName is { Length: > 0 } || factura.PaymentReference is { Length: > 0 })
                {
                    row.ConstantItem(170).AlignRight().Column(c =>
                    {
                        if (factura.ProductName is { Length: > 0 } servicio)
                        {
                            Rotulo(c, "Servicio");
                            c.Item().Text(servicio).SemiBold();
                        }

                        if (factura.PaymentReference is { Length: > 0 } referencia)
                        {
                            Rotulo(c, "Referencia del pago",
                                factura.ProductName is { Length: > 0 } ? 6 : 0);
                            c.Item().Text(referencia).FontSize(9);
                        }
                    });
                }
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

            column.Item().AlignRight().Width(250).Column(totals =>
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

                totals.Item().PaddingTop(6).Background(Fondo).PaddingVertical(6)
                    .PaddingHorizontal(8).Row(row =>
                    {
                        row.RelativeItem().AlignMiddle().Text("TOTAL")
                            .Bold().FontSize(10).FontColor(Azul).LetterSpacing(0.12f);
                        row.ConstantItem(120).AlignRight()
                            .Text(Money(factura.Total, factura.Currency))
                            .Bold().FontSize(14).FontColor(Azul);
                    });
            });

            // El valor en letras es obligatorio en la factura fiscal, y de paso estorba la
            // corrección a mano de la cifra.
            if (factura.Fiscal)
            {
                column.Item().PaddingTop(2)
                    .Text($"Son: {NumeroEnLetras.Moneda(factura.Total, factura.Currency)}")
                    .FontSize(9).SemiBold();
            }

            if (!string.IsNullOrWhiteSpace(factura.Note))
                column.Item().Text(factura.Note).FontSize(9).Italic().FontColor(Gris);
        });
    }

    private static void Footer(
        IContainer container, SubscriptionInvoiceDto factura, PlatformSettingsDto emisor)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(6).LineHorizontal(0.75f).LineColor(Linea);

            column.Item().Text(factura.Fiscal
                    ? "Documento fiscal autorizado por el SAR."
                    : "Este documento es un comprobante de pago y no sustituye una factura fiscal.")
                .FontSize(8).FontColor(Gris);

            // Qué servicio se cobró, cuando se sabe: el mismo RTN factura GarajApp, StockRuta
            // y Profeapp, y quien recibe el documento tiene que ver por cuál paga.
            column.Item().Text(factura.ProductName is { Length: > 0 } servicio
                    ? $"{emisor.Name} · {servicio}"
                    : emisor.Name)
                .FontSize(8).FontColor(Gris);
        });
    }

    /// <summary>El rótulo de un dato: pequeño, gris y espaciado, para que no pese.</summary>
    private static void Rotulo(ColumnDescriptor column, string texto, float arriba = 0) =>
        column.Item().PaddingTop(arriba).PaddingBottom(1).Text(texto)
            .FontSize(7.5f).FontColor(Gris).LetterSpacing(0.08f);

    private static void Total(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingHorizontal(8).PaddingVertical(1).Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(9).FontColor(Gris);
            row.ConstantItem(120).AlignRight().Text(value).FontSize(9);
        });

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Azul).PaddingVertical(5).PaddingHorizontal(6)
            .DefaultTextStyle(x => x.SemiBold().FontSize(8.5f).FontColor("#ffffff")
                .LetterSpacing(0.06f));

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.75f).BorderColor(Linea)
            .PaddingVertical(6).PaddingHorizontal(6).DefaultTextStyle(x => x.FontSize(9));

    private static string Money(decimal value, string currency) =>
        $"{(currency == "HNL" ? "L" : currency)} {value.ToString("N2", Culture)}";

    /// <summary>Sin decimales cuando no hacen falta: «1» se lee mejor que «1.00».</summary>
    private static string Quantity(decimal value) =>
        value == Math.Floor(value) ? value.ToString("0", Culture) : value.ToString("0.##", Culture);
}
