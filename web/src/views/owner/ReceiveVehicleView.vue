<script setup lang="ts">
import { useRouter } from 'vue-router'
import NewServiceRequestForm from '@/components/NewServiceRequestForm.vue'

/**
 * Recibir un vehículo: la hoja de recepción en una pantalla.
 *
 * Hasta hoy el único camino en el panel era Requerimientos → «Aprobar y abrir orden»: dos
 * pantallas y una aprobación para la acción más frecuente del mostrador, cuando el vehículo ya
 * está en el patio. `POST /api/work-orders` existía y era del Dueño; lo que faltaba era la
 * puerta.
 */
const router = useRouter()

function abierta(id: string | null) {
  // Se entra a la orden recién abierta: lo siguiente es escribir los pasos o mandarle el
  // enlace al cliente, y las dos cosas están ahí dentro.
  if (id) router.push({ name: 'work-order', params: { id } })
}
</script>

<template>
  <section>
    <header>
      <h1>Recibir un vehículo</h1>
      <p class="muted small">
        Abre la orden de trabajo de una vez. Para lo que el cliente pide por teléfono o desde la
        app, sin traer el vehículo, están los
        <RouterLink :to="{ name: 'service-requests' }">requerimientos</RouterLink>.
      </p>
    </header>

    <article class="marco">
      <NewServiceRequestForm mode="order" variant="page" @created="abierta" />
    </article>
  </section>
</template>

<style scoped>
header {
  margin-bottom: 1.25rem;
}

header h1 {
  margin: 0 0 0.25rem;
}

.marco {
  max-width: 46rem;
  padding: 1.25rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.muted {
  color: var(--text-muted);
}

.small {
  font-size: 0.875rem;
}
</style>
