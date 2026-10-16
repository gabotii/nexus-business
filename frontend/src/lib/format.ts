/** Spanish formatting for dates shown in the UI (v1 is Spanish only; the company locale arrives with Phase 3). */
const LOCALE = 'es-ES'

const dateTime = new Intl.DateTimeFormat(LOCALE, { dateStyle: 'medium', timeStyle: 'short' })
const date = new Intl.DateTimeFormat(LOCALE, { dateStyle: 'medium' })
const relative = new Intl.RelativeTimeFormat(LOCALE, { numeric: 'auto' })

export const formatDateTime = (iso: string) => dateTime.format(new Date(iso))

export const formatDate = (iso: string) => date.format(new Date(iso))

const steps: [Intl.RelativeTimeFormatUnit, number][] = [
  ['day', 86_400_000],
  ['hour', 3_600_000],
  ['minute', 60_000],
]

/** "hace 5 minutos", "ayer", "dentro de 3 días"; under a minute is "ahora". */
export function formatRelative(iso: string, now: number = Date.now()): string {
  const diff = new Date(iso).getTime() - now
  for (const [unit, ms] of steps) {
    if (Math.abs(diff) >= ms) return relative.format(Math.round(diff / ms), unit)
  }
  return 'ahora'
}

const moneyFormats = new Map<string, Intl.NumberFormat>()

/** "1.234,50 €" in the company currency (CLP and JPY have no decimals). */
export function formatMoney(amount: number, currency: string): string {
  let format = moneyFormats.get(currency)
  if (!format) {
    format = new Intl.NumberFormat(LOCALE, { style: 'currency', currency })
    moneyFormats.set(currency, format)
  }
  return format.format(amount)
}

const decimal = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 4 })

export const formatNumber = (value: number) => decimal.format(value)

/** "21 %" */
export const formatPercent = (value: number) => `${decimal.format(value)} %`

/**
 * Parses an amount typed by a person: "1.234,5", "1234.5" and "1234,5" are all 1234.5. Returns null when the text is
 * not a number or has more than `maxDecimals` decimals (the API would reject it anyway).
 */
export function parseAmount(text: string, maxDecimals: number): number | null {
  let normalized = text.trim().replace(/\s/g, '')
  if (normalized === '') return null
  if (normalized.includes(',')) normalized = normalized.replace(/\./g, '').replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(normalized)) return null
  const decimals = normalized.split('.')[1]?.length ?? 0
  if (decimals > maxDecimals) return null
  return Number(normalized)
}
