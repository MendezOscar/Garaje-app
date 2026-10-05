import { onUnmounted } from 'vue'

/**
 * Cerrar con Escape.
 *
 * Todo lo que se abre encima de la pantalla —el panel de avisos, el cajón de existencias, la
 * foto en grande, el menú del teléfono— se cerraba solo con el ratón. Quien trabaja con el
 * teclado quedaba atrapado dentro, y es la tecla que todo el mundo prueba primero.
 */
export function useEscape(cerrar: () => void) {
  function alPulsar(e: KeyboardEvent) {
    if (e.key === 'Escape') cerrar()
  }

  window.addEventListener('keydown', alPulsar)
  onUnmounted(() => window.removeEventListener('keydown', alPulsar))
}
