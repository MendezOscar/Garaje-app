<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { errorMessage } from '@/api/client'
import BrandLogo from '@/components/BrandLogo.vue'
import ErrorNote from '@/components/ErrorNote.vue'
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

/** El ojo de la contraseña: en un mostrador se teclea a prisa y con el cliente enfrente. */
const verClave = ref(false)

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
    <!-- La mitad que se usa. En el teléfono es la única que se ve: el panel de la derecha
         es adorno, y el adorno no puede empujar el formulario fuera de pantalla. -->
    <div class="forma">
      <div class="forma-dentro">
        <BrandLogo variant="horizontal" :height="30" class="logo" />

        <h1>Entre a su taller</h1>
        <p class="sub">Las órdenes del día, la bodega y el cobro.</p>

        <form @submit.prevent="submit">
          <label>
            Correo
            <input
              v-model="email"
              type="email"
              autocomplete="username"
              placeholder="dueno@sutaller.hn"
              required
            />
          </label>

          <label>
            Contraseña
            <span class="clave">
              <input
                v-model="password"
                :type="verClave ? 'text' : 'password'"
                autocomplete="current-password"
                required
              />
              <button
                type="button"
                class="ojo"
                :aria-label="verClave ? 'Esconder la contraseña' : 'Ver la contraseña'"
                @click="verClave = !verClave"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                  <path
                    d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12Z"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  />
                  <circle cx="12" cy="12" r="3" />
                  <path v-if="verClave" d="m4 20 16-16" stroke-linecap="round" />
                </svg>
              </button>
            </span>
          </label>

          <ErrorNote v-if="error" :message="error" />

          <button class="entrar" type="submit" :disabled="loading">
            {{ loading ? 'Entrando…' : 'Entrar' }}
          </button>
        </form>

        <p class="ayuda">
          ¿Olvidó su contraseña? Escríbale a quien administra su taller: la cambia desde
          <strong>Usuarios</strong> en un momento.
        </p>

        <!-- Solo en desarrollo. Desde que la raíz del sitio es una página pública que trae
             visitantes aquí, dejar las credenciales impresas sería regalar el panel de
             demostración a cualquiera que llegue. -->
        <p v-if="mostrarDemo" class="demo">
          Demostración: dueno@tallerdemo.hn · encargado@tallerdemo.hn ·
          tecnico1@tallerdemo.hn · cliente1@tallerdemo.hn<br />
          Contraseña: Garaj123!
        </p>
      </div>

      <footer class="pie">
        <span>© {{ new Date().getFullYear() }} GarajApp · Hecho en Honduras</span>
        <a href="/">Qué es GarajApp</a>
      </footer>
    </div>

    <!-- El lado de marca. Lo que se ve mientras se teclea: de qué va esto. -->
    <aside class="marca" aria-hidden="true">
      <div class="marca-dentro">
        <h2>Todo su taller,<br />en un solo sitio.</h2>

        <div class="muestra">
          <article class="orden">
            <header>
              <span class="placa">HDE-7788</span>
              <span class="estado listo">Lista para entrega</span>
            </header>
            <p class="vehiculo">Hyundai Tucson 2021</p>
            <p class="meta">María López · CEN-000149</p>
            <div class="avance">
              <span></span><span></span><span></span><span></span><span></span>
            </div>
          </article>

          <article class="aviso">
            <div class="aviso-texto">
              <span class="aviso-rotulo">Aviso por WhatsApp</span>
              <p>«Doña María, su Tucson ya está listo para retirar.»</p>
            </div>
            <span class="wa">
              <svg viewBox="0 0 24 24" fill="currentColor">
                <path
                  d="M17.47 14.38c-.3-.15-1.76-.87-2.03-.97-.27-.1-.47-.15-.67.15-.2.3-.77.97-.94 1.16-.17.2-.35.22-.64.08-.3-.15-1.26-.46-2.39-1.48-.88-.79-1.48-1.76-1.65-2.06-.18-.3-.02-.46.13-.6.13-.14.35-.35.52-.52.17-.18.23-.3.35-.5.11-.2.05-.37-.03-.52-.09-.15-.66-1.61-.91-2.21-.23-.58-.47-.5-.65-.51h-.57c-.2 0-.52.07-.79.37-.27.3-1.04 1.02-1.04 2.48s1.07 2.87 1.21 3.07c.15.2 2.1 3.2 5.08 4.49.71.3 1.26.49 1.69.62.71.23 1.36.2 1.87.12.57-.09 1.76-.72 2.01-1.41.25-.7.25-1.29.17-1.42-.07-.12-.27-.2-.57-.35M12.05 21.8a9.87 9.87 0 0 1-5.03-1.38l-.36-.22-3.74.99 1-3.65-.24-.38a9.86 9.86 0 0 1-1.51-5.26c0-5.45 4.44-9.89 9.89-9.89 2.64 0 5.12 1.03 6.99 2.9a9.83 9.83 0 0 1 2.89 6.99c0 5.45-4.43 9.89-9.89 9.89m8.41-18.3A11.82 11.82 0 0 0 12.05 0C5.5 0 .16 5.34.16 11.89c0 2.1.55 4.15 1.59 5.95L.06 24l6.3-1.65a11.88 11.88 0 0 0 5.69 1.45c6.55 0 11.89-5.34 11.89-11.9 0-3.17-1.24-6.16-3.48-8.41Z"
                />
              </svg>
            </span>
          </article>
        </div>

        <ul class="sellos">
          <li>Los datos de su taller, aparte de los de cualquier otro</li>
          <li>Lo mismo en la computadora y en el teléfono</li>
        </ul>
      </div>
    </aside>
  </div>
</template>

<style scoped>
.login {
  min-height: 100dvh;
  display: grid;
}

@media (min-width: 64rem) {
  .login {
    grid-template-columns: 1fr 1fr;
  }
}

/* ------------------------------------------------------------------ formulario */

.forma {
  display: flex;
  flex-direction: column;
  padding: 2rem 1.5rem 1.5rem;
  background: var(--surface);
}

.forma-dentro {
  width: min(100%, 23rem);
  margin: auto;
  padding: 2rem 0;
}

.logo {
  margin-bottom: 2.5rem;
}

h1 {
  margin: 0;
  font-size: 1.75rem;
  letter-spacing: -0.02em;
}

.sub {
  margin: 0.375rem 0 1.75rem;
  color: var(--text-muted);
}

form {
  display: grid;
  gap: 1rem;
}

label {
  display: grid;
  gap: 0.375rem;
  font-size: 0.875rem;
  font-weight: 500;
}

input {
  width: 100%;
  padding: 0.7rem 0.75rem;
  font-size: 1rem;
}

input:focus-visible {
  outline: 2px solid color-mix(in srgb, var(--brand) 60%, transparent);
  outline-offset: 1px;
  border-color: var(--brand);
}

.clave {
  position: relative;
  display: block;
}

.clave input {
  padding-right: 2.75rem;
}

.ojo {
  position: absolute;
  top: 50%;
  right: 0.375rem;
  translate: 0 -50%;
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  padding: 0;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-muted);
  cursor: pointer;
}

.ojo:hover {
  color: var(--text);
  background: var(--surface-alt);
}

.ojo svg {
  width: 1.125rem;
  height: 1.125rem;
}

.entrar {
  margin-top: 0.25rem;
  padding: 0.75rem 1rem;
  font-size: 1rem;
  font-weight: 600;
  border: 0;
  border-radius: var(--radius-sm);
  background: var(--brand);
  color: #fff;
  cursor: pointer;
}

.entrar:hover:not(:disabled) {
  background: var(--brand-deep);
}

.entrar:disabled {
  opacity: 0.6;
  cursor: progress;
}

.ayuda {
  margin: 1.5rem 0 0;
  padding-top: 1.25rem;
  border-top: 1px solid var(--border);
  font-size: 0.875rem;
  color: var(--text-muted);
  line-height: 1.5;
}

.demo {
  margin: 1rem 0 0;
  padding: 0.75rem;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  font-size: 0.75rem;
  color: var(--text-muted);
  line-height: 1.6;
}

.pie {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 0.5rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.pie a {
  color: var(--text-muted);
}

/* ------------------------------------------------------------------ lado de marca */

.marca {
  display: none;
  position: relative;
  overflow: hidden;
  color: #e9eefa;
  background:
    radial-gradient(40rem 26rem at 70% -10%, #1f6feb55, transparent 60%),
    linear-gradient(180deg, #101b2c, #0d1420);
}

@media (min-width: 64rem) {
  .marca {
    display: grid;
    place-items: center;
  }
}

.marca::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(#ffffff0a 1px, transparent 1px),
    linear-gradient(90deg, #ffffff0a 1px, transparent 1px);
  background-size: 44px 44px;
  mask-image: radial-gradient(75% 65% at 50% 35%, #000, transparent);
}

.marca-dentro {
  position: relative;
  width: min(26rem, 80%);
}

h2 {
  margin: 0 0 2rem;
  font-size: clamp(1.75rem, 2.6vw, 2.375rem);
  line-height: 1.15;
  color: #fff;
}

.muestra {
  position: relative;
  padding-bottom: 4.75rem;
}

.orden,
.aviso {
  border-radius: var(--radius-md);
  background: #fff;
  color: #14171c;
  box-shadow: 0 24px 50px -24px #000000b3;
}

.orden {
  padding: 1rem 1.125rem 1.125rem;
}

.orden header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.placa {
  padding: 0.25rem 0.5rem;
  border: 1px solid #c8d3e6;
  border-radius: var(--radius-sm);
  font-family: var(--font-mono);
  font-size: 0.875rem;
  letter-spacing: 0.04em;
}

.estado {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.8125rem;
  color: #137551;
}

.estado::before {
  content: '';
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: #1fa971;
}

.vehiculo {
  margin: 0.75rem 0 0.125rem;
  font-weight: 600;
}

.meta {
  margin: 0;
  font-size: 0.8125rem;
  color: #646e7a;
}

.avance {
  display: flex;
  gap: 0.25rem;
  margin-top: 0.875rem;
}

.avance span {
  height: 0.25rem;
  flex: 1;
  border-radius: 999px;
  background: #1fa971;
}

.aviso {
  position: absolute;
  right: -1.5rem;
  bottom: 0;
  display: flex;
  align-items: center;
  gap: 0.875rem;
  width: min(22rem, 100%);
  padding: 0.875rem 1rem;
}

.aviso-rotulo {
  font-size: 0.75rem;
  color: #646e7a;
}

.aviso p {
  margin: 0.125rem 0 0;
  font-size: 0.9375rem;
  line-height: 1.4;
}

.wa {
  display: grid;
  place-items: center;
  width: 2.25rem;
  height: 2.25rem;
  flex: none;
  border-radius: 50%;
  background: #e7f8ee;
  color: #25d366;
}

.wa svg {
  width: 1.25rem;
  height: 1.25rem;
}

.sellos {
  list-style: none;
  margin: 2.5rem 0 0;
  padding: 0;
  display: grid;
  gap: 0.625rem;
  font-size: 0.9375rem;
  color: #a8b8d0;
}

.sellos li {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
}

.sellos li::before {
  content: '✓';
  color: #46c892;
  font-weight: 700;
}
</style>
