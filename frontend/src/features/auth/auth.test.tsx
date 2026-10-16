import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { safeNextPath } from '@/lib/forms'
import { ana, myTenants, panaderia, signedIn, signedOut } from '@/test/fixtures'
import { renderRoute } from '@/test/render'
import { antiforgery, API, problem, server } from '@/test/server'
import { describeDevice } from './AccountPage'

describe('session guards', () => {
  it('sends visitors without a session to sign in, remembering where they were going', async () => {
    server.use(signedOut())

    const { router } = renderRoute('/c/panaderia-nunez/equipo')

    expect(await screen.findByRole('heading', { name: 'Iniciar sesión' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/iniciar-sesion')
    expect(router.state.location.search).toBe(`?next=${encodeURIComponent('/c/panaderia-nunez/equipo')}`)
  })

  it('keeps signed-in users away from the sign-in page', async () => {
    server.use(signedIn(), myTenants([]))

    const { router } = renderRoute('/iniciar-sesion')

    await waitFor(() => expect(router.state.location.pathname).toBe('/empresas'))
  })
})

describe('sign in', () => {
  it('signs in and continues to the requested page', async () => {
    let body: unknown
    let me = false
    server.use(
      http.get(`${API}/account/me`, () =>
        me ? HttpResponse.json(ana) : problem(401, 'No autenticado', 'unauthorized'),
      ),
      antiforgery(),
      http.post(`${API}/auth/login`, async ({ request }) => {
        body = await request.json()
        me = true
        return HttpResponse.json(ana)
      }),
      myTenants([panaderia, { ...panaderia, id: 't-2', slug: 'otra', name: 'Otra' }]),
    )
    const user = userEvent.setup()
    const { router } = renderRoute(`/iniciar-sesion?next=${encodeURIComponent('/empresas')}`)

    await user.type(await screen.findByLabelText('Correo electrónico'), 'ana@example.test')
    await user.type(screen.getByLabelText('Contraseña'), 'correct-horse-battery')
    await user.click(screen.getByLabelText('Recordarme 14 días'))
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/empresas'))
    expect(body).toEqual({ email: 'ana@example.test', password: 'correct-horse-battery', rememberMe: true })
  })

  it('shows the API message for wrong credentials', async () => {
    server.use(
      signedOut(),
      antiforgery(),
      http.post(`${API}/auth/login`, () =>
        problem(401, 'Correo o contraseña incorrectos.', 'invalid_credentials'),
      ),
    )
    const user = userEvent.setup()
    renderRoute('/iniciar-sesion')

    await user.type(await screen.findByLabelText('Correo electrónico'), 'ana@example.test')
    await user.type(screen.getByLabelText('Contraseña'), 'nope')
    await user.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Correo o contraseña incorrectos.')
  })

  it('validates the form before calling the API', async () => {
    server.use(signedOut())
    const user = userEvent.setup()
    renderRoute('/iniciar-sesion')

    await user.click(await screen.findByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('Indica tu correo electrónico.')).toBeInTheDocument()
    expect(screen.getByLabelText('Correo electrónico')).toHaveAttribute('aria-invalid', 'true')
  })
})

describe('registration', () => {
  it('asks to check the inbox afterwards without revealing whether the address existed', async () => {
    server.use(
      signedOut(),
      antiforgery(),
      http.post(`${API}/auth/register`, () => new HttpResponse(null, { status: 202 })),
    )
    const user = userEvent.setup()
    renderRoute('/registro')

    await user.type(await screen.findByLabelText('Nombre'), 'Ana Pérez')
    await user.type(screen.getByLabelText('Correo electrónico'), 'ana@example.test')
    await user.type(screen.getByLabelText('Contraseña'), 'short')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))
    expect(await screen.findByText('La contraseña debe tener al menos 10 caracteres.')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Contraseña'))
    await user.type(screen.getByLabelText('Contraseña'), 'correct-horse-battery')
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }))

    expect(await screen.findByRole('heading', { name: 'Revisa tu correo' })).toBeInTheDocument()
    expect(screen.getByText('ana@example.test')).toBeInTheDocument()
  })
})

describe('e-mail verification link', () => {
  it('confirms the address once on arrival', async () => {
    let calls = 0
    server.use(
      signedOut(),
      antiforgery(),
      http.post(`${API}/auth/verify-email`, () => {
        calls++
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderRoute('/verificar-correo?uid=u-ana&token=abc')

    expect(await screen.findByText(/Tu correo está confirmado/)).toBeInTheDocument()
    expect(calls).toBe(1)
  })
})

describe('helpers', () => {
  it('only follows same-site paths after signing in', () => {
    expect(safeNextPath('/c/acme/equipo')).toBe('/c/acme/equipo')
    expect(safeNextPath('//evil.example')).toBe('/')
    expect(safeNextPath('/\\evil.example')).toBe('/')
    expect(safeNextPath('https://evil.example')).toBe('/')
    expect(safeNextPath(null)).toBe('/')
  })

  it('describes devices from their user agent', () => {
    expect(
      describeDevice(
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/141.0 Safari/537.36 Edg/141.0',
      ),
    ).toEqual({ label: 'Edge en Windows', mobile: false })
    expect(
      describeDevice('Mozilla/5.0 (Linux; Android 15; Pixel 7) Chrome/141.0 Mobile Safari/537.36'),
    ).toEqual({
      label: 'Chrome en Android',
      mobile: true,
    })
    expect(describeDevice(null).label).toBe('Dispositivo desconocido')
  })
})
