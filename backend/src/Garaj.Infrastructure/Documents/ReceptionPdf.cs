using System.Globalization;
using Garaj.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// La hoja de recepción en PDF: con cuánto combustible entró el vehículo, qué golpes traía,
/// qué dejó el cliente adentro y la firma de quien lo entregó.
/// </summary>
/// <remarks>
/// Es la hoja que decide una discusión, y por eso vale mandársela al cliente el mismo día: lo
/// que se anotó delante de él, firmado por él, en su teléfono. Si el daño se discute un mes
/// después, ya lo tiene.
/// </remarks>
/// <summary>
/// Lo que la ficha necesita saber, en un solo sitio. No se pasa la orden entera: la ficha la
/// arma también la cotización, que no tiene por qué cargar con el detalle de la orden.
/// </summary>
public sealed record DatosDeRecepcion(
    string Number,
    string BranchName,
    string VehicleLabel,
    string? Plate,
    string CustomerName,
    string CustomerPhone,
    int? Mileage,
    DateTimeOffset OpenedAt,
    string? Description,
    FuelLevel FuelLevel,
    string? Damages,
    string? Belongings,
    string? Notes,
    string? DeliveredByName,
    string? ReceivedByName);

public static class ReceptionPdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    /// <param name="signature">PNG de la firma, o null si no firmó.</param>
    /// <param name="photos">Fotos de la orden, ya descargadas. Van al final.</param>
    public static byte[] Render(
        DatosDeRecepcion ficha,
        string tenantName, string? legalName, string? phone, string? taxId,
        byte[]? logo = null, byte[]? signature = null, IReadOnlyList<byte[]>? photos = null)
        => Build(ficha, tenantName, legalName, phone, taxId, logo, signature, photos).GeneratePdf();

    /// <summary>El documento sin generar, para poder coserlo delante de otro.</summary>
    public static IDocument Build(
        DatosDeRecepcion ficha,
        string tenantName, string? legalName, string? phone, string? taxId,
        byte[]? logo = null, byte[]? signature = null, IReadOnlyList<byte[]>? photos = null)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(h => Header(h, ficha, tenantName, legalName, phone, taxId, logo));
                page.Content().PaddingVertical(1, Unit.Centimetre)
                    .Element(c => Content(c, ficha, signature, photos ?? []));
                page.Footer().AlignCenter().Text(
                        "Esta hoja describe cómo entró el vehículo al taller. "
                        + "Lo que no está anotado aquí, no venía así.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void Header(
        IContainer container, DatosDeRecepcion ficha,
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
                column.Item().Text(ficha.BranchName).FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(200).Column(column =>
            {
                column.Item().AlignRight().Text("FICHA DE RECEPCIÓN").FontSize(13).Bold();
                column.Item().AlignRight().Text(ficha.Number).FontSize(11);
                column.Item().AlignRight()
                    .Text($"Recibido: {ficha.OpenedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Culture)}")
                    .FontSize(9);
            });
        });
    }

    private static void Content(
        IContainer container, DatosDeRecepcion ficha,
        byte[]? signature, IReadOnlyList<byte[]> photos)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            column.Item().Element(c => Datos(c, ficha));

            column.Item().Element(c => Bloque(c, "GOLPES Y RAYONES QUE YA TRAÍA",
                ficha.Damages, "Ninguno anotado al recibirlo."));

            column.Item().Element(c => Bloque(c, "LO QUE DEJÓ EL CLIENTE",
                ficha.Belongings, "Nada anotado."));

            if (!string.IsNullOrWhiteSpace(ficha.Description))
                column.Item().Element(c => Bloque(c, "MOTIVO DE INGRESO", ficha.Description, ""));

            if (!string.IsNullOrWhiteSpace(ficha.Notes))
                column.Item().Element(c => Bloque(c, "OTRAS NOTAS", ficha.Notes, ""));

            column.Item().Element(c => Firma(c, ficha, signature));

            if (photos.Count > 0)
                column.Item().Element(c => Fotos(c, photos));
        });
    }

    private static void Datos(IContainer container, DatosDeRecepcion ficha)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text(ficha.VehicleLabel).FontSize(13).Bold();

            var placa = string.IsNullOrWhiteSpace(ficha.Plate) ? "sin placa" : $"Placa {ficha.Plate}";
            var km = ficha.Mileage is { } m ? $" · {m:N0} km" : string.Empty;
            column.Item().Text($"{placa}{km}").FontSize(10);

            column.Item().Text($"Cliente: {ficha.CustomerName} · {ficha.CustomerPhone}").FontSize(10);
            column.Item().Text($"Combustible al recibirlo: {Combustible(ficha.FuelLevel)}").FontSize(10);

            if (!string.IsNullOrWhiteSpace(ficha.ReceivedByName))
                column.Item().Text($"Lo recibió: {ficha.ReceivedByName}").FontSize(10);
        });
    }

    private static void Bloque(IContainer container, string titulo, string? texto, string vacio)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text(titulo).FontSize(9).Bold().FontColor(Colors.Grey.Darken2);

            var contenido = string.IsNullOrWhiteSpace(texto) ? vacio : texto!.Trim();
            if (string.IsNullOrWhiteSpace(contenido)) return;

            column.Item().Text(contenido)
                .FontSize(10)
                .FontColor(string.IsNullOrWhiteSpace(texto) ? Colors.Grey.Darken1 : Colors.Grey.Darken4);
        });
    }

    private static void Firma(IContainer container, DatosDeRecepcion ficha, byte[]? signature)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("FIRMA DE QUIEN ENTREGÓ EL VEHÍCULO")
                .FontSize(9).Bold().FontColor(Colors.Grey.Darken2);

            if (signature is not null)
                column.Item().Height(70).AlignLeft().Image(signature).FitHeight();
            else
                column.Item().PaddingTop(28).Width(220).BorderBottom(1)
                    .BorderColor(Colors.Grey.Darken1);

            var nombre = string.IsNullOrWhiteSpace(ficha.DeliveredByName)
                ? "No se anotó quién lo entregó."
                : ficha.DeliveredByName!;
            column.Item().Text(nombre).FontSize(10);
        });
    }

    private static void Fotos(IContainer container, IReadOnlyList<byte[]> photos)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text("CÓMO ENTRÓ").FontSize(9).Bold().FontColor(Colors.Grey.Darken2);

            foreach (var fila in photos.Chunk(2))
            {
                column.Item().Row(row =>
                {
                    row.Spacing(8);
                    foreach (var foto in fila)
                        row.RelativeItem().Height(150).Image(foto).FitArea();

                    // Una foto sola no se estira a la hoja entera: quedaría enorme al lado de
                    // las que van en pareja.
                    if (fila.Length == 1) row.RelativeItem();
                });
            }
        });
    }

    private static string Combustible(FuelLevel level) => level switch
    {
        FuelLevel.Empty => "vacío",
        FuelLevel.Quarter => "un cuarto",
        FuelLevel.Half => "la mitad",
        FuelLevel.ThreeQuarters => "tres cuartos",
        FuelLevel.Full => "lleno",
        _ => "no se anotó"
    };
}
