<script setup lang="ts">
/**
 * El hueco de lo que está por llegar.
 *
 * Reemplaza al «Cargando…» y a la pantalla en blanco: se ve de una vez cuántas filas vienen
 * y dónde, y cuando llegan los datos nada salta de sitio. Por eso la altura de la fila
 * imita a la de verdad en vez de ser una barra cualquiera.
 */
withDefaults(
  defineProps<{
    /** Cuántas filas dibujar. Tres o cuatro bastan: es un aviso, no un calco. */
    rows?: number
    /** `lista` para filas de tarjeta, `tabla` para renglones, `tarjetas` para una rejilla. */
    variant?: 'lista' | 'tabla' | 'tarjetas'
  }>(),
  { rows: 3, variant: 'lista' },
)
</script>

<template>
  <!-- `aria-busy` para que el lector de pantalla diga que está cargando en vez de leer
       cajas vacías; el texto de abajo es lo que anuncia. -->
  <div class="esqueleto" :class="variant" aria-busy="true" role="status">
    <span class="sr-only">Cargando…</span>
    <div v-for="n in rows" :key="n" class="fila">
      <div class="skeleton linea ancha"></div>
      <div class="skeleton linea corta"></div>
    </div>
  </div>
</template>

<style scoped>
.esqueleto {
  display: grid;
  gap: var(--space-2);
}

.esqueleto.tarjetas {
  grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
  gap: var(--space-4);
}

.fila {
  display: grid;
  gap: var(--space-2);
  padding: var(--space-4);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

/* En una tabla no hay tarjetas: son renglones separados por la línea de siempre. */
.tabla .fila {
  border: none;
  border-bottom: 1px solid var(--border);
  border-radius: 0;
  background: none;
  padding: var(--space-3) 0;
}

.linea {
  height: 0.75rem;
}

.ancha {
  width: 60%;
}

.corta {
  width: 35%;
}
</style>
