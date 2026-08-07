<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <section>
    <h1>Mis vehículos</h1>
    <p>
      Sesión iniciada como <strong>{{ auth.user?.fullName }}</strong> (Cliente).
    </p>
    <p class="pending">Fase 1: seguimiento del proceso, timeline con fotos y cotizaciones.</p>
  </section>
</template>

<style scoped>
.pending {
  color: var(--text-muted);
}
</style>
