<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <section>
    <h1>Dashboard</h1>
    <p>
      Sesión iniciada como <strong>{{ auth.user?.fullName }}</strong> (Dueño) en
      <strong>{{ auth.user?.tenantName }}</strong>.
    </p>
    <p class="pending">
      Fase 1: requerimientos entrantes, tablero de órdenes y asignación de técnicos.
    </p>
  </section>
</template>

<style scoped>
.pending {
  color: var(--text-muted);
}
</style>
