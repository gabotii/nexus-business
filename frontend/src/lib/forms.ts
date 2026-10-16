import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError, errorMessage } from '@/services/http'

/**
 * Puts the API's validation errors (keyed by JSON path, e.g. "newPassword") on the matching form fields.
 * Returns the message for everything that is not a field error, to show above the form; undefined when all of the
 * errors landed on fields.
 */
export function applyApiErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): string | undefined {
  if (!(error instanceof ApiError)) return errorMessage(error)

  let placed = false
  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const field = fields.find((f) => f === key)
    if (field && messages[0]) {
      setError(field, { type: 'server', message: messages[0] })
      placed = true
    }
  }
  return placed ? undefined : error.message
}

/** Only same-site paths are followed after signing in (no open redirects). */
export function safeNextPath(next: string | null): string {
  return next && next.startsWith('/') && !next.startsWith('//') && !next.startsWith('/\\') ? next : '/'
}
