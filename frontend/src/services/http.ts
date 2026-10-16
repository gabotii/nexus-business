/**
 * Typed access to the API. Every request is same-origin (nginx in production, the Vite proxy in development), so
 * the browser attaches the HttpOnly session cookie by itself; the SPA never sees or stores credentials.
 */
export const API_BASE = '/api/v1'

/** Status 0 means the request never got an HTTP answer (offline, DNS, proxy down). */
export const NETWORK_ERROR_STATUS = 0

/** An RFC 9457 problem returned by the API, or a transport failure normalised to the same shape. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>
  readonly traceId: string | undefined
  /** Extra fields of the problem (e.g. `existingCustomerId` on a duplicate). */
  readonly details: Readonly<Record<string, unknown>>

  constructor(
    status: number,
    message: string,
    code: string,
    fieldErrors: Record<string, string[]> = {},
    traceId?: string,
    details: Record<string, unknown> = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
    this.traceId = traceId
    this.details = details
  }

  get isNetworkError(): boolean {
    return this.status === NETWORK_ERROR_STATUS
  }
}

/** A message suitable for a toast or an inline error, for any thrown value. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const firstFieldError = Object.values(error.fieldErrors)[0]?.[0]
    return firstFieldError ?? error.message
  }
  return 'Ha ocurrido un error inesperado.'
}

type QueryValue = string | number | boolean | null | undefined | readonly (string | number)[]
export type Query = Record<string, QueryValue>

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  /** Multipart upload; the browser sets the Content-Type with its boundary. */
  formData?: FormData
  query?: Query
  signal?: AbortSignal
  /** Health endpoints answer text/plain. */
  responseType?: 'json' | 'text'
}

export function buildQuery(query: Query | undefined): string {
  if (!query) return ''
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) value.forEach((v) => params.append(key, String(v)))
    else params.append(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

interface ProblemBody {
  detail?: string
  title?: string
  status?: number
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
  [extension: string]: unknown
}

function fallbackMessage(status: number): string {
  if (status === 429) return 'Demasiadas solicitudes. Espera un momento e inténtalo de nuevo.'
  if (status >= 500) return 'El servicio no está disponible en este momento.'
  return 'La solicitud no se pudo completar.'
}

async function toApiError(response: Response): Promise<ApiError> {
  let body: ProblemBody = {}
  const contentType = response.headers.get('Content-Type') ?? ''
  if (contentType.includes('json')) {
    try {
      body = (await response.json()) as ProblemBody
    } catch {
      // A malformed body falls back to the generic message below.
    }
  }
  const { detail, title: _title, status: _status, code, traceId, errors, ...details } = body
  return new ApiError(
    response.status,
    detail ?? fallbackMessage(response.status),
    code ?? `http_${response.status}`,
    errors ?? {},
    traceId ?? response.headers.get('X-Correlation-Id') ?? undefined,
    details,
  )
}

/** Cookie the API issues for the double-submit antiforgery check (ADR-0002); readable on purpose, not a secret. */
export const XSRF_COOKIE = 'XSRF-TOKEN'
export const XSRF_HEADER = 'X-XSRF-TOKEN'

export function readXsrfToken(): string | undefined {
  for (const part of document.cookie.split(';')) {
    const [name, ...value] = part.trim().split('=')
    if (name === XSRF_COOKIE) return decodeURIComponent(value.join('='))
  }
  return undefined
}

let pendingToken: Promise<void> | undefined

/** Asks the API for a fresh antiforgery cookie. Concurrent callers share one request. */
export function refreshXsrfToken(): Promise<void> {
  pendingToken ??= fetch(new URL(`${API_BASE}/auth/antiforgery`, window.location.origin), {
    credentials: 'same-origin',
  })
    .then(
      () => undefined,
      () => undefined,
    )
    .finally(() => {
      pendingToken = undefined
    })
  return pendingToken
}

const isUnsafe = (method: string) => method !== 'GET'

/**
 * Sends a request and returns the parsed body. Paths are relative to the API root unless they start with
 * "/health". Throws {@link ApiError} for HTTP errors and transport failures; an aborted request rethrows the
 * AbortError so TanStack Query can recognise the cancellation.
 *
 * State-changing requests carry the antiforgery token. When the API rejects it (it is bound to the signed-in user,
 * so it goes stale after the session ends elsewhere) the token is refreshed and the request retried once.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? 'GET'
  if (!isUnsafe(method)) return send<T>(path, options)

  if (readXsrfToken() === undefined) await refreshXsrfToken()
  try {
    return await send<T>(path, options)
  } catch (error) {
    if (!(error instanceof ApiError && error.code === 'csrf_failed')) throw error
    await refreshXsrfToken()
    return send<T>(path, options)
  }
}

async function send<T>(path: string, options: RequestOptions): Promise<T> {
  const method = options.method ?? 'GET'
  const headers: Record<string, string> = {
    Accept: options.responseType === 'text' ? 'text/plain' : 'application/json',
  }
  const xsrf = isUnsafe(method) ? readXsrfToken() : undefined
  if (xsrf) headers[XSRF_HEADER] = xsrf
  let body: string | FormData | undefined
  if (options.formData) {
    body = options.formData
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(options.body)
  }

  const root = path.startsWith('/health') ? '' : API_BASE
  const url = new URL(`${root}${path}${buildQuery(options.query)}`, window.location.origin)

  let response: Response
  try {
    response = await fetch(url, { method, headers, body, signal: options.signal, credentials: 'same-origin' })
  } catch (error) {
    // Not `instanceof DOMException`: the abort error may come from another realm (fetch polyfills, test runners).
    if (options.signal?.aborted || (error instanceof Error && error.name === 'AbortError')) throw error
    throw new ApiError(
      NETWORK_ERROR_STATUS,
      'No se pudo conectar con el servidor. Revisa tu conexión.',
      'network_error',
    )
  }

  if (!response.ok) throw await toApiError(response)
  // 204, and 202 Accepted without a body (registration, password recovery), carry nothing to parse.
  const text = response.status === 204 ? '' : await response.text()
  if (options.responseType === 'text') return text as T
  return (text === '' ? undefined : JSON.parse(text)) as T
}
