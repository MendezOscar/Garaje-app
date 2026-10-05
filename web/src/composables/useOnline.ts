import { onUnmounted, ref } from 'vue'

/**
 * Si hay internet o no.
 *
 * El taller trabaja con el wifi de la oficina y datos del teléfono; se cae a ratos. Sin
 * esto, lo único que se ve es que los botones «no hacen nada», y el Dueño cree que el
 * sistema perdió lo que acababa de escribir.
 */
export function useOnline() {
  const online = ref(navigator.onLine)

  const subir = () => (online.value = true)
  const bajar = () => (online.value = false)

  window.addEventListener('online', subir)
  window.addEventListener('offline', bajar)

  onUnmounted(() => {
    window.removeEventListener('online', subir)
    window.removeEventListener('offline', bajar)
  })

  return { online }
}
