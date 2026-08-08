<script setup lang="ts">
import { computed } from 'vue'
import { WORK_ORDER_STATUS_LABEL, WorkOrderStatus } from '@/types/domain'

const props = defineProps<{ status: WorkOrderStatus }>()

/**
 * El color codifica si la orden avanza (azul), está detenida esperando a alguien (ámbar),
 * terminó bien (verde) o se canceló (gris). Un técnico debe poder leer el tablero de un
 * vistazo sin decodificar siete colores distintos.
 */
const tone = computed(() => {
  switch (props.status) {
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

const label = computed(() => WORK_ORDER_STATUS_LABEL[props.status])
</script>

<template>
  <span class="badge" :data-tone="tone">{{ label }}</span>
</template>

<style scoped>
.badge {
  display: inline-block;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  font-size: 0.75rem;
  white-space: nowrap;
}

.badge[data-tone='active'] {
  background: color-mix(in srgb, var(--accent) 18%, transparent);
  color: var(--accent);
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
    color: var(--warning);
  }

  .badge[data-tone='done'] {
    color: var(--success);
  }
}
</style>
