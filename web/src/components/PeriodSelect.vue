<script setup lang="ts">
import { computed, ref, watch } from 'vue'

/**
 * Qué periodo se está mirando. Un selector, no dos fechas con un botón «Aplicar».
 *
 * Las pantallas de dinero pedían «Desde», «Hasta» y tocar Aplicar, y abrían con las dos
 * fechas vacías: quien entraba a Ventas no veía nada hasta llenar un formulario. Casi
 * siempre lo que se quiere es hoy, la semana o el mes; las fechas sueltas quedan para
 * cuando de verdad hacen falta, y se aplican solas.
 */
type Periodo = 'hoy' | 'semana' | 'mes' | 'mesPasado' | 'otras'

const props = withDefaults(defineProps<{ inicial?: Periodo }>(), { inicial: 'mes' })

/** El rango elegido, en `yyyy-MM-dd`. Es lo que la pantalla manda a la API. */
const emit = defineEmits<{ cambio: [{ desde: string; hasta: string }] }>()

const periodo = ref<Periodo>(props.inicial)
const desde = ref('')
const hasta = ref('')

function dia(fecha: Date): string {
  const local = new Date(fecha.getTime() - fecha.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 10)
}

/** El rango de cada periodo, con la semana empezando el lunes. */
const rango = computed<{ desde: string; hasta: string }>(() => {
  const hoy = new Date()

  switch (periodo.value) {
    case 'hoy':
      return { desde: dia(hoy), hasta: dia(hoy) }

    case 'semana': {
      const lunes = new Date(hoy)
      lunes.setDate(hoy.getDate() - ((hoy.getDay() + 6) % 7))
      return { desde: dia(lunes), hasta: dia(hoy) }
    }

    case 'mes':
      return { desde: dia(new Date(hoy.getFullYear(), hoy.getMonth(), 1)), hasta: dia(hoy) }

    case 'mesPasado': {
      const primero = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1)
      const ultimo = new Date(hoy.getFullYear(), hoy.getMonth(), 0)
      return { desde: dia(primero), hasta: dia(ultimo) }
    }

    default:
      return { desde: desde.value, hasta: hasta.value }
  }
})

watch(
  rango,
  (nuevo) => {
    // Con fechas a mano, se avisa cuando las dos están puestas: con una sola, la consulta
    // saldría con medio rango y traería cualquier cosa.
    if (periodo.value === 'otras' && !(nuevo.desde && nuevo.hasta)) return
    emit('cambio', nuevo)
  },
  { immediate: true },
)
</script>

<template>
  <div class="periodo">
    <select v-model="periodo">
      <option value="hoy">Hoy</option>
      <option value="semana">Esta semana</option>
      <option value="mes">Este mes</option>
      <option value="mesPasado">El mes pasado</option>
      <option value="otras">Otras fechas…</option>
    </select>

    <template v-if="periodo === 'otras'">
      <input v-model="desde" type="date" aria-label="Desde" />
      <input v-model="hasta" type="date" aria-label="Hasta" />
    </template>
  </div>
</template>

<style scoped>
.periodo {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}
</style>
