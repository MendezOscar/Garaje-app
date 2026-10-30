<script setup lang="ts">
/**
 * Un filtro que se enciende y se apaga, con su cuenta cuando la hay.
 *
 * Reemplaza a las casillas sueltas («Solo con saldo», «Ver los dados de baja») y a los chips
 * que cada pantalla dibujaba a su manera. Una casilla pide leer la etiqueta para saber qué
 * hace; un chip encendido se ve encendido desde el otro lado del mostrador.
 */
defineProps<{ cuantos?: number }>()

const activo = defineModel<boolean>({ default: false })
</script>

<template>
  <button
    type="button"
    class="chip"
    :class="{ activo }"
    :aria-pressed="activo"
    @click="activo = !activo"
  >
    <slot />
    <span v-if="cuantos !== undefined" class="cuenta">{{ cuantos }}</span>
  </button>
</template>

<style scoped>
.chip {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.4rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface);
  color: var(--text);
  font-size: 0.875rem;
  font-weight: 400;
}

.chip:hover {
  border-color: var(--accent);
}

.chip.activo {
  border-color: var(--accent);
  background: color-mix(in srgb, var(--accent) 14%, transparent);
  color: var(--accent);
  font-weight: 500;
}

.cuenta {
  font-variant-numeric: tabular-nums;
  opacity: 0.7;
}
</style>
