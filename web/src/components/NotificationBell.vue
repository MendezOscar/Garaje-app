<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { notificationsApi } from '@/api/garaj'
import { NOTIFICATION_ICON, type Notification } from '@/types/domain'

/**
 * Sondeo cada minuto en vez de WebSocket o SSE. Un taller tiene tres o cuatro personas
 * conectadas: mantener una conexión abierta por cada una, en un plan que además duerme el
 * servidor, cuesta más que preguntar el contador una vez por minuto.
 */
const POLL_MS = 60_000

const router = useRouter()
const unread = ref(0)
const items = ref<Notification[]>([])
const open = ref(false)
const loading = ref(false)
let timer: number | undefined

const badge = computed(() => (unread.value > 9 ? '9+' : String(unread.value)))

async function refreshCount() {
  try {
    unread.value = await notificationsApi.unreadCount()
  } catch {
    // Silencio a propósito: si la API no responde, la campana no es el lugar donde
    // enterarse. La pantalla que el usuario está mirando ya mostrará su propio error.
  }
}

async function toggle() {
  open.value = !open.value
  if (!open.value) return

  loading.value = true
  try {
    items.value = (await notificationsApi.list({ pageSize: 15 })).items
  } finally {
    loading.value = false
  }
}

async function activate(notification: Notification) {
  open.value = false

  if (!notification.isRead) {
    notification.isRead = true
    unread.value = Math.max(0, unread.value - 1)
    await notificationsApi.markRead(notification.id)
  }

  // La orden es el destino útil: es donde se ve el vehículo, la línea de tiempo y la
  // cotización. Un aviso sin orden —un requerimiento nuevo— lleva a la bandeja.
  if (notification.workOrderId) {
    await router.push({ name: 'work-order', params: { id: notification.workOrderId } })
  } else if (notification.serviceRequestId) {
    await router.push({ name: 'service-requests' })
  } else if (notification.quoteId) {
    await router.push({ name: 'quotes' })
  }
}

async function markAll() {
  await notificationsApi.markAllRead()
  unread.value = 0
  items.value = items.value.map((n) => ({ ...n, isRead: true }))
}

function relative(iso: string): string {
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60_000)
  if (minutes < 1) return 'ahora'
  if (minutes < 60) return `hace ${minutes} min`
  if (minutes < 1440) return `hace ${Math.round(minutes / 60)} h`
  return new Date(iso).toLocaleDateString('es-HN', { day: '2-digit', month: 'short' })
}

onMounted(() => {
  refreshCount()
  timer = window.setInterval(refreshCount, POLL_MS)
})

onBeforeUnmount(() => window.clearInterval(timer))
</script>

<template>
  <div class="bell">
    <button type="button" class="trigger" :title="`${unread} avisos sin leer`" @click="toggle">
      🔔
      <span v-if="unread > 0" class="badge">{{ badge }}</span>
    </button>

    <!-- Capa transparente a pantalla completa: cierra el panel al hacer clic fuera sin
         tener que escuchar eventos en el documento. -->
    <div v-if="open" class="backdrop" @click="open = false" />

    <div v-if="open" class="panel">
      <header>
        <strong>Avisos</strong>
        <button v-if="unread > 0" type="button" class="link" @click="markAll">
          Marcar todo leído
        </button>
      </header>

      <p v-if="loading" class="empty">Cargando…</p>
      <p v-else-if="items.length === 0" class="empty">No hay avisos todavía.</p>

      <ul v-else>
        <li v-for="item in items" :key="item.id" :class="{ unread: !item.isRead }">
          <button type="button" @click="activate(item)">
            <span class="icon">{{ NOTIFICATION_ICON[item.type] }}</span>
            <span class="text">
              <strong>{{ item.title }}</strong>
              <span class="body">{{ item.body }}</span>
              <span class="when">{{ relative(item.createdAt) }}</span>
            </span>
          </button>
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.bell {
  position: relative;
}

.trigger {
  position: relative;
  border: none;
  background: none;
  font-size: 1.125rem;
  cursor: pointer;
  padding: 0.25rem;
}

.badge {
  position: absolute;
  top: -2px;
  right: -6px;
  min-width: 1.1rem;
  padding: 0 0.25rem;
  border-radius: 999px;
  background: #dc2626;
  color: white;
  font-size: 0.6875rem;
  font-weight: 600;
  line-height: 1.1rem;
}

.backdrop {
  position: fixed;
  inset: 0;
  z-index: 20;
}

.panel {
  position: absolute;
  right: 0;
  top: calc(100% + 0.5rem);
  z-index: 21;
  width: min(24rem, calc(100vw - 2rem));
  max-height: 70vh;
  overflow-y: auto;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 0.5rem;
  box-shadow: 0 12px 32px rgb(0 0 0 / 0.14);
}

.panel header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--border);
}

.link {
  border: none;
  background: none;
  color: var(--accent);
  font-size: 0.75rem;
  cursor: pointer;
}

.empty {
  padding: 1.5rem 1rem;
  text-align: center;
  color: var(--text-muted);
  font-size: 0.875rem;
}

.panel ul {
  list-style: none;
  margin: 0;
  padding: 0;
}

.panel li + li {
  border-top: 1px solid var(--border);
}

.panel li.unread {
  background: var(--surface-alt);
}

.panel li button {
  display: flex;
  gap: 0.625rem;
  width: 100%;
  padding: 0.75rem 1rem;
  border: none;
  background: none;
  text-align: left;
  cursor: pointer;
  font: inherit;
}

.icon {
  font-size: 1.125rem;
}

.text {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  min-width: 0;
}

.body {
  font-size: 0.8125rem;
  color: var(--text-muted);
}

.when {
  font-size: 0.6875rem;
  color: var(--text-muted);
}
</style>
