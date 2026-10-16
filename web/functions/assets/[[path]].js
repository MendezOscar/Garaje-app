/**
 * Un archivo de `/assets/` que ya no existe tiene que contestar 404.
 *
 * El `_redirects` manda al `index.html` todo lo que no sea un archivo —así funciona recargar
 * `/cita/{token}` o cualquier ruta del panel—, pero ese comodín también atrapa los archivos
 * que faltan: una pestaña abierta desde antes de un despliegue pide el JavaScript de la
 * versión anterior, Cloudflare le devuelve HTML con un 200, y el navegador se queja de que
 * «'text/html' no es un tipo válido para un módulo». Un 404 de verdad dice lo que pasa.
 *
 * `_redirects` no admite el 404 —Cloudflare solo acepta 200, 301, 302, 303, 307 y 308—, así
 * que la corrección va aquí, en el único lugar donde se puede mirar la respuesta.
 *
 * **Falla hacia el lado seguro**: si algo sale raro, se devuelve lo que vino. Que un archivo
 * perdido conteste mal es molesto; que esto rompa los archivos buenos sería dejar el panel
 * en blanco para todos.
 */
export async function onRequest(context) {
  const respuesta = await context.next();

  try {
    const tipo = respuesta.headers.get('content-type') ?? '';

    // Nada de /assets/ es HTML: son los JS, los CSS y las fuentes que escribe la compilación.
    // Si llega HTML, es el comodín contestando por un archivo que no está.
    if (!tipo.includes('text/html')) return respuesta;

    return new Response('Ese archivo ya no existe. Recargue la página.', {
      status: 404,
      headers: {
        'content-type': 'text/plain; charset=utf-8',
        // Sin guardar: el archivo falta por un despliegue, no para siempre.
        'cache-control': 'no-store',
      },
    });
  } catch {
    return respuesta;
  }
}
