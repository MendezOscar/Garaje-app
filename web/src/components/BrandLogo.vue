<script setup lang="ts">
/**
 * La marca, en SVG dentro del documento y no como `<img>`.
 *
 * Así la tuerca toma el azul de `--brand` y la palabra toma `currentColor`: en modo oscuro
 * el logotipo se aclara solo, que es justo lo que pide la guía —sobre fondo oscuro la marca
 * va en claro—. Con un `<img>` habría que mantener dos archivos y acordarse de cambiarlos.
 *
 * La G va como texto en Space Grotesk. Para impresión (vinilo, bordado) hay que usar los
 * SVG de `public/brand/` y convertirla a contornos, como dice el LEEME del paquete.
 */
withDefaults(
  defineProps<{
    /** `isotipo` solo la tuerca; `horizontal` para barras; `vertical` para pantallas de entrada. */
    variant?: 'isotipo' | 'horizontal' | 'vertical'
    /** Alto en píxeles. El mínimo de la guía son 24. */
    height?: number
    /** Sobre azul o grafito, la marca va en blanco pleno. */
    inverted?: boolean
  }>(),
  { variant: 'horizontal', height: 32, inverted: false },
)

/** Tuerca hexagonal con el centro vacío: el mismo trazo en los tres formatos. */
const NUT =
  'M50,0 L93,25 L93,75 L50,100 L7,75 L7,25 Z ' +
  'M50,17.75 L77.7,33.88 L77.7,66.12 L50,82.25 L22.3,66.12 L22.3,33.88 Z'
</script>

<template>
  <svg
    v-if="variant === 'isotipo'"
    class="logo"
    :class="{ inverted }"
    viewBox="0 0 100 100"
    :height="height"
    role="img"
    aria-label="GarajApp"
  >
    <path :d="NUT" fill-rule="evenodd" class="nut" />
    <text x="50" y="52" text-anchor="middle" dominant-baseline="middle" class="mark">G</text>
  </svg>

  <svg
    v-else-if="variant === 'vertical'"
    class="logo"
    :class="{ inverted }"
    viewBox="0 0 200 150"
    :height="height"
    role="img"
    aria-label="GarajApp"
  >
    <g transform="translate(64,0) scale(0.72)">
      <path :d="NUT" fill-rule="evenodd" class="nut" />
      <text x="50" y="52" text-anchor="middle" dominant-baseline="middle" class="mark">G</text>
    </g>
    <text x="15" y="128" dominant-baseline="middle" class="word">GarajApp</text>
  </svg>

  <svg
    v-else
    class="logo"
    :class="{ inverted }"
    viewBox="0 0 320 100"
    :height="height"
    role="img"
    aria-label="GarajApp"
  >
    <path :d="NUT" fill-rule="evenodd" class="nut" />
    <text x="50" y="52" text-anchor="middle" dominant-baseline="middle" class="mark">G</text>
    <text x="122" y="52" dominant-baseline="middle" class="wordmark">GarajApp</text>
  </svg>
</template>

<style scoped>
.logo {
  display: block;
  /* La tuerca siempre cuadrada: nunca se deforma para llenar un hueco. */
  width: auto;
  flex: none;
}

.nut {
  fill: var(--brand);
}

.mark,
.word,
.wordmark {
  fill: currentColor;
  font-family: var(--font-display);
  font-weight: 700;
}

.mark {
  font-size: 40px;
  letter-spacing: -1.6px;
}

.wordmark {
  font-size: 46px;
  letter-spacing: -1.61px;
}

.word {
  font-size: 30px;
  letter-spacing: -1.05px;
}

.inverted .nut,
.inverted .mark,
.inverted .word,
.inverted .wordmark {
  fill: #fff;
}
</style>
