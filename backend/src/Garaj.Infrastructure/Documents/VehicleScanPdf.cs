using System.Globalization;
using Garaj.Application.VehicleScans;
using Garaj.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// El informe de una lectura con el adaptador OBD2: qué códigos reportó el vehículo, cuándo
/// y con qué se leyó.
/// </summary>
/// <remarks>
/// Lo lee el cliente, no el técnico —viaja adjunto a la cotización por WhatsApp—, así que
/// está escrito para alguien que no sabe lo que es un DTC: la tabla dice el código y al lado
/// qué significa en español, y al pie se explica qué son las tres memorias y por qué el
/// taller no apaga la luz de una vez.
///
/// Se guarda como archivo, a diferencia del PDF de la cotización, porque es evidencia: lo
/// que el carro reportó ese día tiene que poder enseñarse igual dentro de seis meses, aunque
/// para entonces los códigos se hayan borrado del vehículo.
/// </remarks>
public static class VehicleScanPdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    /// <param name="logo">PNG del logo del taller, o null. Como en la cotización: va en bytes
    /// porque el documento se abre sin conexión.</param>
    public static byte[] Render(
        VehicleScanDto scan,
        string tenantName, string? legalName, string? phone, string? taxId,
        string workOrderNumber, string vehicleLabel, string? plate, string customerName,
        byte[]? logo = null)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(h => Header(
                    h, scan, tenantName, legalName, phone, taxId, workOrderNumber, logo));

                page.Content().PaddingVertical(1, Unit.Centimetre).Element(c => Content(
                    c, scan, vehicleLabel, plate, customerName));

                page.Footer().Element(Footer);
            });
        }).GeneratePdf();
    }

    private static void Header(
        IContainer container, VehicleScanDto scan,
        string tenantName, string? legalName, string? phone, string? taxId,
        string workOrderNumber, byte[]? logo)
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
            });

            row.ConstantItem(200).Column(column =>
            {
                column.Item().AlignRight().Text("INFORME DE DIAGNÓSTICO").FontSize(13).Bold();
                column.Item().AlignRight().Text($"Orden {workOrderNumber}").FontSize(11);
                column.Item().AlignRight()
                    .Text($"Lectura: {scan.ScannedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Culture)}")
                    .FontSize(9);
            });
        });
    }

    private static void Content(
        IContainer container, VehicleScanDto scan,
        string vehicleLabel, string? plate, string customerName)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(datos =>
            {
                datos.Spacing(2);
                datos.Item().Text(vehicleLabel).FontSize(12).Bold();
                if (!string.IsNullOrWhiteSpace(plate))
                    datos.Item().Text($"Placa: {plate}").FontSize(10);
                datos.Item().Text($"Cliente: {customerName}").FontSize(10);

                // El VIN que dictó la computadora del carro, no el copiado a mano de la ficha.
                if (!string.IsNullOrWhiteSpace(scan.Vin))
                    datos.Item().Text($"VIN leído del vehículo: {scan.Vin}").FontSize(9);

                if (scan.Mileage is { } km)
                    datos.Item().Text($"Kilometraje: {km.ToString("N0", Culture)} km").FontSize(9);

                datos.Item().Text($"Realizó la lectura: {scan.ScannedByName}").FontSize(9);
            });

            if (scan.Codes.Count == 0)
            {
                // Una lectura limpia es un resultado, no un informe vacío: es lo que se le
                // enseña al cliente que llegó diciendo que el carro «anda raro».
                column.Item().PaddingTop(6).Text("El vehículo no reportó ningún código de falla.")
                    .FontSize(12).Bold();

                column.Item().Text(
                        "Se leyeron las tres memorias de la computadora del vehículo —confirmadas, "
                        + "pendientes y permanentes— y las tres salieron sin registros. Eso no "
                        + "descarta fallas mecánicas que la computadora no vigila.")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            }
            else
            {
                column.Item().Text(
                        scan.Codes.Count == 1
                            ? "El vehículo reportó 1 código de falla:"
                            : $"El vehículo reportó {scan.Codes.Count} códigos de falla:")
                    .FontSize(11).Bold();

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(60);
                        c.ConstantColumn(80);
                        c.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(Cabecera).Text("Código");
                        header.Cell().Element(Cabecera).Text("Estado");
                        header.Cell().Element(Cabecera).Text("Qué significa");
                    });

                    foreach (var code in scan.Codes)
                    {
                        table.Cell().Element(Celda).Text(code.Code).Bold();
                        table.Cell().Element(Celda).Text(EstadoLegible(code.Status)).FontSize(9);
                        table.Cell().Element(Celda).Text(
                                code.Description
                                // Un código de fabricante sin descripción no se disfraza: se
                                // dice que la da el escáner de la marca y por qué.
                                ?? (code.IsGeneric
                                    ? "Sin descripción disponible."
                                    : "Código propio del fabricante: su significado lo da el escáner de la marca."))
                            .FontSize(9);
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(scan.Notes))
            {
                column.Item().PaddingTop(4).Text("Observaciones del taller").FontSize(10).Bold();
                column.Item().Text(scan.Notes!).FontSize(9);
            }

            column.Item().PaddingTop(10).Text("Qué quiere decir cada estado").FontSize(10).Bold();

            column.Item().Text(
                    "Confirmada: el vehículo vio la falla repetirse y encendió la luz del motor. "
                    + "Pendiente: la vio una sola vez y espera confirmarla; puede desaparecer sola. "
                    + "Permanente: la grabó el propio vehículo y solo se quita cuando él mismo "
                    + "comprueba que la falla ya no está.")
                .FontSize(8.5f).FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(6).Text(
                    "Un código dice qué reportó la computadora del vehículo, no qué pieza hay que "
                    + "cambiar: señala dónde buscar. Borrar el código apaga la luz pero también "
                    + "borra la evidencia, así que el taller lo hace después de reparar, no antes.")
                .FontSize(8.5f).FontColor(Colors.Grey.Darken2);
        });
    }

    private static void Footer(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingTop(4).Text(text =>
            {
                text.Span("Lectura tomada del conector de diagnóstico del vehículo").FontSize(8);
                text.Span(" · ").FontSize(8);
                text.Span("Página ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" de ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        });
    }

    private static string EstadoLegible(DtcStatus status) => status switch
    {
        DtcStatus.Pending => "Pendiente",
        DtcStatus.Permanent => "Permanente",
        _ => "Confirmada"
    };

    private static IContainer Cabecera(IContainer container) => container
        .BorderBottom(1).BorderColor(Colors.Grey.Darken1)
        .PaddingVertical(4).DefaultTextStyle(x => x.SemiBold().FontSize(9));

    private static IContainer Celda(IContainer container) => container
        .BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingRight(4);
}
