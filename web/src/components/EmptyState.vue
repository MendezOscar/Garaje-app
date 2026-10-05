<script setup lang="ts">
/**
 * Lo que se ve cuando no hay nada que ver.
 *
 * Una lista vacía no es un error: casi siempre es que el taller todavía no ha hecho eso.
 * Entonces el texto dice qué va a aparecer aquí y, cuando hay algo que hacer, el botón lo
 * hace. Nunca se deja el hueco en blanco: quien abre la pantalla por primera vez no sabe si
 * está vacía o si se rompió.
 */
defineProps<{
  /** Qué falta, en una línea: «Todavía no hay órdenes». */
  title: string
  /** Por qué está vacío o qué hacer para llenarlo. Opcional. */
  hint?: string
}>()
</script>

<template>
  <div class="vacio">
    <p class="titulo">{{ title }}</p>
    <p v-if="hint" class="muted small pista">{{ hint }}</p>
    <!-- La acción que llena la lista, si la hay. -->
    <slot />
  </div>
</template>

<style scoped>
.vacio {
  display: grid;
  justify-items: center;
  gap: var(--space-2);
  padding: var(--space-6) var(--space-4);
  text-align: center;
}

.titulo {
  margin: 0;
  font-weight: 600;
}

.pista {
  margin: 0;
  max-width: 28rem;
}
</style>
