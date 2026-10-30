<script setup lang="ts">
/**
 * La fila de filtros, la misma en toda lista.
 *
 * Había seis maneras de filtrar según la pantalla: chips arriba, chips a la derecha, fechas
 * con botón «Aplicar», casillas sueltas y un selector de sucursal por página. Quien aprende a
 * filtrar en Órdenes tenía que volver a aprender en Ventas.
 *
 * El orden es siempre: buscar a la izquierda, los filtros al centro, y a la derecha lo que
 * cambia la forma de ver —tablero o lista—. Nada lleva botón de aplicar: filtrar es mirar,
 * y lo que se mira se cambia al tocarlo.
 */
defineProps<{
  /** Qué se busca en esta pantalla. Sin esto no se dibuja el buscador. */
  placeholder?: string
}>()

const buscado = defineModel<string>('search', { default: '' })
</script>

<template>
  <div class="barra">
    <span v-if="placeholder" class="buscador">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
        <circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" stroke-linecap="round" />
      </svg>
      <input v-model="buscado" type="search" :placeholder="placeholder" />
    </span>

    <!-- Los filtros de la pantalla: selectores y chips, en ese orden. -->
    <slot />

    <!-- A la derecha, cómo se ve lo filtrado: tablero o lista, hoja o tarjetas. -->
    <div v-if="$slots.vista" class="vista">
      <slot name="vista" />
    </div>
  </div>
</template>

<style scoped>
.barra {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  margin-bottom: var(--space-4);
}

.buscador {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex: 1;
  min-width: 14rem;
  padding: 0 0.625rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text-muted);
}

.buscador svg {
  width: 1rem;
  height: 1rem;
  flex: none;
}

.buscador input {
  flex: 1;
  min-width: 0;
  padding: 0.45rem 0;
  border: 0;
  background: none;
  color: var(--text);
}

.buscador input:focus {
  outline: none;
}

.buscador:focus-within {
  border-color: var(--accent);
}

/* Lo de la derecha se separa de los filtros: no filtra, cambia la forma de ver. */
.vista {
  margin-left: auto;
  display: flex;
  gap: var(--space-2);
  align-items: center;
}
</style>
