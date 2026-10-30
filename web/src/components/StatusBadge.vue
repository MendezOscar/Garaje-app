<script setup lang="ts">
import { computed } from 'vue'
import { WORK_ORDER_STATUS_LABEL, WorkOrderStatus } from '@/types/domain'

const props = defineProps<{ status: WorkOrderStatus }>()

/**
 * El color codifica si la orden avanza, está detenida esperando a alguien, terminó bien o se
 * canceló. Un técnico debe poder leer el tablero de un vistazo sin decodificar siete colores.
 *
 * Las cuatro que avanzan ya no comparten el mismo azul: las dos primeras —Recibida y En
 * diagnóstico— son gris, porque todavía nadie le ha puesto la mano al vehículo, y el azul
 * queda para cuando se está trabajando de verdad. Lo que separa a una de otra dentro de su
 * color son las rayitas de avance, que dicen cuánto falta sin inventar un color más.
 */
const tone = computed(() => {
  switch (props.status) {
    case WorkOrderStatus.Received:
    case WorkOrderStatus.Diagnosing:
      return 'entering'
    case WorkOrderStatus.WaitingApproval:
    case WorkOrderStatus.WaitingParts:
      return 'blocked'
    case WorkOrderStatus.Ready:
    case WorkOrderStatus.Delivered:
      return 'done'
    case WorkOrderStatus.Cancelled:
      return 'muted'
    default:
      return 'active'
  }
})

/**
 * En qué paso de los cuatro del trabajo va. Null en las que no son un paso del camino
 * —detenida, lista, cancelada—: ahí el avance no significa nada.
 */
const paso = computed(() => {
  switch (props.status) {
    case WorkOrderStatus.Received:
      return 1
    case WorkOrderStatus.Diagnosing:
      return 2
    case WorkOrderStatus.InProgress:
      return 3
    case WorkOrderStatus.Testing:
      return 4
    default:
      return null
  }
})

const label = computed(() => WORK_ORDER_STATUS_LABEL[props.status])

const titulo = computed(() =>
  paso.value ? `${label.value} · paso ${paso.value} de 4` : label.value,
)
</script>

<template>
  <span class="badge" :data-tone="tone" :title="titulo">
    {{ label }}
    <!-- Las rayitas van después del texto y sin color propio: son una ayuda, no otro
         código que haya que aprenderse. Con aria-hidden porque el título ya lo dice. -->
    <span v-if="paso" class="avance" aria-hidden="true">
      <i v-for="n in 4" :key="n" :class="{ hecho: n <= paso }" />
    </span>
  </span>
</template>

<style scoped>
.badge {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
  white-space: nowrap;
}

.avance {
  display: inline-flex;
  gap: 2px;
}

.avance i {
  width: 4px;
  height: 8px;
  border-radius: 1px;
  background: currentColor;
  opacity: 0.25;
}

.avance i.hecho {
  opacity: 1;
}

.badge[data-tone='active'] {
  background: color-mix(in srgb, var(--accent) 18%, transparent);
  color: var(--accent);
}

/* Entrando: todavía no se trabaja. Gris, para que el azul signifique «le están metiendo
   mano» y no «existe». */
.badge[data-tone='entering'] {
  background: var(--surface-alt);
  color: var(--text);
}

/* Los tres salen de los tokens de marca, no de hexes escritos aquí: así el modo oscuro
   —donde el ámbar y el verde suben de luminosidad para alcanzar el contraste— sigue
   funcionando sin duplicar reglas. */
.badge[data-tone='blocked'] {
  background: color-mix(in srgb, var(--warning) 20%, transparent);
  color: color-mix(in srgb, var(--warning) 80%, var(--ink));
}

.badge[data-tone='done'] {
  background: color-mix(in srgb, var(--success) 20%, transparent);
  color: color-mix(in srgb, var(--success) 80%, var(--ink));
}

.badge[data-tone='muted'] {
  background: var(--surface-alt);
  color: var(--text-muted);
}

/* Sobre grafito, mezclar con tinta oscurece la letra hasta perderla contra su propio
   fondo: ahí el token va puro, que en oscuro ya viene aclarado. */
@media (prefers-color-scheme: dark) {
  .badge[data-tone='blocked'] {
    color: var(--warning-text);
  }

  .badge[data-tone='done'] {
    color: var(--success-text);
  }
}
</style>
