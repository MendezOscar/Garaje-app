<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * El menú «···» de una fila: lo que se puede hacer con ella, sin llenarla de botones.
 *
 * Una lista con cuatro botones por fila y veinte filas son ochenta botones en pantalla, y
 * entre ellos «Anular» en rojo repetido veinte veces. Queda a la vista la acción más común
 * de cada pantalla; el resto vive aquí, y lo que borra o anula va al final, separado.
 *
 * Se cierra al elegir, al tocar fuera y con Escape, que es lo que espera cualquiera.
 */
const abierto = ref(false)
const caja = ref<HTMLElement | null>(null)

function alternar() {
  abierto.value = !abierto.value
}

function cerrar() {
  abierto.value = false
}

function fuera(evento: MouseEvent) {
  if (abierto.value && !caja.value?.contains(evento.target as Node)) cerrar()
}

function escape(evento: KeyboardEvent) {
  if (evento.key === 'Escape') cerrar()
}

onMounted(() => {
  document.addEventListener('click', fuera)
  document.addEventListener('keydown', escape)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', fuera)
  document.removeEventListener('keydown', escape)
})
</script>

<template>
  <div ref="caja" class="menu" @click.stop>
    <button
      type="button"
      class="abrir"
      :aria-expanded="abierto"
      aria-haspopup="menu"
      aria-label="Más acciones"
      @click="alternar"
    >
      ···
    </button>

    <div v-if="abierto" class="lista" role="menu" @click="cerrar">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.menu {
  position: relative;
  display: inline-flex;
}

.abrir {
  padding: 0.15rem 0.5rem;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
  font-size: 1rem;
  line-height: 1.2;
}

.abrir:hover,
.abrir[aria-expanded='true'] {
  border-color: var(--border);
  color: var(--text);
}

.lista {
  position: absolute;
  top: calc(100% + 0.25rem);
  right: 0;
  z-index: 20;
  min-width: 12rem;
  padding: 0.25rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  box-shadow: 0 10px 30px -12px rgb(0 0 0 / 0.45);
  display: grid;
  gap: 0.125rem;
}

/* Las opciones las pone cada pantalla como botones; el estilo lo pone el menú para que
   todas se vean igual, y lo destructivo se separa con la clase `peligro`. */
.lista :slotted(button),
.lista :slotted(a) {
  display: block;
  width: 100%;
  padding: 0.4rem 0.6rem;
  border: 0;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text);
  font: inherit;
  font-size: 0.875rem;
  text-align: left;
  text-decoration: none;
  cursor: pointer;
}

.lista :slotted(button:hover),
.lista :slotted(a:hover) {
  background: var(--surface-alt);
}

.lista :slotted(.peligro) {
  margin-top: 0.25rem;
  border-top: 1px solid var(--border);
  padding-top: 0.5rem;
  color: var(--danger);
}
</style>
