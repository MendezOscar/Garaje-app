const dateTime = new Intl.DateTimeFormat('es-HN', {
  day: '2-digit',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

const dateOnly = new Intl.DateTimeFormat('es-HN', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
})

export function formatDateTime(value: string | null | undefined): string {
  return value ? dateTime.format(new Date(value)) : '—'
}

export function formatDate(value: string | null | undefined): string {
  return value ? dateOnly.format(new Date(value)) : '—'
}

/** "hace 2 h" / "en 3 d". El taller razona en tiempo transcurrido, no en fechas absolutas. */
export function relativeTime(value: string | null | undefined): string {
  if (!value) return '—'

  const diffMs = new Date(value).getTime() - Date.now()
  const rtf = new Intl.RelativeTimeFormat('es', { numeric: 'auto' })

  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['day', 86_400_000],
    ['hour', 3_600_000],
    ['minute', 60_000],
  ]

  for (const [unit, ms] of units) {
    if (Math.abs(diffMs) >= ms) return rtf.format(Math.round(diffMs / ms), unit)
  }
  return 'ahora'
}

/**
 * Link de WhatsApp. El teléfono ya viene normalizado en E.164 desde el backend, así que
 * aquí solo se arma la URL.
 */
export function whatsappLink(phone: string, message?: string): string {
  const base = `https://wa.me/${phone}`
  return message ? `${base}?text=${encodeURIComponent(message)}` : base
}
