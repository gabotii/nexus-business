import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderRoute } from '@/test/render'
import { API, ORIGIN, server } from '@/test/server'

const meta = { productName: 'Nexus Business', apiVersion: 'v1', version: '1.4.2' }

function row(name: string) {
  return screen.getByText(name, { selector: 'dt' }).parentElement as HTMLElement
}

describe('StatusPage', () => {
  it('shows every service as operational, without needing a session', async () => {
    server.use(
      http.get(`${API}/meta`, () => HttpResponse.json(meta)),
      http.get(`${ORIGIN}/health/ready`, () => HttpResponse.text('Healthy')),
    )

    renderRoute('/status')

    expect(await screen.findByText('Todos los sistemas funcionan con normalidad.')).toBeInTheDocument()
    expect(within(row('API')).getByText('Operativo')).toBeInTheDocument()
    expect(within(row('Base de datos')).getByText('Operativo')).toBeInTheDocument()
    expect(within(row('Versión')).getByText('1.4.2 · API v1')).toBeInTheDocument()
  })

  it('shows a checking state while the services have not answered', () => {
    server.use(
      http.get(`${API}/meta`, () => new Promise<never>(() => {})),
      http.get(`${ORIGIN}/health/ready`, () => new Promise<never>(() => {})),
    )

    renderRoute('/status')

    expect(screen.getByText('Comprobando los servicios…')).toBeInTheDocument()
    expect(within(row('API')).getByText('Comprobando…')).toBeInTheDocument()
  })

  it('reports a degraded database when readiness fails, and recovers on retry', async () => {
    let ready = false
    server.use(
      http.get(`${API}/meta`, () => HttpResponse.json(meta)),
      http.get(`${ORIGIN}/health/ready`, () =>
        ready ? HttpResponse.text('Healthy') : HttpResponse.text('Unhealthy', { status: 503 }),
      ),
    )
    renderRoute('/status')

    expect(await screen.findByText(/Algún servicio no responde correctamente/)).toBeInTheDocument()
    expect(within(row('API')).getByText('Operativo')).toBeInTheDocument()
    expect(within(row('Base de datos')).getByText('No disponible')).toBeInTheDocument()

    ready = true
    await userEvent.click(screen.getByRole('button', { name: 'Comprobar de nuevo' }))

    await waitFor(() => expect(within(row('Base de datos')).getByText('Operativo')).toBeInTheDocument())
    expect(screen.getByText('Todos los sistemas funcionan con normalidad.')).toBeInTheDocument()
  })

  it('distinguishes an unreachable server from a failing one', async () => {
    server.use(
      http.get(`${API}/meta`, () => HttpResponse.error()),
      http.get(`${ORIGIN}/health/ready`, () => HttpResponse.error()),
    )

    renderRoute('/status')

    expect(
      await screen.findByText('No se pudo contactar con el servidor. Revisa tu conexión.'),
    ).toBeInTheDocument()
    expect(within(row('API')).getByText('Sin conexión')).toBeInTheDocument()
    expect(within(row('Versión')).getByText('—')).toBeInTheDocument()
  })
})

describe('Not found', () => {
  it('renders a 404 page with a way back for unknown routes', async () => {
    renderRoute('/no-such-page')

    expect(await screen.findByRole('heading', { name: 'Página no encontrada' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Volver al inicio' })).toHaveAttribute('href', '/')
  })
})
