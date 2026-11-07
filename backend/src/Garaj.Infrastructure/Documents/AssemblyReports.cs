using System.Globalization;
using ClosedXML.Excel;
using Garaj.Application.Assembly;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// Los dos reportes del armado: el que se le manda al contratista para que pague, y el que
/// usa el dueño para pagarles a los técnicos.
/// </summary>
/// <remarks>
/// Las columnas del de facturación son las de la hoja de cálculo que el contratista ya recibe
/// hoy —fecha, serie, modelo, técnico, bodega, cobro—, en ese orden y con esos nombres. No es
/// nostalgia: el que tiene que aceptar el reporte es el que paga, y estrenarle un formato
/// abre una conversación que nadie pidió.
///
/// En el de pago no aparece por ningún lado lo que el taller cobra. Se imprime, se firma y
/// queda en manos del técnico.
/// </remarks>
public static class AssemblyReports
{
    private static readonly CultureInfo Cultura = new("es-HN");

    public static byte[] BillingExcel(AssemblyBillingReportDto reporte)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Motos armadas");

        hoja.Cell(1, 1).Value = reporte.TenantName;
        hoja.Cell(1, 1).Style.Font.Bold = true;
        hoja.Cell(2, 1).Value = $"Motos armadas para {reporte.ContractorName}";
        hoja.Cell(3, 1).Value = $"Del {Dia(reporte.From)} al {Dia(reporte.To)}";

        var fila = 5;
        string[] encabezados = ["Fecha", "Serie", "Modelo", "Técnico", "Bodega", "Cobro"];

        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = hoja.Cell(fila, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");
        }

        foreach (var r in reporte.Rows)
        {
            fila++;
            hoja.Cell(fila, 1).Value = r.AssembledOn.ToDateTime(TimeOnly.MinValue);
            hoja.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";

            // Como texto: una serie que empieza con ceros los pierde si Excel la lee como
            // número, y una serie sin sus ceros no es la serie.
            hoja.Cell(fila, 2).SetValue(r.Serial).Style.NumberFormat.Format = "@";

            hoja.Cell(fila, 3).Value = r.ModelName;
            hoja.Cell(fila, 4).Value = r.TechnicianName;
            hoja.Cell(fila, 5).Value = r.WarehouseName;
            hoja.Cell(fila, 6).Value = r.Charge;
            hoja.Cell(fila, 6).Style.NumberFormat.Format = "#,##0.00";
        }

        fila++;
        hoja.Cell(fila, 5).Value = $"{reporte.Count} motos";
        hoja.Cell(fila, 5).Style.Font.Bold = true;
        hoja.Cell(fila, 6).Value = reporte.Total;
        hoja.Cell(fila, 6).Style.Font.Bold = true;
        hoja.Cell(fila, 6).Style.NumberFormat.Format = "#,##0.00";

        hoja.Columns().AdjustToContents();

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }

    public static byte[] PayExcel(AssemblyPayReportDto reporte)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Pago a técnicos");

        hoja.Cell(1, 1).Value = reporte.TenantName;
        hoja.Cell(1, 1).Style.Font.Bold = true;
        hoja.Cell(2, 1).Value = "Pago por motos armadas";
        hoja.Cell(3, 1).Value = $"Del {Dia(reporte.From)} al {Dia(reporte.To)}";

        var fila = 5;
        string[] encabezados = ["Técnico", "Motos", "A pagar"];

        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = hoja.Cell(fila, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");
        }

        foreach (var l in reporte.Lines)
        {
            fila++;
            hoja.Cell(fila, 1).Value = l.TechnicianName;
            hoja.Cell(fila, 2).Value = l.Count;
            hoja.Cell(fila, 3).Value = l.Pay;
            hoja.Cell(fila, 3).Style.NumberFormat.Format = "#,##0.00";
        }

        fila++;
        hoja.Cell(fila, 1).Value = "Total";
        hoja.Cell(fila, 1).Style.Font.Bold = true;
        hoja.Cell(fila, 2).Value = reporte.Count;
        hoja.Cell(fila, 2).Style.Font.Bold = true;
        hoja.Cell(fila, 3).Value = reporte.Total;
        hoja.Cell(fila, 3).Style.Font.Bold = true;
        hoja.Cell(fila, 3).Style.NumberFormat.Format = "#,##0.00";

        hoja.Columns().AdjustToContents();

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }

    public static byte[] BillingPdf(AssemblyBillingReportDto reporte) =>
        Document.Create(container => container.Page(page =>
        {
            Pagina(page);

            page.Header().Element(h => Encabezado(
                h, reporte.TenantName,
                $"Motos armadas para {reporte.ContractorName}",
                reporte.From, reporte.To));

            page.Content().PaddingVertical(12).Element(c => c.Table(tabla =>
            {
                tabla.ColumnsDefinition(col =>
                {
                    col.ConstantColumn(58);
                    col.RelativeColumn(2.4f);
                    col.RelativeColumn(1.4f);
                    col.RelativeColumn(1.6f);
                    col.RelativeColumn(1.4f);
                    col.ConstantColumn(70);
                });

                Cabecera(tabla, "Fecha", "Serie", "Modelo", "Técnico", "Bodega", "Cobro");

                foreach (var r in reporte.Rows)
                {
                    tabla.Cell().Element(Celda).Text(Dia(r.AssembledOn));
                    tabla.Cell().Element(Celda).Text(r.Serial).FontFamily(Fonts.Consolas).FontSize(8);
                    tabla.Cell().Element(Celda).Text(r.ModelName);
                    tabla.Cell().Element(Celda).Text(r.TechnicianName);
                    tabla.Cell().Element(Celda).Text(r.WarehouseName);
                    tabla.Cell().Element(Celda).AlignRight().Text(Plata(r.Charge));
                }

                tabla.Cell().ColumnSpan(4).Element(Pie).Text($"{reporte.Count} motos").Bold();
                tabla.Cell().Element(Pie).Text(string.Empty);
                tabla.Cell().Element(Pie).AlignRight().Text(Plata(reporte.Total)).Bold();
            }));

            page.Footer().Element(PiePagina);
        })).GeneratePdf();

    public static byte[] PayPdf(AssemblyPayReportDto reporte) =>
        Document.Create(container => container.Page(page =>
        {
            Pagina(page);

            page.Header().Element(h => Encabezado(
                h, reporte.TenantName, "Pago por motos armadas", reporte.From, reporte.To));

            page.Content().PaddingVertical(12).Column(columna =>
            {
                columna.Item().Table(tabla =>
                {
                    tabla.ColumnsDefinition(col =>
                    {
                        col.RelativeColumn(3);
                        col.ConstantColumn(60);
                        col.ConstantColumn(80);
                    });

                    Cabecera(tabla, "Técnico", "Motos", "A pagar");

                    foreach (var l in reporte.Lines)
                    {
                        tabla.Cell().Element(Celda).Text(l.TechnicianName);
                        tabla.Cell().Element(Celda).AlignRight().Text($"{l.Count}");
                        tabla.Cell().Element(Celda).AlignRight().Text(Plata(l.Pay));
                    }

                    tabla.Cell().Element(Pie).Text("Total").Bold();
                    tabla.Cell().Element(Pie).AlignRight().Text($"{reporte.Count}").Bold();
                    tabla.Cell().Element(Pie).AlignRight().Text(Plata(reporte.Total)).Bold();
                });

                // Se imprime y se firma: lo que queda en manos del técnico es esta hoja.
                columna.Item().PaddingTop(40).Row(fila =>
                {
                    foreach (var rotulo in new[] { "Entregó", "Recibió" })
                    {
                        fila.RelativeItem().PaddingHorizontal(20).Column(c =>
                        {
                            c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(4)
                                .Text(rotulo).FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    }
                });
            });

            page.Footer().Element(PiePagina);
        })).GeneratePdf();

    private static void Pagina(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken4));
    }

    private static void Encabezado(
        IContainer container, string taller, string titulo, DateOnly desde, DateOnly hasta) =>
        container.Column(columna =>
        {
            columna.Item().Text(taller).FontSize(13).Bold();
            columna.Item().Text(titulo).FontSize(11);
            columna.Item().Text($"Del {Dia(desde)} al {Dia(hasta)}")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
        });

    private static void Cabecera(TableDescriptor tabla, params string[] rotulos)
    {
        tabla.Header(header =>
        {
            foreach (var rotulo in rotulos)
            {
                header.Cell().Element(c => c
                        .BorderBottom(1).BorderColor(Colors.Grey.Medium)
                        .PaddingVertical(4).PaddingHorizontal(2))
                    .Text(rotulo).Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private static IContainer Celda(IContainer c) => c
        .BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
        .PaddingVertical(3).PaddingHorizontal(2);

    private static IContainer Pie(IContainer c) => c
        .BorderTop(1).BorderColor(Colors.Grey.Medium)
        .PaddingVertical(5).PaddingHorizontal(2);

    private static void PiePagina(IContainer container) =>
        container.AlignCenter().Text(x =>
        {
            x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
            x.Span(" de ").FontSize(8).FontColor(Colors.Grey.Medium);
            x.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
        });

    private static string Dia(DateOnly d) => d.ToString("dd/MM/yyyy", Cultura);

    private static string Plata(decimal v) => v.ToString("N2", Cultura);
}
