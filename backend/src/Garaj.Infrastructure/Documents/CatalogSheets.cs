using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Garaj.Application.Common;
using Garaj.Application.Inventory;
using Garaj.Application.Quotes;

namespace Garaj.Infrastructure.Documents;

/// <summary>
/// Las plantillas de Excel con que un taller carga su catálogo, y la lectura de las mismas.
/// </summary>
/// <remarks>
/// Un taller que arranca tiene su catálogo en un cuaderno o en una hoja de cálculo, y
/// pasarlo a mano son dos días de trabajo que nadie hace: por eso la carga existe. La
/// plantilla la genera este mismo archivo que después la lee, para que no puedan separarse.
///
/// Las columnas se buscan **por el nombre del encabezado**, no por su posición: alguien va a
/// mover una columna o a agregar la suya, y eso no tiene por qué romper la carga.
/// </remarks>
public static class CatalogSheets
{
    private static readonly CultureInfo Cultura = new("es-HN");

    /// <summary>Lo que se leyó de una fila: el dato, o el error que impide guardarla.</summary>
    public record Fila<T>(int Numero, string Key, string Name, T? Dato, string? Error);

    // ------------------------------------------------------------------ repuestos

    public static byte[] PlantillaDeRepuestos()
    {
        using var libro = new XLWorkbook();

        var hoja = libro.Worksheets.Add("Repuestos");
        Encabezados(hoja, [
            "SKU", "Nombre", "Descripción", "Marca", "Categoría", "Unidad",
            "Costo", "Precio de venta"
        ]);

        Ejemplo(hoja, 2, ["AC-15W40", "Aceite 15W40 litro", "Aceite de motor mineral",
            "Castrol", "Lubricantes", "lt", 95m, 140m]);
        Ejemplo(hoja, 3, ["FIL-A-203", "Filtro de aceite 203", null,
            "Fram", "Filtros", "u", 85m, 160m]);

        hoja.Columns().AdjustToContents();

        Instrucciones(libro, [
            "Llene la hoja «Repuestos», una línea por repuesto, y súbala desde Inventario.",
            "",
            "SKU — obligatorio. Es el código con que usted lo busca y lo dicta. Si ya existe uno",
            "con ese SKU, la carga le actualiza los datos en vez de duplicarlo.",
            "Nombre — obligatorio. Como quiere verlo en la orden y en la factura.",
            "Unidad — u, lt, jgo, par… Si la deja en blanco se guarda «u».",
            "Costo — lo que a usted le cuesta. Puede ir en cero y corregirse al darle entrada.",
            "Precio de venta — lo que le cobra al cliente.",
            "",
            "Las dos filas de ejemplo se pueden borrar o escribir encima.",
            "Los precios van en números, sin la L ni separador de miles."
        ]);

        return Guardar(libro);
    }

    public static IReadOnlyList<Fila<SavePartRequest>> LeerRepuestos(Stream archivo)
    {
        var (hoja, columnas) = Abrir(archivo, "Repuestos");
        var filas = new List<Fila<SavePartRequest>>();

        foreach (var fila in Usadas(hoja))
        {
            var sku = Texto(fila, columnas, "sku");
            var nombre = Texto(fila, columnas, "nombre");

            if (sku is null && nombre is null) continue;

            string? error = null;

            if (sku is null) error = "Falta el SKU.";
            else if (nombre is null) error = "Falta el nombre.";

            var costo = Numero(fila, columnas, "costo");
            var venta = Numero(fila, columnas, "precio de venta");

            if (error is null && costo is null && Tiene(fila, columnas, "costo"))
                error = "El costo no es un número.";
            if (error is null && venta is null && Tiene(fila, columnas, "precio de venta"))
                error = "El precio de venta no es un número.";
            if (error is null && (costo < 0 || venta < 0))
                error = "Los precios no pueden ser negativos.";

            filas.Add(new Fila<SavePartRequest>(
                fila.RowNumber(),
                sku ?? "",
                nombre ?? "",
                error is null
                    ? new SavePartRequest(
                        sku!,
                        nombre!,
                        Texto(fila, columnas, "descripción"),
                        Texto(fila, columnas, "marca"),
                        Texto(fila, columnas, "categoría"),
                        Texto(fila, columnas, "unidad") ?? "u",
                        costo ?? 0m,
                        venta ?? 0m)
                    : null,
                error));
        }

        return filas;
    }

    // ------------------------------------------------------------------ mano de obra

    public static byte[] PlantillaDeManoDeObra()
    {
        using var libro = new XLWorkbook();

        var hoja = libro.Worksheets.Add("Mano de obra");
        Encabezados(hoja, [
            "Código", "Nombre", "Descripción", "Categoría", "Cómo se cobra",
            "Precio fijo", "Tarifa por hora", "Horas"
        ]);

        Ejemplo(hoja, 2, ["MO-ACEITE", "Cambio de aceite y filtro", "Incluye revisión de niveles",
            "Mantenimiento", "fijo", 350m, null, null]);
        Ejemplo(hoja, 3, ["MO-DIAG", "Diagnóstico eléctrico", null,
            "Eléctrico", "hora", null, 450m, 1.5m]);

        hoja.Columns().AdjustToContents();

        Instrucciones(libro, [
            "Llene la hoja «Mano de obra», una línea por servicio, y súbala desde Mano de obra.",
            "",
            "Código — obligatorio. Si ya existe uno con ese código, la carga le actualiza los",
            "datos en vez de duplicarlo.",
            "Nombre — obligatorio. Como quiere verlo en la orden y en la cotización.",
            "",
            "Cómo se cobra — escriba «fijo» o «hora». Es lo único que decide qué columnas llenar:",
            "",
            "  fijo  →  llene «Precio fijo». Se cobra ese precio, trabaje lo que trabaje.",
            "  hora  →  llene «Tarifa por hora» y «Horas». Se cobra la tarifa por las horas que",
            "           lleve; las horas que ponga aquí son las que el sistema propone, y se",
            "           pueden cambiar en cada trabajo.",
            "",
            "Las dos filas de ejemplo —una de cada forma— se pueden borrar o escribir encima.",
            "Los precios van en números, sin la L ni separador de miles."
        ]);

        return Guardar(libro);
    }

    public static IReadOnlyList<Fila<SaveLaborServiceRequest>> LeerManoDeObra(Stream archivo)
    {
        var (hoja, columnas) = Abrir(archivo, "Mano de obra");
        var filas = new List<Fila<SaveLaborServiceRequest>>();

        foreach (var fila in Usadas(hoja))
        {
            var codigo = Texto(fila, columnas, "código");
            var nombre = Texto(fila, columnas, "nombre");

            if (codigo is null && nombre is null) continue;

            var cobro = (Texto(fila, columnas, "cómo se cobra") ?? "").ToLowerInvariant();
            var esFijo = cobro.StartsWith("fij");
            var porHora = cobro.StartsWith("hor");

            var fijo = Numero(fila, columnas, "precio fijo") ?? 0m;
            var tarifa = Numero(fila, columnas, "tarifa por hora") ?? 0m;
            var horas = Numero(fila, columnas, "horas") ?? 0m;

            string? error = null;

            if (codigo is null) error = "Falta el código.";
            else if (nombre is null) error = "Falta el nombre.";
            else if (!esFijo && !porHora) error = "En «Cómo se cobra» escriba «fijo» o «hora».";
            else if (esFijo && fijo <= 0) error = "Cobro fijo: falta el precio fijo.";
            else if (porHora && tarifa <= 0) error = "Cobro por hora: falta la tarifa por hora.";
            else if (porHora && horas <= 0) error = "Cobro por hora: falta cuántas horas lleva.";

            filas.Add(new Fila<SaveLaborServiceRequest>(
                fila.RowNumber(),
                codigo ?? "",
                nombre ?? "",
                error is null
                    ? new SaveLaborServiceRequest(
                        codigo!,
                        nombre!,
                        Texto(fila, columnas, "descripción"),
                        Texto(fila, columnas, "categoría"),
                        esFijo ? 0m : horas,
                        esFijo ? 0m : tarifa,
                        esFijo,
                        esFijo ? fijo : 0m)
                    : null,
                error));
        }

        return filas;
    }

    // ------------------------------------------------------------------ lo común

    /// <summary>
    /// La hoja que toca —la primera si no aparece con ese nombre, que es lo que pasa cuando
    /// alguien la renombra— y dónde quedó cada columna conocida.
    /// </summary>
    private static (IXLWorksheet Hoja, Dictionary<string, int> Columnas) Abrir(
        Stream archivo, string nombre)
    {
        XLWorkbook libro;
        try
        {
            libro = new XLWorkbook(archivo);
        }
        catch (Exception)
        {
            throw new AppException(
                "No se pudo leer el archivo. Tiene que ser el Excel de la plantilla (.xlsx).");
        }

        var hoja = libro.Worksheets.FirstOrDefault(h => Igual(h.Name, nombre))
            ?? libro.Worksheets.FirstOrDefault()
            ?? throw new AppException("El archivo no tiene ninguna hoja.");

        var encabezado = hoja.FirstRowUsed()
            ?? throw new AppException("La hoja está vacía: no tiene ni los encabezados.");

        var columnas = new Dictionary<string, int>();

        foreach (var celda in encabezado.CellsUsed())
        {
            var titulo = Normalizar(celda.GetString());
            if (titulo.Length > 0) columnas[titulo] = celda.Address.ColumnNumber;
        }

        if (columnas.Count == 0)
            throw new AppException("La primera fila tiene que llevar los encabezados de la plantilla.");

        return (hoja, columnas);
    }

    /// <summary>Las filas con algo escrito, sin la de encabezados.</summary>
    private static IEnumerable<IXLRangeRow> Usadas(IXLWorksheet hoja)
    {
        var rango = hoja.RangeUsed();
        if (rango is null) yield break;

        foreach (var fila in rango.RowsUsed().Skip(1)) yield return fila;
    }

    private static bool Tiene(IXLRangeRow fila, Dictionary<string, int> columnas, string titulo) =>
        Celda(fila, columnas, titulo) is { } celda && !celda.IsEmpty();

    private static IXLCell? Celda(IXLRangeRow fila, Dictionary<string, int> columnas, string titulo) =>
        columnas.TryGetValue(Normalizar(titulo), out var columna)
            ? fila.Worksheet.Cell(fila.RowNumber(), columna)
            : null;

    private static string? Texto(IXLRangeRow fila, Dictionary<string, int> columnas, string titulo)
    {
        var valor = Celda(fila, columnas, titulo)?.GetString().Trim();
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    /// <summary>
    /// El número de una celda, venga como número o escrito a mano. Null si no se entiende, que
    /// no es lo mismo que cero: quien escribió «consultar» en un precio tiene que enterarse.
    /// </summary>
    private static decimal? Numero(IXLRangeRow fila, Dictionary<string, int> columnas, string titulo)
    {
        var celda = Celda(fila, columnas, titulo);
        if (celda is null || celda.IsEmpty()) return null;

        if (celda.DataType == XLDataType.Number) return (decimal)celda.GetDouble();

        var texto = celda.GetString().Trim()
            .Replace("L", "", StringComparison.OrdinalIgnoreCase)
            .Replace("$", "")
            .Replace(",", "")
            .Trim();

        return decimal.TryParse(texto, NumberStyles.Any, Cultura, out var valor)
            || decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out valor)
                ? valor
                : null;
    }

    private static void Encabezados(IXLWorksheet hoja, string[] titulos)
    {
        for (var i = 0; i < titulos.Length; i++)
        {
            var celda = hoja.Cell(1, i + 1);
            celda.Value = titulos[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FE");
        }

        hoja.SheetView.FreezeRows(1);
    }

    /// <summary>Las filas de ejemplo, en gris: se ven como lo que son y se escriben encima.</summary>
    private static void Ejemplo(IXLWorksheet hoja, int fila, object?[] valores)
    {
        for (var i = 0; i < valores.Length; i++)
        {
            var celda = hoja.Cell(fila, i + 1);

            if (valores[i] is decimal numero) celda.Value = numero;
            else if (valores[i] is string texto) celda.Value = texto;

            celda.Style.Font.FontColor = XLColor.FromHtml("#6B7280");
        }
    }

    private static void Instrucciones(XLWorkbook libro, string[] lineas)
    {
        var hoja = libro.Worksheets.Add("Cómo se llena");

        for (var i = 0; i < lineas.Length; i++) hoja.Cell(i + 1, 1).Value = lineas[i];

        hoja.Column(1).Width = 95;
        hoja.Cell(1, 1).Style.Font.Bold = true;
    }

    private static byte[] Guardar(XLWorkbook libro)
    {
        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }

    private static bool Igual(string a, string b) => Normalizar(a) == Normalizar(b);

    /// <summary>
    /// Sin acentos, sin espacios de sobra y en minúscula: «Categoría» y «CATEGORIA» son la
    /// misma columna, y quien llena la plantilla no tiene por qué saberlo.
    /// </summary>
    private static string Normalizar(string texto)
    {
        var plano = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var limpio = new StringBuilder(plano.Length);
        foreach (var letra in plano)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letra) != UnicodeCategory.NonSpacingMark)
                limpio.Append(letra);
        }

        return limpio.ToString().Normalize(NormalizationForm.FormC);
    }
}
