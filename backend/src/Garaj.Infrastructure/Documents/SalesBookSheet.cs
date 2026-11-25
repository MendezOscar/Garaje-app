using System.Globalization;
using ClosedXML.Excel;
using Garaj.Application.Platform;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// El libro de ventas del mes, en hoja de cálculo: lo que se le entrega al contador.
/// </summary>
/// <remarks>
/// Solo las facturas con CAI: el libro es la cuenta de los números autorizados, y meterle
/// los comprobantes —que llevan numeración propia y no amparan nada— lo infla y confunde a
/// quien lo revisa.
///
/// Las columnas son las que pide el libro —fecha, número, CAI, cliente, RTN, gravado, exento,
/// ISV, total— y en ese orden. El servicio que se cobró va al final, porque es dato nuestro y
/// no del SAR: a quien revisa le importa el emisor, que es uno solo.
///
/// Los anulados salen en el libro, tachados y en cero: el número existió, y un número que no
/// aparece por ningún lado es justo lo que levanta la pregunta. Los que se tomaron y nunca
/// llegaron a documento van listados al pie con su explicación.
/// </remarks>
public static class SalesBookSheet
{
    private static readonly CultureInfo Cultura = new("es-HN");

    public static byte[] Render(SalesBookDto libro, PlatformSettingsDto emisor)
    {
        using var workbook = new XLWorkbook();
        var hoja = workbook.Worksheets.Add("Libro de ventas");

        hoja.Cell(1, 1).Value = emisor.LegalName ?? emisor.Name;
        hoja.Cell(1, 1).Style.Font.Bold = true;

        if (!string.IsNullOrWhiteSpace(emisor.TaxId))
            hoja.Cell(2, 1).Value = $"RTN {emisor.TaxId}";

        hoja.Cell(3, 1).Value = $"Libro de ventas del {Dia(libro.From)} al {Dia(libro.To)}";

        var fila = 5;

        string[] encabezados =
        [
            "Fecha", "Número", "CAI", "Cliente", "RTN",
            "Gravado", "Exento", "ISV", "Total", "Estado", "Servicio"
        ];

        for (var i = 0; i < encabezados.Length; i++)
        {
            var celda = hoja.Cell(fila, i + 1);
            celda.Value = encabezados[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        foreach (var r in libro.Rows)
        {
            fila++;

            hoja.Cell(fila, 1).Value = r.IssuedOn.ToDateTime(TimeOnly.MinValue);
            hoja.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            hoja.Cell(fila, 2).Value = r.Number;
            hoja.Cell(fila, 3).Value = r.Cai ?? "—";
            hoja.Cell(fila, 4).Value = r.CustomerName;
            hoja.Cell(fila, 5).Value = r.CustomerTaxId ?? "Consumidor final";

            // El anulado se enseña en cero: el número existió, el dinero no.
            hoja.Cell(fila, 6).Value = r.IsVoided ? 0 : r.Taxable;
            hoja.Cell(fila, 7).Value = r.IsVoided ? 0 : r.Exempt;
            hoja.Cell(fila, 8).Value = r.IsVoided ? 0 : r.Tax;
            hoja.Cell(fila, 9).Value = r.IsVoided ? 0 : r.Total;

            hoja.Cell(fila, 10).Value = r.IsVoided ? $"Anulado: {r.VoidReason}" : "Emitido";
            hoja.Cell(fila, 11).Value = r.ProductName ?? "—";

            if (r.IsVoided) hoja.Row(fila).Style.Font.Strikethrough = true;
        }

        fila += 2;
        hoja.Cell(fila, 5).Value = "Totales";
        hoja.Cell(fila, 5).Style.Font.Bold = true;
        hoja.Cell(fila, 6).Value = libro.Taxable;
        hoja.Cell(fila, 7).Value = libro.Exempt;
        hoja.Cell(fila, 8).Value = libro.Tax;
        hoja.Cell(fila, 9).Value = libro.Total;
        hoja.Range(fila, 6, fila, 9).Style.Font.Bold = true;

        hoja.Range(6, 6, fila, 9).Style.NumberFormat.Format = "#,##0.00";

        fila += 2;
        hoja.Cell(fila, 1).Value =
            $"{libro.Count} documentos, de los cuales {libro.VoidedCount} anulados.";

        if (libro.MissingNumbers.Count > 0)
        {
            fila += 2;
            hoja.Cell(fila, 1).Value = "Números tomados que no llegaron a documento:";
            hoja.Cell(fila, 1).Style.Font.Bold = true;

            foreach (var numero in libro.MissingNumbers)
            {
                fila++;
                hoja.Cell(fila, 1).Value = numero;
            }

            fila += 1;
            hoja.Cell(fila, 1).Value =
                "Se tomaron del rango y la emisión no llegó a guardarse. No ampararon ninguna "
                + "venta.";
        }

        hoja.Columns().AdjustToContents();

        using var memoria = new MemoryStream();
        workbook.SaveAs(memoria);

        return memoria.ToArray();
    }

    private static string Dia(DateOnly dia) => dia.ToString("dd 'de' MMMM 'de' yyyy", Cultura);
}
