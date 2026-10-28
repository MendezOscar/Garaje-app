using Garaj.Domain.Enums;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// El afiche del enlace de citas: una hoja para imprimir y pegar en el mostrador.
///
/// No es un documento del negocio como la factura o la ficha de recepción —aquí no hay nada
/// que probar después—: es publicidad del taller, y por eso el QR ocupa media página y el
/// texto es lo poco que alguien lee de pie frente a un mostrador.
/// </summary>
public static class BookingPosterPdf
{
    /// <param name="link">El enlace público de citas, ya armado.</param>
    /// <param name="kind">Vehículos o equipos: cambia las palabras, no el afiche.</param>
    public static byte[] Render(
        string tenantName, string link, WorkshopKind kind,
        string? phone = null, string? address = null, byte[]? logo = null)
    {
        var activo = kind == WorkshopKind.Equipment ? "su equipo" : "su vehículo";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(1.6f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(12).FontColor(Colors.Grey.Darken4));

                // Un afiche es de una hoja: con logo el contenido crecía y se iba a una
                // segunda página, que en la pared no sirve de nada.
                page.Content().ScaleToFit().Column(column =>
                {
                    column.Item().Element(c => Encabezado(c, tenantName, logo));

                    column.Item().PaddingTop(18).AlignCenter()
                        .Text("Pida su cita desde su teléfono")
                        .FontSize(32).Bold().FontColor("#1F6FEB");

                    column.Item().PaddingTop(6).AlignCenter()
                        .Text("Sin llamar, sin esperar y sin crear cuenta.")
                        .FontSize(15).FontColor(Colors.Grey.Darken2);

                    column.Item().PaddingTop(22).AlignCenter().Element(c => Codigo(c, link));

                    column.Item().PaddingTop(10).AlignCenter()
                        .Text("Apunte la cámara de su teléfono al código")
                        .FontSize(14).SemiBold();

                    column.Item().PaddingTop(26).Element(c => Pasos(c, activo));

                    column.Item().PaddingTop(20).Element(c => Pie(c, link, phone, address));
                });
            });
        }).GeneratePdf();
    }

    private static void Encabezado(IContainer container, string tenantName, byte[]? logo)
    {
        container.AlignCenter().Row(row =>
        {
            row.AutoItem().AlignMiddle().Text(tenantName).FontSize(22).Bold();

            if (logo is not null)
                row.AutoItem().PaddingLeft(12).Width(56).Height(46).AlignMiddle().Image(logo).FitArea();
        });
    }

    /// <summary>El QR, grande y con marco: se escanea de lejos y se recorta derecho.</summary>
    private static void Codigo(IContainer container, string link)
    {
        // Corrección M: aguanta hasta un 15% de daño, que es lo que sufre un papel pegado en
        // la pared de un taller.
        using var datos = QRCodeGenerator.GenerateQrCode(link, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(datos).GetGraphic(20);

        container.Border(2).BorderColor("#1F6FEB").Padding(14)
            .Width(290).Height(290).Image(png).FitArea();
    }

    private static void Pasos(IContainer container, string activo)
    {
        container.Row(row =>
        {
            row.Spacing(14);
            Paso(row, "1", "Escanee el código", "Con la cámara, como quien lee un menú.");
            Paso(row, "2", "Diga qué necesita", $"Y qué día puede traer {activo}.");
            Paso(row, "3", "Le confirmamos", "Le avisamos por el mismo enlace si ese día le cabe.");
        });
    }

    private static void Paso(RowDescriptor row, string numero, string titulo, string detalle)
        => row.RelativeItem().Background("#F3F6FB").Padding(14).Column(column =>
        {
            column.Item().Text(numero).FontSize(26).Bold().FontColor("#1F6FEB");
            column.Item().PaddingTop(4).Text(titulo).FontSize(13).Bold();
            column.Item().PaddingTop(2).Text(detalle).FontSize(11).FontColor(Colors.Grey.Darken2);
        });

    private static void Pie(IContainer container, string link, string? phone, string? address)
        => container.BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(12).Column(column =>
        {
            column.Item().AlignCenter()
                .Text("¿No le abre el código? Escriba esta dirección en su navegador:")
                .FontSize(10).FontColor(Colors.Grey.Darken1);

            column.Item().PaddingTop(2).AlignCenter().Text(link).FontSize(12).SemiBold();

            var contacto = string.Join("  ·  ", new[] { phone, address }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            if (contacto.Length > 0)
                column.Item().PaddingTop(8).AlignCenter().Text(contacto)
                    .FontSize(11).FontColor(Colors.Grey.Darken2);
        });
}
