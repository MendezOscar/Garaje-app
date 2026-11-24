using System.Security.Cryptography;
using System.Text;

namespace Garaj.Infrastructure.Services;

/// <summary>
/// La llave con la que el portal de un servicio —StockRuta, Profeapp— se identifica para pedir
/// el número de una factura.
/// </summary>
/// <remarks>
/// Se guarda en <b>hash</b> y no en claro: quien lea la base no puede emitir con ella. Y se
/// enseña una sola vez, al generarla, igual que la contraseña de un taller nuevo.
///
/// El hash es un SHA-256 pelado, sin sal y sin estiramiento, y eso es correcto aquí: esto no
/// es una contraseña que alguien eligió —y que sería «Taller2026»— sino 32 bytes de azar. No
/// hay diccionario que recorrer ni tabla que precomputar contra 256 bits. El estiramiento
/// existe para castigar la adivinanza de lo adivinable, y además haría que cada petición de
/// factura pagara el costo de un PBKDF2.
/// </remarks>
internal static class LlavesDeProducto
{
    /// <summary>Lo que lleva delante, para reconocerla de un vistazo en un archivo de config.</summary>
    private const string Prefijo = "gbk_";

    /// <summary>
    /// Una llave nueva. Devuelve la llave en claro —la única vez que existe— y lo que se
    /// guarda.
    /// </summary>
    internal static (string Llave, string Hash, string Pista) Nueva()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        // Base64 de URL: la llave viaja en una cabecera y se pega en un archivo de
        // configuración, así que nada de «+», «/» ni «=».
        var llave = Prefijo + Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return (llave, Hash(llave), llave[^4..]);
    }

    /// <summary>Lo que se guarda y con lo que se compara al recibir una petición.</summary>
    internal static string Hash(string llave) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(llave.Trim())));
}
