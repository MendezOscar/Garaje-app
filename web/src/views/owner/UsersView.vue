<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { branchesApi, usersApi } from '@/api/garaj'
import EmptyState from '@/components/EmptyState.vue'
import ErrorNote from '@/components/ErrorNote.vue'
import SkeletonList from '@/components/SkeletonList.vue'
import {
  PAY_MODE_LABEL,
  TechnicianPayMode,
  type Branch,
  type User,
} from '@/types/domain'
import { formatDateTime, formatMoney } from '@/utils/format'

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
  payMode: TechnicianPayMode.Undefined as TechnicianPayMode,
  payAmount: 0,
  /** `Technician` o `Manager`: qué se está dando de alta. Solo cuenta al crear. */
  role: 'Technician' as 'Technician' | 'Manager',
}

const form = ref({ ...emptyForm })
const editing = ref(false)

const technicians = computed(() => users.value.filter((u) => u.role === 'Technician'))
const managers = computed(() => users.value.filter((u) => u.role === 'Manager'))
const owners = computed(() => users.value.filter((u) => u.role === 'Owner'))
const customers = computed(() => users.value.filter((u) => u.role === 'Customer'))

/** Cómo se llama cada perfil en pantalla. */
const ROL_LABEL: Record<string, string> = {
  Owner: 'Dueño',
  Manager: 'Encargado',
  Technician: 'Técnico',
  Customer: 'Cliente',
}

/**
 * Qué grupo se está mirando y con qué filtro.
 *
 * Antes la pantalla eran cuatro listas una debajo de otra: para llegar a los clientes con
 * acceso había que pasar por todo el equipo. Con pestañas, cada grupo está a un clic y la
 * tabla es siempre la misma.
 */
const grupo = ref<'Technician' | 'Manager' | 'Owner' | 'Customer'>('Technician')
const busqueda = ref('')

const pestanas = computed(() => [
  { key: 'Technician' as const, label: 'Técnicos', cuantos: technicians.value.length },
  { key: 'Manager' as const, label: 'Encargados', cuantos: managers.value.length },
  { key: 'Owner' as const, label: 'Dueños', cuantos: owners.value.length },
  { key: 'Customer' as const, label: 'Clientes con acceso', cuantos: customers.value.length },
])

const visibles = computed(() => {
  const texto = busqueda.value.trim().toLowerCase()
  return users.value.filter(
    (u) =>
      u.role === grupo.value &&
      (!texto ||
        u.fullName.toLowerCase().includes(texto) ||
        u.email.toLowerCase().includes(texto)),
  )
})

/** Las iniciales del círculo: dos letras, que es lo que cabe. */
function iniciales(nombre: string) {
  return nombre
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0]?.toUpperCase() ?? '')
    .join('')
}

/** El color del perfil: el encargado y el dueño tienen que distinguirse de un vistazo. */
function claseDeRol(role: string) {
  return `rol-${role.toLowerCase()}`
}

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
    payMode: user.payMode,
    payAmount: user.payAmount,
    role: user.role === 'Manager' ? 'Manager' : 'Technician',
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
        payMode: f.payMode,
        payAmount: f.payAmount,
      })
      notice.value = 'Cambios guardados.'
    } else {
      // Sin sucursal el técnico no vería ninguna orden: su bandeja filtra por asignación,
      // pero todo lo demás de la app filtra por sucursal.
      if (!f.branchIds.length) {
        error.value =
          f.role === 'Manager'
            ? 'Elija al menos una sucursal que vaya a manejar el encargado.'
            : 'Elija al menos una sucursal donde trabaja el técnico.'
        return
      }

      await usersApi.create({
        email: f.email.trim(),
        fullName: f.fullName.trim(),
        role: f.role,
        password: f.password,
        branchIds: f.branchIds,
        payMode: f.payMode,
        payAmount: f.payAmount,
      })
      notice.value =
        f.role === 'Manager'
          ? 'Encargado creado. Entrégale el correo y la contraseña.'
          : 'Técnico creado. Entrégale el correo y la contraseña.'

      // Que se vea el que acaba de crear: si se dio de alta un encargado estando en la
      // pestaña de técnicos, la lista parecería no haber cambiado.
      grupo.value = f.role
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
    <header class="cabecera">
      <div>
        <h1>
          Usuarios
          <span v-if="users.length" class="cuantos num">{{ users.length }}</span>
        </h1>
        <p class="muted small">
          Quién entra a la aplicación y qué ve. El Técnico solo trabaja en las sucursales que
          se le asignen aquí.
        </p>
      </div>
    </header>

    <ErrorNote v-if="error" :message="error" />
    <p v-if="notice" class="notice">{{ notice }}</p>

    <div class="layout">
      <div class="panel">
        <div class="pestanas">
          <button
            v-for="p in pestanas"
            :key="p.key"
            type="button"
            class="pestana"
            :class="{ activa: grupo === p.key }"
            @click="grupo = p.key"
          >
            {{ p.label }}
            <span class="num">{{ p.cuantos }}</span>
          </button>

          <span class="buscador">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
              <circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" stroke-linecap="round" />
            </svg>
            <input v-model="busqueda" type="search" placeholder="Nombre o correo" />
          </span>
        </div>

        <p v-if="grupo === 'Customer'" class="aviso muted small">
          El acceso del cliente se abre desde su ficha en
          <RouterLink :to="{ name: 'customers' }">Clientes</RouterLink>, para que todo acceso
          corresponda a alguien del padrón.
        </p>

        <SkeletonList v-if="loading" :rows="5" variant="tabla" />
        <EmptyState
          v-else-if="!visibles.length"
          :title="busqueda ? 'Nadie coincide con esa búsqueda' : 'Todavía no hay nadie aquí'"
          :hint="busqueda ? 'Pruebe con el nombre o el correo.' : 'Dé de alta al primero en el formulario de al lado.'"
        />

        <table v-else>
          <thead>
            <tr>
              <th scope="col">Nombre</th>
              <th scope="col">Perfil</th>
              <th scope="col">Sucursales</th>
              <th scope="col">Último ingreso</th>
              <th scope="col"><span class="sr-only">Acciones</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="u in visibles" :key="u.id" :class="{ baja: !u.isActive }">
              <td>
                <span class="persona">
                  <span class="avatar" :class="claseDeRol(u.role)">{{ iniciales(u.fullName) }}</span>
                  <span class="persona-texto">
                    <strong>{{ u.fullName }}</strong>
                    <span class="muted small">{{ u.email }}</span>
                  </span>
                </span>
              </td>
              <td>
                <span class="pastilla" :class="claseDeRol(u.role)">{{ ROL_LABEL[u.role] ?? u.role }}</span>
                <span v-if="!u.isActive" class="pastilla inactiva">dado de baja</span>
                <div v-if="u.role === 'Technician' && u.payMode !== TechnicianPayMode.Undefined" class="muted small">
                  {{ PAY_MODE_LABEL[u.payMode] }} ·
                  {{ u.payMode === TechnicianPayMode.Percentage ? `${u.payAmount}%` : formatMoney(u.payAmount) }}
                </div>
              </td>
              <td class="muted small">
                {{ u.role === 'Owner' || u.role === 'Customer' ? '—' : branchNames(u.branchIds) }}
              </td>
              <td class="muted small">
                {{ u.lastLoginAt ? formatDateTime(u.lastLoginAt) : 'nunca ha entrado' }}
              </td>
              <td class="col-acciones">
                <div class="acciones">
                  <button
                    v-if="u.role === 'Technician' || u.role === 'Manager'"
                    type="button"
                    class="icono"
                    title="Editar"
                    :disabled="busy"
                    @click="edit(u)"
                  >
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                      <path d="M4 20h4L19 9l-4-4L4 16z" stroke-linejoin="round" />
                    </svg>
                  </button>
                  <button
                    type="button"
                    class="icono"
                    title="Cambiarle la contraseña"
                    :disabled="busy"
                    @click="resetPassword(u)"
                  >
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                      <circle cx="8" cy="12" r="3.5" /><path d="M11.5 12H20v3m-3-3v2.5" stroke-linecap="round" />
                    </svg>
                  </button>
                  <button
                    v-if="u.role !== 'Owner'"
                    type="button"
                    class="icono peligro"
                    :title="u.isActive ? 'Dar de baja' : 'Reactivar'"
                    :disabled="busy"
                    @click="toggleActive(u)"
                  >
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
                      <path
                        v-if="u.isActive"
                        d="M16 7a4 4 0 1 0-8 0 4 4 0 0 0 8 0M4 20c0-3.3 3.6-5 8-5m3 4h6"
                        stroke-linecap="round"
                      />
                      <path v-else d="M16 7a4 4 0 1 0-8 0 4 4 0 0 0 8 0M4 20c0-3.3 3.6-5 8-5m3 4h6m-3-3 3 3-3 3" stroke-linecap="round" stroke-linejoin="round" />
                    </svg>
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <form class="card" @submit.prevent="save">
        <h2>
          {{
            editing
              ? 'Editar usuario'
              : form.role === 'Manager'
                ? 'Nuevo encargado'
                : 'Nuevo técnico'
          }}
        </h2>

        <!--
          Qué se está dando de alta. El técnico trabaja las órdenes que le asignan; el
          encargado manda en sus sucursales —factura, cobra, cotiza y mueve bodega— pero no
          toca los ajustes del taller, los catálogos, los gastos ni al personal.
        -->
        <div v-if="!editing" class="perfil">
          <span class="rotulo">Perfil</span>
          <div class="opciones">
            <button
              type="button"
              class="opcion"
              :class="{ activa: form.role === 'Technician' }"
              @click="form.role = 'Technician'"
            >
              Técnico
            </button>
            <button
              type="button"
              class="opcion"
              :class="{ activa: form.role === 'Manager' }"
              @click="form.role = 'Manager'"
            >
              Encargado
            </button>
          </div>
          <small class="muted">
            {{
              form.role === 'Manager'
                ? 'Manda en sus sucursales: factura, cobra, cotiza y mueve bodega. No ve los ajustes del taller, los catálogos, los gastos ni al personal.'
                : 'Trabaja las órdenes que se le asignen en sus sucursales.'
            }}
          </small>
        </div>

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
          <legend>
            {{ form.role === 'Manager' ? 'Sucursales que maneja' : 'Sucursales donde trabaja' }}
          </legend>
          <label v-for="b in branches" :key="b.id" class="checkbox">
            <input v-model="form.branchIds" type="checkbox" :value="b.id" />
            {{ b.name }}
          </label>
        </fieldset>

        <!-- Cómo se le paga. Solo al editar: al crearlo todavía no se ha acordado. -->
        <template v-if="editing">
          <label>
            Cómo se le paga
            <select v-model.number="form.payMode">
              <option
                v-for="(label, value) in PAY_MODE_LABEL"
                :key="value"
                :value="Number(value)"
              >
                {{ label }}
              </option>
            </select>
          </label>
          <label v-if="form.payMode !== TechnicianPayMode.Undefined">
            {{ form.payMode === TechnicianPayMode.Percentage ? 'Porcentaje' : 'Monto' }}
            <input v-model.number="form.payAmount" type="number" min="0" step="0.01" />
            <small class="muted">
              {{
                form.payMode === TechnicianPayMode.Fixed
                  ? 'Lo que se le paga por periodo.'
                  : form.payMode === TechnicianPayMode.Percentage
                    ? 'De la mano de obra que facturaron sus órdenes.'
                    : 'Por cada hora que registre en los pasos que complete.'
              }}
            </small>
          </label>
        </template>

        <label v-if="editing" class="checkbox">
          <input v-model="form.isActive" type="checkbox" />
          Activo
        </label>

        <div class="actions">
          <button type="submit" :disabled="busy">
            {{ editing ? 'Guardar' : form.role === 'Manager' ? 'Crear encargado' : 'Crear técnico' }}
          </button>
          <button v-if="editing" type="button" class="btn-ghost" @click="reset">Cancelar</button>
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
.cabecera {
  margin-bottom: 1.25rem;
}

h1 {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0;
  font-size: 1.625rem;
}

.cabecera p {
  margin: 0.25rem 0 0;
}

.cuantos {
  padding: 0.1rem 0.5rem;
  border-radius: 999px;
  background: var(--surface-alt);
  color: var(--text-muted);
  font-size: 0.875rem;
}

.notice {
  color: var(--success-text);
}

.layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 22rem;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 68rem) {
  .layout {
    grid-template-columns: 1fr;
  }
}

/* ------------------------------------------------------------------ panel y pestañas */

.panel {
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  overflow: hidden;
}

.pestanas {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-wrap: wrap;
  padding: 0.625rem 0.875rem;
  border-bottom: 1px solid var(--border);
}

.pestana {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.35rem 0.7rem;
  border: 1px solid transparent;
  border-radius: 999px;
  background: none;
  color: var(--text-muted);
  font-size: 0.875rem;
  cursor: pointer;
}

.pestana .num {
  font-size: 0.75rem;
  opacity: 0.8;
}

.pestana:hover {
  background: var(--surface-alt);
  color: var(--text);
}

.pestana.activa {
  border-color: color-mix(in srgb, var(--brand) 40%, var(--border));
  background: color-mix(in srgb, var(--brand) 10%, transparent);
  color: var(--brand);
  font-weight: 600;
}

.buscador {
  position: relative;
  margin-left: auto;
  width: min(16rem, 100%);
}

.buscador svg {
  position: absolute;
  top: 50%;
  left: 0.625rem;
  translate: 0 -50%;
  width: 1.125rem;
  height: 1.125rem;
  color: var(--text-muted);
}

.buscador input {
  width: 100%;
  padding-left: 2.125rem;
}

.aviso {
  margin: 0;
  padding: 0.625rem 0.875rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface-alt);
}

/* ------------------------------------------------------------------ tabla */

table {
  width: 100%;
  border-collapse: collapse;
}

th,
td {
  padding: 0.75rem 0.875rem;
  text-align: left;
  vertical-align: middle;
}

th {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
  background: var(--surface-alt);
}

tbody tr {
  border-top: 1px solid var(--border);
}

tbody tr:hover {
  background: var(--surface-alt);
}

/* Dado de baja no se esconde: su trabajo sigue en el histórico y a veces hay que
   reactivarlo. Se apaga, nada más. */
.baja .persona,
.baja td:not(.col-acciones) {
  opacity: 0.55;
}

.persona {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.avatar {
  display: grid;
  place-items: center;
  width: 2.25rem;
  height: 2.25rem;
  flex: none;
  border-radius: 50%;
  background: color-mix(in srgb, var(--brand) 12%, transparent);
  color: var(--brand);
  font-size: 0.8125rem;
  font-weight: 700;
}

.avatar.rol-manager {
  background: color-mix(in srgb, var(--warning) 18%, transparent);
  color: var(--warning-text);
}

.avatar.rol-owner {
  background: color-mix(in srgb, var(--success) 16%, transparent);
  color: var(--success-text);
}

.avatar.rol-customer {
  background: var(--surface-alt);
  color: var(--text-muted);
}

.persona-texto {
  display: grid;
  min-width: 0;
}

.pastilla {
  display: inline-block;
  padding: 0.15rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.pastilla.rol-manager {
  border-color: color-mix(in srgb, var(--warning) 45%, var(--border));
  color: var(--warning-text);
}

.pastilla.rol-owner {
  border-color: color-mix(in srgb, var(--success) 45%, var(--border));
  color: var(--success-text);
}

.pastilla.inactiva {
  margin-left: 0.25rem;
  border-color: color-mix(in srgb, var(--danger) 40%, var(--border));
  color: var(--danger);
}

.col-acciones {
  width: 1%;
}

.acciones {
  display: flex;
  justify-content: flex-end;
  gap: 0.25rem;
}

.icono {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text-muted);
  cursor: pointer;
}

.icono svg {
  width: 1.0625rem;
  height: 1.0625rem;
}

.icono:hover {
  color: var(--brand);
  border-color: var(--brand);
}

.icono.peligro:hover {
  color: var(--danger);
  border-color: var(--danger);
}

/* ------------------------------------------------------------------ formulario */

.card {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
}

.card h2 {
  margin: 0;
  font-size: 1rem;
}

.card label {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.card input,
.card select {
  width: 100%;
}

.perfil {
  display: grid;
  gap: 0.25rem;
}

.rotulo {
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.opciones {
  display: flex;
  gap: 0.375rem;
}

/* El perfil se elige, no se despliega: son dos, y de la elección depende la mitad de lo que
   dice el resto del formulario. */
.opcion {
  flex: 1;
  padding: 0.45rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  font-size: 0.875rem;
  cursor: pointer;
}

.opcion.activa {
  border-color: var(--brand);
  background: color-mix(in srgb, var(--brand) 10%, transparent);
  color: var(--brand);
  font-weight: 600;
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
</style>
