<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <section>
    <h1>Mis asignaciones</h1>
    <p>
      Sesión iniciada como <strong>{{ auth.user?.fullName }}</strong> (Técnico).
    </p>
    <p class="pending">Fase 1: órdenes asignadas, checklist de pasos y cambio de estado.</p>
  </section>
</template>

<style scoped>
.pending {
  color: var(--text-muted);
}
</style>
