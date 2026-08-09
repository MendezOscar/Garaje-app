/**
 * Filtro de fechas de las listas: hoy, esta semana, este mes o todo.
 *
 * Corta igual que los reportes —la semana arranca el domingo y el mes el día 1— para que
 * "esta semana" signifique lo mismo en las dos pantallas. Se calcula con la hora del
 * navegador, que en el taller es la de Honduras.
 */
export const PERIODS = [
  { key: 'all', label: 'Todo' },
  { key: 'today', label: 'Hoy' },
  { key: 'week', label: 'Semana' },
  { key: 'month', label: 'Mes' },
] as const

export type PeriodKey = (typeof PERIODS)[number]['key']

/** Desde cuándo listar, en ISO. `undefined` en «Todo»: sin filtro. */
export function periodFrom(key: PeriodKey): string | undefined {
  const now = new Date()
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate())

  switch (key) {
    case 'today':
      return start.toISOString()
    case 'week':
      start.setDate(start.getDate() - start.getDay())
      return start.toISOString()
    case 'month':
      return new Date(now.getFullYear(), now.getMonth(), 1).toISOString()
    default:
      return undefined
  }
}
