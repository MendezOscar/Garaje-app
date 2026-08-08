# GarajApp — paquete de marca

Ruta elegida: **1a Tuerca G**.

## Contenido

    logo/     8 SVG: horizontal, vertical e isotipo en azul, blanco y grafito
    icono/    favicon.svg + PNG del icono de app (1024 / 512 / 192 / 180 / 48)
    tokens/   garajapp-tokens.css (web) y garaj_brand.dart (Flutter)

## Colores

| Uso | Hex |
| --- | --- |
| Azul Garaj (marca, acción, links) | `#1F6FEB` |
| Azul profundo (hover, encabezados) | `#124293` |
| Grafito (texto, modo oscuro) | `#14171C` |
| Ámbar (espera aprobación / repuestos) | `#F2A31A` |
| Verde (listo, entregado, pagado) | `#1FA971` |
| Rojo (cancelado, sin stock) | `#C0392B` |
| Superficie / tarjeta / borde | `#F7F8FA` · `#FFFFFF` · `#D9DDE4` |

Máximo dos colores por pantalla más grafito. Ámbar y verde son **solo de estado**.

## Tipografía

- **Space Grotesk 700** — logotipo y titulares.
- **IBM Plex Sans** — interfaz y texto (400 / 500 / 600). Mínimo 15 px en móvil.
- **IBM Plex Mono** — montos, cantidades y correlativos (`MTZ-000123`, `L 2,150.00`).

Web: `https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;700&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap`
Flutter: descargar los .ttf y declararlos en `pubspec.yaml` como `SpaceGrotesk`, `IBMPlexSans`, `IBMPlexMono`.

## Reglas

- Área de respeto: media tuerca por lado.
- Tamaño mínimo: 24 px de alto en pantalla, 10 mm impreso.
- Sobre azul o grafito, la marca va en blanco pleno.
- Nunca gradientes, colores de estado, rotación ni deformación: la tuerca siempre es cuadrada.
- Sobre foto, primero una placa sólida.

## Nota técnica de los SVG

La **G** va como texto con Space Grotesk. Antes de mandar a imprimir (bordado, vinilo,
serigrafía) conviértela a contornos en el editor vectorial — así no depende de la fuente.
Los PNG del icono ya vienen rasterizados y no tienen esa dependencia.
