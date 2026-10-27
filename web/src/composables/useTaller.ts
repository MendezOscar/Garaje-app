import { computed, ref } from 'vue'
import { tenantApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import { WorkshopKind, type TenantSettings } from '@/types/domain'

/**
 * Con qué trabaja el taller y cómo se llaman las cosas en pantalla.
 *
 * Un taller mecánico recibe vehículos con placa y kilometraje; uno de electrónica recibe
 * equipos con número de serie. Es la misma pantalla con otras palabras, así que las palabras
 * salen de aquí y no se escriben a mano en cada vista.
 *
 * La configuración se pide una vez por sesión y se comparte: son datos que casi nunca
 * cambian y los pedía ya media docena de pantallas por su cuenta.
 */
const ajustes = ref<TenantSettings | null>(null)
let enVuelo: Promise<TenantSettings | null> | null = null

export function useTaller() {
  // De la sesión, no de la ficha del taller: la ficha solo la puede leer el Dueño y estas
  // palabras las necesitan todas las pantallas, también las del Encargado y el Técnico.
  const auth = useAuthStore()
  const esDeEquipos = computed(() => auth.user?.worksWith === WorkshopKind.Equipment)

  const vocabulario = computed(() =>
    esDeEquipos.value
      ? {
          activo: 'equipo',
          Activo: 'Equipo',
          activos: 'equipos',
          Activos: 'Equipos',
          identificador: 'Serie',
          recibir: 'Recibir equipo',
        }
      : {
          activo: 'vehículo',
          Activo: 'Vehículo',
          activos: 'vehículos',
          Activos: 'Vehículos',
          identificador: 'Placa',
          recibir: 'Recibir vehículo',
        },
  )

  /** Lo trae del servidor la primera vez; las demás devuelve lo que ya está en memoria. */
  async function cargar() {
    if (ajustes.value) return ajustes.value
    enVuelo ??= tenantApi.get().catch(() => null)

    ajustes.value = await enVuelo
    enVuelo = null
    return ajustes.value
  }

  /** Tras guardar los ajustes, para que el resto de las pantallas no quede con lo viejo. */
  function actualizar(nuevos: TenantSettings) {
    ajustes.value = nuevos
  }

  return { ajustes, esDeEquipos, vocabulario, cargar, actualizar }
}
