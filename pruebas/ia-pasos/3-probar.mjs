// Prueba a mano de «Proponer los pasos»: corre los casos exportados, guarda lo que propuso
// al lado de lo que el taller cargó de verdad, y dice qué costó.
//
// No forma parte de GarajApp. No toca el panel, ni la app, ni la base: lee casos.json y
// escribe resultado.md. Nada de esto le llega a ningún taller.
//
//   export ANTHROPIC_API_KEY=...        (no la escriba en ningún archivo)
//   npm install
//   npm run probar
//
import fs from "node:fs";
import Anthropic from "@anthropic-ai/sdk";
import { z } from "zod";
import { zodOutputFormat } from "@anthropic-ai/sdk/helpers/zod";

const MODELO = "claude-opus-5-5";
const POR_MILLON = { entrada: 4.0, salida: 20.0 }; // dólares
const LEMPIRAS_POR_DOLAR = 26;

// La forma de la respuesta. Es la misma que tendría que tener en el backend: lo que el
// modelo devuelve se valida contra esto, no se interpreta a mano.
const Propuesta = z.object({
  pasos: z.array(
    z.object({
      titulo: z.string(),
      descripcion: z.string(),
      codigoManoDeObra: z.string().nullable(),
      horas: z.number(),
    }),
  ),
  repuestos: z.array(
    z.object({
      sku: z.string(),
      cantidad: z.number(),
      paraElPaso: z.string(),
    }),
  ),
  nota: z.string().nullable(),
});

const datos = JSON.parse(fs.readFileSync("./casos.json", "utf8"));
const sistema = fs.readFileSync("./2-prompt.md", "utf8");
const cliente = new Anthropic();

const salida = [`# Prueba de «Proponer los pasos» — ${datos.taller}`, ""];
let costoTotal = 0;

for (const caso of datos.casos) {
  process.stdout.write(`${caso.numero}… `);

  const peticion = {
    vehiculo: caso.vehiculo,
    kilometraje: caso.kilometraje,
    diagnostico: caso.diagnostico,
    catalogo: datos.catalogo,
  };

  const r = await cliente.messages.parse({
    model: MODELO,
    max_tokens: 8000,
    // El catálogo va en el sistema y antes que el caso: es lo estable, y en el backend
    // sería lo que queda en caché. Si se mueve de sitio, el caché deja de servir.
    system: [
      { type: "text", text: sistema },
      {
        type: "text",
        text: `## Catálogo del taller\n\n${JSON.stringify(datos.catalogo)}`,
        cache_control: { type: "ephemeral" },
      },
    ],
    messages: [
      {
        role: "user",
        content:
          `Vehículo: ${peticion.vehiculo}\n` +
          `Kilometraje: ${peticion.kilometraje ?? "no anotado"}\n\n` +
          `Diagnóstico del técnico:\n${peticion.diagnostico}`,
      },
    ],
    output_config: {
      effort: "medium",
      format: zodOutputFormat(Propuesta),
    },
  });

  const u = r.usage;
  const costo =
    (u.input_tokens * POR_MILLON.entrada) / 1e6 +
    (u.output_tokens * POR_MILLON.salida) / 1e6;
  costoTotal += costo;

  const p = r.parsed_output;

  salida.push(
    `## ${caso.numero} — ${caso.vehiculo}`,
    "",
    `**Diagnóstico:** ${caso.diagnostico}`,
    "",
    "| Lo que propuso | Lo que el taller cargó |",
    "| --- | --- |",
    `| ${lista(p?.pasos.map((x) => `${x.titulo} (${x.horas} h)`))} | ${lista(
      caso.pasosReales.map((x) => `${x.titulo}${x.horas ? ` (${x.horas} h)` : ""}`),
    )} |`,
    `| ${lista(p?.repuestos.map((x) => `${x.cantidad} × ${x.sku}`))} | ${lista(
      caso.repuestosReales.map((x) => `${x.cantidad} × ${x.sku} ${x.nombre ?? ""}`),
    )} |`,
    "",
    p?.nota ? `**Nota del asistente:** ${p.nota}` : "",
    `<sub>${u.input_tokens} tokens de entrada · ${u.output_tokens} de salida · ` +
      `L ${(costo * LEMPIRAS_POR_DOLAR).toFixed(2)}` +
      (u.cache_read_input_tokens ? ` · ${u.cache_read_input_tokens} leídos de caché` : "") +
      `</sub>`,
    "",
  );
}

const porCaso = costoTotal / datos.casos.length;
salida.push(
  "---",
  "",
  `**${datos.casos.length} casos · L ${(costoTotal * LEMPIRAS_POR_DOLAR).toFixed(2)} en total ` +
    `· L ${(porCaso * LEMPIRAS_POR_DOLAR).toFixed(2)} por propuesta.**`,
  "",
  "La propuesta vale 1 crédito. Si ese costo por propuesta se aleja mucho de L 0.36, hay que",
  "rehacer la cuenta de los créditos antes de ponerla en la propuesta comercial.",
);

fs.writeFileSync("./resultado.md", salida.join("\n"));
console.log(`\n→ resultado.md · L ${(costoTotal * LEMPIRAS_POR_DOLAR).toFixed(2)}`);

function lista(xs) {
  if (!xs || xs.length === 0) return "_nada_";
  return xs.map((x) => `• ${x}`).join("<br />");
}
