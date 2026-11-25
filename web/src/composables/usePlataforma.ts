import { computed, ref } from 'vue'
import { platformApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import { Roles } from '@/types/api'
import type { PlatformSettings, PlatformTenant } from '@/types/domain'
import { necesitaAtencion } from '@/utils/cobro'

/**
 * Lo que está esperando en el menú del perfil Plataforma.
 *
 * El menú decía cuatro nombres de sección: para saber si había un taller cortado o si el CAI
 * se estaba acabando había que entrar a mirar. Ahora Talleres lleva su número en rojo y
 * Facturación avisa del CAI desde cualquier pantalla, que son las dos cosas que no pueden
 * esperar a que a alguien se le ocurra revisar.
 *
 * Es el equivalente de `usePendientes` para este perfil: el otro sale del resumen del taller,
 * y este perfil no tiene taller.
 */
const tenants = ref<PlatformTenant[]>([])
const emisor = ref<PlatformSettings | null>(null)
let enVuelo: Promise<void> | null = null
let cargado = false

export function usePlataforma() {
  const auth = useAuthStore()

  async function cargar(forzar = false) {
    if (auth.role !== Roles.Platform) return
    if (!forzar && cargado) return
    if (enVuelo) return enVuelo

    enVuelo = Promise.all([
      platformApi.list().then((lista) => {
        tenants.value = lista
      }),
      platformApi.billingSettings().then((ajustes) => {
        emisor.value = ajustes
      }),
    ])
      // Sin red o sin permiso el menú se queda sin números: es una ayuda, no la aplicación.
      .then(() => {
        cargado = true
      })
      .catch(() => {})
      .finally(() => {
        enVuelo = null
      })

    return enVuelo
  }

  /** Cuántos talleres hay que atender. La misma regla que pinta las tarjetas de la lista. */
  const porAtender = computed(() => tenants.value.filter(necesitaAtencion).length)

  /**
   * Qué pasa con el CAI, en dos palabras para el menú. Sin rango no es un detalle: sin CAI no
   * se puede emitir una factura, ni nuestra ni de ningún otro servicio.
   */
  const cai = computed(() => {
    if (!emisor.value) return null

    const r = emisor.value.fiscalRange
    if (!r) return 'Sin CAI'
    if (r.isExpired) return 'Venció'
    if (r.isExhausted) return 'Agotado'
    if (r.remaining <= 20) return `Quedan ${r.remaining}`

    return null
  })

  return { cargar, porAtender, cai }
}
