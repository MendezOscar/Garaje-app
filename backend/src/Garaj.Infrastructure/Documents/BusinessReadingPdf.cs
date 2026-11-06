using System.Globalization;
using Garaj.Application.Assistant;
using Garaj.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// La lectura del negocio, en una página que el dueño puede guardar o enseñar.
/// </summary>
/// <remarks>
/// El contenido llega en Markdown sencillo y se pinta a mano, sin librería: solo hay tres
/// formas —título con <c>##</c>, viñeta con <c>-</c> y negrita con <c>**</c>—, que es
/// justamente lo que el prompt le pide al modelo. Meter un analizador de Markdown entero para
/// tres marcas sería traer un problema nuevo a cambio de nada.
/// </remarks>
public static class BusinessReadingPdf
{
    private static readonly CultureInfo Culture = new("es-HN");

    public static byte[] Render(string tenantName, BusinessReadingDto lectura) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Column(col =>
                {
                    col.Item().Text(tenantName).FontSize(16).SemiBold();
                    col.Item().Text(
                            lectura.Kind == BusinessReadingKind.Weekly
                                ? $"Lectura de la semana · {Dia(lectura.PeriodFrom)} al {Dia(lectura.PeriodTo)}"
                                : $"Cierre del mes · {Dia(lectura.PeriodFrom)} al {Dia(lectura.PeriodTo)}")
                        .FontSize(10).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    foreach (var linea in (lectura.Content ?? "").Replace("\r", "").Split('\n'))
                    {
                        var texto = linea.Trim();

                        if (texto.Length == 0)
                        {
                            col.Item().Height(6);
                            continue;
                        }

                        if (texto.StartsWith("## "))
                        {
                            col.Item().PaddingTop(10).Text(texto[3..])
                                .FontSize(12).SemiBold().FontColor(Colors.Blue.Darken2);
                            continue;
                        }

                        if (texto.StartsWith("# "))
                        {
                            col.Item().PaddingTop(10).Text(texto[2..]).FontSize(13).SemiBold();
                            continue;
                        }

                        if (texto.StartsWith("- ") || texto.StartsWith("* "))
                        {
                            col.Item().PaddingLeft(10).Text(t =>
                            {
                                t.Span("•  ");
                                Negritas(t, texto[2..]);
                            });
                            continue;
                        }

                        col.Item().Text(t => Negritas(t, texto));
                    }
                });

                page.Footer().PaddingTop(10).Column(col =>
                {
                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    col.Item().PaddingTop(6).Text(
                            "Lectura armada por GarajApp con los números del taller. Las cifras salen de " +
                            "sus reportes; el texto las explica.")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

    /// <summary>Pinta <c>**así**</c> en negrita, que es la única marca dentro de una línea.</summary>
    private static void Negritas(TextDescriptor t, string texto)
    {
        var partes = texto.Split("**");

        for (var i = 0; i < partes.Length; i++)
        {
            if (partes[i].Length == 0) continue;

            // Las posiciones impares son lo que iba entre los dos pares de asteriscos.
            if (i % 2 == 1) t.Span(partes[i]).SemiBold();
            else t.Span(partes[i]);
        }
    }

    private static string Dia(DateOnly d) => d.ToString("dd 'de' MMMM", Culture);
}
