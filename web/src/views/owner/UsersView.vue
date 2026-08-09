<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, usersApi } from '@/api/garaj'
import type { Branch, User } from '@/types/domain'
import { formatDateTime } from '@/utils/format'

/**
 * Usuarios del taller. Los da de alta el Dueño y nadie más: son las llaves del negocio.
 *
 * El Técnico se crea aquí con sus sucursales; el Cliente no, porque su acceso nace del
 * padrón —se le abre desde su ficha en Clientes— y así no hay manera de crear un usuario
 * Cliente que no corresponda a nadie.
 */
const users = ref<User[]>([])
const branches = ref<Branch[]>([])

const loading = ref(false)
const busy = ref(false)
const error = ref('')
const notice = ref('')

const emptyForm = {
  id: '',
  email: '',
  fullName: '',
  password: '',
  branchIds: [] as string[],
  isActive: true,
}

const form = ref({ ...emptyForm })
const editing = ref(false)

const technicians = computed(() => users.value.filter((u) => u.role === 'Technician'))
const owners = computed(() => users.value.filter((u) => u.role === 'Owner'))
const customers = computed(() => users.value.filter((u) => u.role === 'Customer'))

async function load() {
  loading.value = true
  error.value = ''
  try {
    users.value = await usersApi.list()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudieron cargar los usuarios.')
  } finally {
    loading.value = false
  }
}

function edit(user: User) {
  editing.value = true
  notice.value = ''
  form.value = {
    id: user.id,
    email: user.email,
    fullName: user.fullName,
    password: '',
    branchIds: [...user.branchIds],
    isActive: user.isActive,
  }
}

function reset() {
  editing.value = false
  form.value = { ...emptyForm, branchIds: [] }
}

async function save() {
  const f = form.value
  if (!f.fullName.trim()) return

  busy.value = true
  error.value = ''
  notice.value = ''

  try {
    if (editing.value) {
      await usersApi.update(f.id, {
        fullName: f.fullName.trim(),
        isActive: f.isActive,
        branchIds: f.branchIds,
      })
      notice.value = 'Cambios guardados.'
    } else {
      // Sin sucursal el técnico no vería ninguna orden: su bandeja filtra por asignación,
      // pero todo lo demás de la app filtra por sucursal.
      if (!f.branchIds.length) {
        error.value = 'Elija al menos una sucursal donde trabaja el técnico.'
        return
      }

      await usersApi.create({
        email: f.email.trim(),
        fullName: f.fullName.trim(),
        role: 'Technician',
        password: f.password,
        branchIds: f.branchIds,
      })
      notice.value = 'Técnico creado. Entrégale el correo y la contraseña.'
    }

    reset()
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo guardar el usuario.')
  } finally {
    busy.value = false
  }
}

async function resetPassword(user: User) {
  const password = window.prompt(
    `Nueva contraseña para ${user.fullName}.\nCierra las sesiones que tenga abiertas.`,
  )
  if (!password?.trim()) return

  busy.value = true
  error.value = ''
  notice.value = ''
  try {
    await usersApi.resetPassword(user.id, password.trim())
    notice.value = `Contraseña cambiada. Entrégasela a ${user.fullName}.`
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cambiar la contraseña.')
  } finally {
    busy.value = false
  }
}

async function toggleActive(user: User) {
  const question = user.isActive
    ? `¿Dar de baja a ${user.fullName}? No podrá entrar, pero su trabajo queda registrado.`
    : `¿Reactivar a ${user.fullName}?`
  if (!confirm(question)) return

  busy.value = true
  error.value = ''
  try {
    await usersApi.update(user.id, {
      fullName: user.fullName,
      isActive: !user.isActive,
      branchIds: user.branchIds,
    })
    await load()
  } catch (e) {
    error.value = errorMessage(e, 'No se pudo cambiar el estado del usuario.')
  } finally {
    busy.value = false
  }
}

function branchNames(ids: string[]) {
  const names = branches.value.filter((b) => ids.includes(b.id)).map((b) => b.name)
  return names.length ? names.join(' · ') : 'sin sucursal'
}

onMounted(async () => {
  branches.value = await branchesApi.list().catch(() => [])
  await load()
})
</script>

<template>
  <section>
    <header class="top">
      <h1>Usuarios</h1>
      <p class="muted small">
        Quién entra a la aplicación y qué ve. El Técnico solo trabaja en las sucursales que se
        le asignen aquí.
      </p>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-if="notice" class="notice">{{ notice }}</p>

    <div class="layout">
      <div>
        <h2>Técnicos</h2>
        <p v-if="loading" class="muted">Cargando…</p>
        <p v-else-if="!technicians.length" class="muted">
          Todavía no hay técnicos. Cree el primero en el formulario de al lado.
        </p>

        <table v-else>
          <thead>
            <tr>
              <th>Nombre</th>
              <th>Sucursales</th>
              <th>Último ingreso</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="u in technicians" :key="u.id" :class="{ inactive: !u.isActive }">
              <td>
                {{ u.fullName }}
                <div class="muted small">{{ u.email }}</div>
              </td>
              <td class="muted small">{{ branchNames(u.branchIds) }}</td>
              <td class="muted small">
                {{ u.lastLoginAt ? formatDateTime(u.lastLoginAt) : 'nunca ha entrado' }}
              </td>
              <td class="row-actions">
                <button type="button" class="link" @click="edit(u)">Editar</button>
                <button type="button" class="link" @click="resetPassword(u)">Contraseña</button>
                <button type="button" class="link danger" @click="toggleActive(u)">
                  {{ u.isActive ? 'Dar de baja' : 'Reactivar' }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>

        <h2>Dueños</h2>
        <ul class="plain">
          <li v-for="u in owners" :key="u.id">
            {{ u.fullName }} <span class="muted small">{{ u.email }}</span>
          </li>
        </ul>

        <h2>Clientes con acceso</h2>
        <p class="muted small">
          Se les abre desde su ficha en <RouterLink :to="{ name: 'customers' }">Clientes</RouterLink>,
          para que todo acceso corresponda a alguien del padrón.
        </p>
        <p v-if="!customers.length" class="muted">Ningún cliente usa la app todavía.</p>
        <table v-else>
          <thead>
            <tr>
              <th>Cliente</th>
              <th>Último ingreso</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="u in customers" :key="u.id" :class="{ inactive: !u.isActive }">
              <td>
                {{ u.fullName }}
                <div class="muted small">{{ u.email }}</div>
              </td>
              <td class="muted small">
                {{ u.lastLoginAt ? formatDateTime(u.lastLoginAt) : 'nunca ha entrado' }}
              </td>
              <td class="row-actions">
                <button type="button" class="link" @click="resetPassword(u)">Contraseña</button>
                <button type="button" class="link danger" @click="toggleActive(u)">
                  {{ u.isActive ? 'Quitar acceso' : 'Reactivar' }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <form class="card" @submit.prevent="save">
        <h2>{{ editing ? 'Editar usuario' : 'Nuevo técnico' }}</h2>

        <label>
          Nombre
          <input v-model="form.fullName" placeholder="Keny Alvarado" required />
        </label>

        <label>
          Correo
          <input
            v-model="form.email"
            type="email"
            placeholder="tecnico@taller.hn"
            :disabled="editing"
            required
          />
        </label>
        <p v-if="editing" class="muted small">El correo no se cambia: es con lo que entra.</p>

        <label v-if="!editing">
          Contraseña
          <input v-model="form.password" type="text" placeholder="Mínimo 8 caracteres" required />
        </label>
        <p v-if="!editing" class="muted small">
          Se la entrega usted en persona. Él la puede cambiar después.
        </p>

        <fieldset>
          <legend>Sucursales donde trabaja</legend>
          <label v-for="b in branches" :key="b.id" class="checkbox">
            <input v-model="form.branchIds" type="checkbox" :value="b.id" />
            {{ b.name }}
          </label>
        </fieldset>

        <label v-if="editing" class="checkbox">
          <input v-model="form.isActive" type="checkbox" />
          Activo
        </label>

        <div class="actions">
          <button type="submit" :disabled="busy">{{ editing ? 'Guardar' : 'Crear técnico' }}</button>
          <button v-if="editing" type="button" class="link" @click="reset">Cancelar</button>
        </div>

        <p class="muted small">
          Un usuario no se borra: se da de baja. Las órdenes, las fotos y los movimientos de
          bodega llevan su nombre, y borrarlo dejaría el histórico sin responsable.
        </p>
      </form>
    </div>
  </section>
</template>

<style scoped>
h1 {
  margin: 0;
}

.top {
  margin-bottom: 1rem;
}

h2 {
  margin: 1.25rem 0 0.5rem;
  font-size: 0.8125rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.layout {
  display: grid;
  grid-template-columns: 1fr 20rem;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 60rem) {
  .layout {
    grid-template-columns: 1fr;
  }
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

th,
td {
  padding: 0.5rem;
  border-bottom: 1px solid var(--border);
  text-align: left;
  vertical-align: top;
}

th {
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-muted);
}

.inactive {
  opacity: 0.55;
}

.row-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: flex-end;
}

.plain {
  list-style: none;
  margin: 0;
  padding: 0;
  font-size: 0.875rem;
}

.card {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
}

.card h2 {
  margin: 0;
}

.card label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.card input {
  width: 100%;
}

fieldset {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 0.5rem 0.75rem;
}

legend {
  padding: 0 0.25rem;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.checkbox {
  flex-direction: row !important;
  align-items: center;
  gap: 0.5rem;
}

.checkbox input {
  width: auto;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.link {
  padding: 0;
  border: none;
  background: none;
  color: var(--accent);
  cursor: pointer;
  font-size: 0.8125rem;
}

.link.danger {
  color: var(--danger);
}

.notice {
  color: var(--success, #15803d);
}

.muted {
  color: var(--text-muted);
}

.small {
  font-size: 0.75rem;
}

.error {
  color: var(--danger);
}
</style>
