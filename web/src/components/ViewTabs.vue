<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'

/**
 * Las pestañas de una pantalla que son dos pantallas hermanas.
 *
 * El menú tenía entradas separadas para cosas que son la misma pregunta vista de dos
 * maneras: Ventas y Por cobrar, Reportes y Resultados, Mano de obra y Trabajos frecuentes.
 * Juntarlas bajó el menú de 22 entradas a 14; aquí se pasa de una a otra sin volver al menú.
 *
 * Cada pestaña es un enlace de verdad —no un estado de la vista— para que el botón de atrás
 * del navegador y los enlaces guardados sigan funcionando.
 */
defineProps<{
  pestanas: { to: RouteLocationRaw; label: string; cuantas?: number; tono?: string }[]
}>()
</script>

<template>
  <nav class="pestanas">
    <RouterLink v-for="p in pestanas" :key="p.label" :to="p.to" class="pestana">
      {{ p.label }}
      <span v-if="p.cuantas !== undefined" class="cuenta" :class="p.tono">{{ p.cuantas }}</span>
    </RouterLink>
  </nav>
</template>

<style scoped>
.pestanas {
  display: flex;
  gap: var(--space-4);
  margin-bottom: var(--space-4);
  border-bottom: 1px solid var(--border);
}

.pestana {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.5rem 0.125rem;
  margin-bottom: -1px;
  border-bottom: 2px solid transparent;
  color: var(--text-muted);
  text-decoration: none;
  font-size: 0.9375rem;
}

.pestana:hover {
  color: var(--text);
}

.pestana.router-link-active {
  border-bottom-color: var(--accent);
  color: var(--accent);
  font-weight: 500;
}

.cuenta {
  padding: 0 0.4rem;
  border-radius: 999px;
  background: var(--surface-alt);
  font-size: 0.75rem;
  font-variant-numeric: tabular-nums;
}

.cuenta.alerta {
  background: color-mix(in srgb, var(--danger) 20%, transparent);
  color: var(--danger);
}

.cuenta.espera {
  background: color-mix(in srgb, var(--warning) 20%, transparent);
  color: var(--warning-text);
}
</style>
