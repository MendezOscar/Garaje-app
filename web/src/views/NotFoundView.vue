<script setup lang="ts">
import { RouterLink } from 'vue-router'
</script>

<template>
  <section class="not-found">
    <h1>404</h1>
    <p>La página no existe.</p>
    <RouterLink to="/">Volver al inicio</RouterLink>
  </section>
</template>

<style scoped>
.not-found {
  min-height: 100dvh;
  display: grid;
  place-content: center;
  justify-items: center;
  gap: 0.5rem;
}
</style>
