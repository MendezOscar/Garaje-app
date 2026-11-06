<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { errorMessage } from '@/api/client'
import { vehicleScansApi } from '@/api/garaj'
import ErrorNote from '@/components/ErrorNote.vue'
import { DTC_STATUS_LABEL, type VehicleScan, type VehicleScanHit } from '@/types/domain'
import { formatDateTime } from '@/utils/format'

/**
 * Las lecturas del adaptador OBD2 que lleva la orden.
 *
 * Aquí solo se consultan: escanear se hace desde la app, que es la que está en el taller con
 * el carro delante. Lo que el panel aporta es la pregunta que el informe en PDF nunca pudo
 * contestar —a qué otros carros les ha salido este mismo código—, y por eso cada código se
 * puede tocar.
 */
const props = defineProps<{ workOrderId: string }>()

const lecturas = ref<VehicleScan[]>([])
const cargando = ref(true)
const error = ref('')

/** El código que se está consultando, y dónde más ha salido. */
const consultado = ref('')
const apariciones = ref<VehicleScanHit[]>([])
const buscando = ref(false)

onMounted(async () => {
  try {
    lecturas.value = await vehicleScansApi.listForWorkOrder(props.workOrderId)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    cargando.value = false
  }
})

async function buscarCodigo(code: string) {
  if (consultado.value === code) {
    consultado.value = ''
    apariciones.value = []
    return
  }

  consultado.value = code
  buscando.value = true
  apariciones.value = []

  try {
    // Fuera las de esta misma orden: lo que se quiere saber es dónde más salió.
    const todas = await vehicleScansApi.search(code)
    apariciones.value = todas.filter((h) => h.workOrderId !== props.workOrderId)
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    buscando.value = false
  }
}
</script>

<template>
  <article v-if="cargando || lecturas.length > 0 || error" class="card">
    <h2>Diagnóstico del escáner</h2>

    <ErrorNote :message="error" />

    <p v-if="cargando" class="vacio">Cargando…</p>

    <div v-for="lectura in lecturas" :key="lectura.id" class="lectura">
      <header>
        <strong>{{ formatDateTime(lectura.scannedAt) }}</strong>
        <span>{{ lectura.scannedByName }}</span>
      </header>

      <p v-if="lectura.codes.length === 0" class="limpia">
        El vehículo no reportó ningún código.
      </p>

      <ul v-else class="codigos">
        <li v-for="codigo in lectura.codes" :key="codigo.code + codigo.status">
          <button type="button" class="codigo" @click="buscarCodigo(codigo.code)">
            {{ codigo.code }}
          </button>
          <div class="que-es">
            <span>{{ codigo.description ?? 'Código propio del fabricante.' }}</span>
            <small>{{ DTC_STATUS_LABEL[codigo.status] }}</small>
          </div>
        </li>
      </ul>

      <p v-if="lectura.notes" class="notas">{{ lectura.notes }}</p>

      <small v-if="lectura.vin" class="pie">VIN leído del vehículo: {{ lectura.vin }}</small>
    </div>

    <section v-if="consultado" class="apariciones">
      <h3>{{ consultado }} en otras órdenes</h3>

      <p v-if="buscando" class="vacio">Buscando…</p>

      <p v-else-if="apariciones.length === 0" class="vacio">
        No le ha salido a ningún otro vehículo del taller.
      </p>

      <ul v-else>
        <li v-for="hit in apariciones" :key="hit.scanId + hit.code">
          <RouterLink :to="`/ordenes/${hit.workOrderId}`">
            {{ hit.plate ?? hit.vehicleLabel }}
          </RouterLink>
          <small>
            {{ hit.vehicleLabel }} · {{ hit.customerName }} · {{ hit.workOrderNumber }} ·
            {{ formatDateTime(hit.scannedAt) }}
          </small>
        </li>
      </ul>
    </section>
  </article>
</template>

<style scoped>
.lectura {
  border-top: 1px solid var(--border);
  padding-top: 0.75rem;
  margin-top: 0.75rem;
}

.lectura:first-of-type {
  border-top: 0;
  margin-top: 0;
}

.lectura header {
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
  font-size: 0.85rem;
}

.lectura header span {
  color: var(--text-muted);
}

.codigos {
  list-style: none;
  margin: 0.5rem 0 0;
  padding: 0;
  display: grid;
  gap: 0.5rem;
}

.codigos li {
  display: flex;
  gap: 0.6rem;
  align-items: flex-start;
}

.codigo {
  background: var(--surface-alt);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 0.15rem 0.45rem;
  font-weight: 700;
  font-family: inherit;
  cursor: pointer;
  white-space: nowrap;
}

.que-es {
  display: grid;
  font-size: 0.85rem;
}

.que-es small,
.pie {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.limpia,
.notas,
.vacio {
  font-size: 0.85rem;
  color: var(--text-muted);
  margin: 0.4rem 0 0;
}

.apariciones {
  border-top: 1px solid var(--border);
  margin-top: 0.9rem;
  padding-top: 0.75rem;
}

.apariciones h3 {
  font-size: 0.9rem;
  margin: 0 0 0.4rem;
}

.apariciones ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 0.5rem;
}

.apariciones li {
  display: grid;
}

.apariciones small {
  color: var(--text-muted);
  font-size: 0.75rem;
}
</style>
