import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

/** Shared MSW server; each test registers the handlers it needs with server.use(...). */
export const server = setupServer()

/** jsdom's origin: the client builds absolute same-origin URLs from window.location. */
export const ORIGIN = window.location.origin
export const API = `${ORIGIN}/api/v1`

export function problem(status: number, detail: string, code: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    { status, title: 'Error', detail, code, errors, traceId: '0af7651916cd43dd8448eb211c80319c' },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

/** The antiforgery endpoint as the API implements it: sets the readable XSRF-TOKEN cookie. */
export function antiforgery(token = 'xsrf-test-token') {
  return http.get(`${API}/auth/antiforgery`, () => {
    document.cookie = `XSRF-TOKEN=${token}; path=/`
    return new HttpResponse(null, { status: 204 })
  })
}

/** Removes the XSRF cookie so a test starts as a fresh browser. */
export function clearXsrfCookie() {
  document.cookie = 'XSRF-TOKEN=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT'
}
