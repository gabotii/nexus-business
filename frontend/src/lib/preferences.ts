/**
 * The only module allowed to touch browser storage (enforced by ESLint). It holds per-device UI preferences —
 * never session, tenant or business data, which live in HttpOnly cookies and in memory respectively.
 * Storage can be unavailable (private mode, blocked site data), so every access degrades to "no preference".
 */
const PREFIX = 'nexus.'

export function readPreference(key: string): string | null {
  try {
    return window.localStorage.getItem(PREFIX + key)
  } catch {
    return null
  }
}

export function writePreference(key: string, value: string): void {
  try {
    window.localStorage.setItem(PREFIX + key, value)
  } catch {
    // Not persisted: the preference still applies for this page view.
  }
}
