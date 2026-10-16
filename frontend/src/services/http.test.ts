import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { antiforgery, API, ORIGIN, problem, server } from '@/test/server'
import { ApiError, buildQuery, errorMessage, request } from './http'

describe('request', () => {
  it('returns the parsed JSON body and sends same-origin JSON requests', async () => {
    let seen: Request | undefined
    server.use(
      antiforgery(),
      http.post(`${API}/things`, async ({ request: req }) => {
        seen = req
        return HttpResponse.json({ id: 7, ...((await req.json()) as object) })
      }),
    )

    const result = await request<{ id: number; name: string }>('/things', {
      method: 'POST',
      body: { name: 'Ana' },
    })

    expect(result).toEqual({ id: 7, name: 'Ana' })
    expect(seen?.headers.get('Content-Type')).toBe('application/json')
    expect(seen?.headers.get('Accept')).toBe('application/json')
  })

  it('serialises the query string, skipping empty values and repeating arrays', async () => {
    let url: URL | undefined
    server.use(
      http.get(`${API}/search`, ({ request: req }) => {
        url = new URL(req.url)
        return HttpResponse.json([])
      }),
    )

    await request('/search', { query: { q: 'café', page: 2, empty: '', none: undefined, tag: ['a', 'b'] } })

    expect(url?.searchParams.get('q')).toBe('café')
    expect(url?.searchParams.get('page')).toBe('2')
    expect(url?.searchParams.has('empty')).toBe(false)
    expect(url?.searchParams.getAll('tag')).toEqual(['a', 'b'])
  })

  it('maps problem details to an ApiError with code, field errors and trace id', async () => {
    server.use(
      antiforgery(),
      http.post(`${API}/orders`, () =>
        problem(400, 'Revisa los datos enviados.', 'validation_failed', {
          'lines[0].quantity': ['Debe ser positiva.'],
        }),
      ),
    )

    const error = await request('/orders', { method: 'POST', body: {} }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(400)
    expect(apiError.code).toBe('validation_failed')
    expect(apiError.traceId).toBe('0af7651916cd43dd8448eb211c80319c')
    expect(errorMessage(apiError)).toBe('Debe ser positiva.')
  })

  it('falls back to a generic message when the error body is not a problem document', async () => {
    server.use(http.get(`${API}/broken`, () => new HttpResponse('<html>Bad gateway</html>', { status: 502 })))

    const error = (await request('/broken').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(502)
    expect(error.code).toBe('http_502')
    expect(error.message).toBe('El servicio no está disponible en este momento.')
  })

  it('normalises transport failures to a network ApiError', async () => {
    server.use(http.get(`${API}/offline`, () => HttpResponse.error()))

    const error = (await request('/offline').catch((e: unknown) => e)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.isNetworkError).toBe(true)
    expect(error.code).toBe('network_error')
  })

  it('rethrows aborts so callers can tell a cancellation from a failure', async () => {
    server.use(http.get(`${API}/slow`, () => new Promise<never>(() => {})))
    const controller = new AbortController()

    const pending = request('/slow', { signal: controller.signal })
    controller.abort()

    await expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  })

  it('returns undefined for 204 and reads text bodies outside the API root', async () => {
    server.use(
      antiforgery(),
      http.delete(`${API}/things/1`, () => new HttpResponse(null, { status: 204 })),
      http.get(`${ORIGIN}/health/ready`, () => HttpResponse.text('Healthy')),
    )

    await expect(request('/things/1', { method: 'DELETE' })).resolves.toBeUndefined()
    await expect(request('/health/ready', { responseType: 'text' })).resolves.toBe('Healthy')
  })
})

describe('buildQuery', () => {
  it('returns an empty string when there is nothing to send', () => {
    expect(buildQuery(undefined)).toBe('')
    expect(buildQuery({ a: null, b: '' })).toBe('')
  })
})

describe('errorMessage', () => {
  it('never exposes raw error text from non-API errors', () => {
    expect(errorMessage(new Error('TypeError: x is undefined at foo.js:1'))).toBe(
      'Ha ocurrido un error inesperado.',
    )
  })
})

describe('antiforgery', () => {
  it('fetches a token before the first state-changing request and echoes it in the header', async () => {
    let header: string | null = null
    server.use(
      antiforgery('token-1'),
      http.post(`${API}/things`, ({ request: req }) => {
        header = req.headers.get('X-XSRF-TOKEN')
        return new HttpResponse(null, { status: 204 })
      }),
    )

    await request('/things', { method: 'POST' })

    expect(header).toBe('token-1')
  })

  it('does not send the token on reads', async () => {
    document.cookie = 'XSRF-TOKEN=token-1; path=/'
    let header: string | null = 'unset'
    server.use(
      http.get(`${API}/things`, ({ request: req }) => {
        header = req.headers.get('X-XSRF-TOKEN')
        return HttpResponse.json([])
      }),
    )

    await request('/things')

    expect(header).toBeNull()
  })

  it('refreshes a rejected token and retries the request once', async () => {
    document.cookie = 'XSRF-TOKEN=stale; path=/'
    const seen: (string | null)[] = []
    server.use(
      antiforgery('fresh'),
      http.post(`${API}/things`, ({ request: req }) => {
        const token = req.headers.get('X-XSRF-TOKEN')
        seen.push(token)
        return token === 'fresh'
          ? HttpResponse.json({ ok: true })
          : problem(400, 'La sesión del formulario ha caducado.', 'csrf_failed')
      }),
    )

    await expect(request('/things', { method: 'POST' })).resolves.toEqual({ ok: true })
    expect(seen).toEqual(['stale', 'fresh'])
  })

  it('gives up after one retry', async () => {
    let attempts = 0
    server.use(
      antiforgery('still-bad'),
      http.put(`${API}/things/1`, () => {
        attempts++
        return problem(400, 'La sesión del formulario ha caducado.', 'csrf_failed')
      }),
    )

    const error = (await request('/things/1', { method: 'PUT', body: {} }).catch(
      (e: unknown) => e,
    )) as ApiError

    expect(error.code).toBe('csrf_failed')
    expect(attempts).toBe(2)
  })
})

describe('empty bodies', () => {
  it('returns undefined for 202 Accepted without content', async () => {
    server.use(
      antiforgery(),
      http.post(`${API}/auth/register`, () => new HttpResponse(null, { status: 202 })),
    )

    await expect(request('/auth/register', { method: 'POST', body: {} })).resolves.toBeUndefined()
  })
})
