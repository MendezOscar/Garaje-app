<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import { homeRouteFor } from '@/router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const mostrarDemo = import.meta.env.DEV

const email = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

async function submit() {
  loading.value = true
  error.value = ''

  try {
    await auth.login(email.value, password.value)
    const redirect = route.query.redirect as string | undefined
    await router.replace(redirect ?? { name: homeRouteFor(auth.role) })
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo iniciar sesión.')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login">
    <form class="card" @submit.prevent="submit">
      <BrandLogo variant="vertical" :height="88" class="logo" />
      <p class="subtitle">Gestión de taller mecánico</p>

      <label>
        Correo
        <input v-model="email" type="email" autocomplete="username" required />
      </label>

      <label>
        Contraseña
        <input v-model="password" type="password" autocomplete="current-password" required />
      </label>

      <p v-if="error" class="error">{{ error }}</p>

      <button type="submit" :disabled="loading">
        {{ loading ? 'Ingresando…' : 'Ingresar' }}
      </button>

      <!-- Solo en desarrollo. Desde que la raíz del sitio es una página pública que trae
           visitantes aquí, dejar las credenciales impresas sería regalar el panel de
           demostración a cualquiera que llegue. -->
      <p v-if="mostrarDemo" class="hint">
        Demostración: eduar@rvm.hn · caleb@rvm.hn · daleth.moran@gmail.com<br />
        Contraseña: Garaj123!
      </p>
    </form>
  </div>
</template>

<style scoped>
.login {
  min-height: 100dvh;
  display: grid;
  place-items: center;
  padding: 1rem;
}

.card {
  width: min(100%, 22rem);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 2rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.logo {
  margin: 0 auto 0.25rem;
}

.subtitle {
  margin: 0 0 0.75rem;
  text-align: center;
  color: var(--text-muted);
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
  color: var(--text-muted);
}

.error {
  margin: 0;
  color: var(--danger);
  font-size: 0.875rem;
}

.hint {
  margin: 0.5rem 0 0;
  font-size: 0.75rem;
  color: var(--text-muted);
  line-height: 1.5;
}
</style>
