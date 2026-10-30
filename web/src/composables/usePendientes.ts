import { computed, ref } from 'vue'
import { reportsApi } from '@/api/garaj'
import { useAuthStore } from '@/stores/auth'
import type { Dashboard } from '@/types/domain'

/**
 * Lo que está esperando a alguien, para el menú.
 *
 * El menú decía solo nombres de secciones: para saber si había algo que atender había que
 * entrar a cada una. Ahora cada entrada con trabajo pendiente lleva su número —rojo lo
 * atrasado o vencido, ámbar lo que espera— y se ve desde cualquier pantalla.
 *
 * Sale del mismo resumen que pinta Inicio, así que no es una consulta nueva por entrada ni
 * un número que pueda discrepar del de esa pantalla.
 */
const resumen = ref<Dashboard | null>(null)
let enVuelo: Promise<void> | null = null
let sucursalCargada: string | null | undefined

export function usePendientes() {
  const auth = useAuthStore()

  async function cargar(forzar = false) {
    if (!auth.isManagement) return

    // Al cambiar de sucursal hay que volver a pedirlo: los pendientes son de lo que se mira.
    if (!forzar && resumen.value && sucursalCargada === auth.activeBranchId) return
    if (enVuelo) return enVuelo

    sucursalCargada = auth.activeBranchId
    enVuelo = reportsApi
      .dashboard(auth.activeBranchId || undefined)
      .then((d) => {
        resumen.value = d
      })
      // Sin red o sin permiso, el menú se queda sin números: es un adorno útil, no la app.
      .catch(() => {})
      .finally(() => {
        enVuelo = null
      })

    return enVuelo
  }

  /** «L 7.7k»: en el menú no cabe «L 7,679.41» y el centavo ahí no decide nada. */
  function montoCorto(valor: number): string {
    if (valor >= 1000) return `L ${(valor / 1000).toFixed(1)}k`
    return `L ${Math.round(valor)}`
  }

  const ordenesAtrasadas = computed(() => resumen.value?.lateWorkOrders ?? 0)
  const citasPorConfirmar = computed(() => resumen.value?.appointmentsToConfirm ?? 0)
  const repuestosBajoMinimo = computed(() => resumen.value?.partsBelowMinimum ?? 0)
  const vencidoPorCobrar = computed(() => resumen.value?.overdueReceivables ?? 0)
  const requerimientosPendientes = computed(() => resumen.value?.pendingRequests ?? 0)

  return {
    resumen,
    cargar,
    montoCorto,
    ordenesAtrasadas,
    citasPorConfirmar,
    repuestosBajoMinimo,
    vencidoPorCobrar,
    requerimientosPendientes,
  }
}
