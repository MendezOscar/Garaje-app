# Prueba de «Proponer los pasos»

Probar si la IA propone pasos que el taller de verdad habría cargado, **antes** de construir
nada. Son cuatro archivos y una tarde.

> **Esto no toca GarajApp.** No hay endpoint, ni pantalla, ni migración, ni nada desplegado.
> Es un script que corre en su máquina, lee una exportación de la base y escribe un archivo
> con el resultado. **A ningún taller le llega nada.** Cuando se implemente de verdad, el
> asistente nace apagado y se enciende taller por taller desde el panel de plataforma, con
> `DiagnosticsEnabled` —que es como ya se encienden el módulo de diagnóstico y los topes—.

## Antes de empezar

1. **La llave.** En [console.anthropic.com](https://console.anthropic.com) cree una llave y,
   en la misma consola, **póngale un límite de gasto**. Es el candado que no depende de que
   el código esté bien.
2. La llave va en una variable de entorno de su terminal. **No la escriba en ningún archivo**
   de este repositorio, que es público:
   ```bash
   export ANTHROPIC_API_KEY=sk-ant-...
   ```

## Paso 1 · Sacar los casos

Primero se apunta la terminal a la base. El script reparte la cadena de conexión en las
variables que psql lee solo, y **nunca imprime la contraseña**:

```bash
cd pruebas/ia-pasos
source 0-conectar.sh produccion   # o sin «produccion» para la base local
```

Y ya se sacan los casos:

```bash
psql -v taller='Taller Demo' -v casos=10 -t -A -f 1-casos.sql -o casos.json
```

Saca del taller que se le diga sus diez órdenes más recientes **que tengan diagnóstico escrito
y pasos cargados** —sin pasos no hay con qué comparar—, más el catálogo de mano de obra y de
repuestos. Incluye, por cada orden, **los pasos y repuestos que el taller cargó de verdad**:
esa es la respuesta correcta.

No sale ningún precio, a propósito. La IA propone qué pasos y qué repuestos; el precio lo pone
el catálogo después. Si los precios no entran al prompt, no hay forma de que salga un número
inventado en una cotización.

`casos.json` **no se versiona** —lleva datos de talleres reales—; ya está en el `.gitignore`.

## Paso 2 · Correr la prueba

```bash
npm install
npm run probar
```

Escribe `resultado.md`: por cada orden, lo que propuso al lado de lo que el taller cargó, y
abajo lo que costó cada llamada y el total.

## Paso 3 · Leerlo

La pregunta no es si se parecen las dos columnas. Es esta, orden por orden:

> **¿Habría cargado yo esos pasos?**

Tres cosas que hay que mirar aparte de eso:

- **¿Propuso algún repuesto que no está en el catálogo?** No debería poder: el `sku` sale de
  la lista que se le dio. Si pasa una vez, el prompt está mal y hay que arreglarlo antes de
  seguir.
- **¿Propuso un repuesto que no le sirve a ese vehículo?** Ahí es donde `aplicaA` tiene que
  estar trabajando.
- **¿Qué hizo cuando el diagnóstico era pobre?** Si inventó pasos en vez de decir que falta
  información, eso es lo peligroso y se corrige en el prompt (regla 6).

Para ajustar, se edita `2-prompt.md` y se vuelve a correr. El script no se toca.

## Con qué taller probar

**Primero el Taller Demo**, que es el único con volumen. Sus datos son inventados, así que
sirve para ver que la mecánica funciona —el formato sale bien, respeta el catálogo, no se
inventa repuestos— pero **no para juzgar calidad**: un diagnóstico de demostración no se
parece a uno escrito por un técnico apurado.

**Después Ariel Garage**, con sus órdenes reales. Esas sí dicen si la propuesta sirve.

## Lo que cuesta

Unos **$0.015 por caso**, así que diez casos son menos de veinte centavos. El script lo dice
al terminar, en lempiras. Si el costo por propuesta se aleja mucho de **L 0.36**, hay que
rehacer la cuenta de los créditos antes de dejarla escrita en la propuesta comercial.

## Nota sobre la base local

El contenedor local puede estar atrasado de migraciones: si la consulta se queja de que
`part_applications` no existe, es eso. Se arregla corriendo la API una vez —aplica las
migraciones al arrancar— o probando contra la base donde estén los talleres de verdad, que es
de donde conviene sacar los casos de todas formas.
