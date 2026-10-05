<script setup lang="ts">
/**
 * El aviso de que algo falló, con la salida a mano.
 *
 * Un error sin botón deja al Dueño con el `F5` como única opción, y a veces ni se le
 * ocurre. Si la pantalla sabe recargarse sola, pasa `@retry` y aquí sale «Reintentar».
 */
defineProps<{ message: string }>()

// El `retry` no se declara como `emits` a propósito: declarado, Vue lo saca de `$attrs` y
// aquí no habría forma de saber si la vista puso o no un `@retry` para mostrar el botón.
defineOptions({ inheritAttrs: false })
</script>

<template>
  <!-- `role="alert"` para que el lector de pantalla lo diga en cuanto aparece, sin que haya
       que ir a buscarlo. -->
  <p class="aviso" role="alert">
    <span>{{ message }}</span>
    <button v-if="$attrs.onRetry" type="button" class="btn-ghost btn-sm" @click="$emit('retry')">
      Reintentar
    </button>
  </p>
</template>

<style scoped>
.aviso {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-3);
  margin: 0 0 var(--space-4);
  padding: var(--space-3);
  border-left: 3px solid var(--danger);
  border-radius: var(--radius-sm);
  background: color-mix(in srgb, var(--danger) 10%, transparent);
  color: var(--danger);
}
</style>
