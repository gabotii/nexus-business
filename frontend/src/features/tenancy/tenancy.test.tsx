import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import {
  ana,
  employeeRole,
  members,
  myTenants,
  panaderia,
  roles,
  signedIn,
  signedOut,
  workspace,
  workspaceHandler,
} from '@/test/fixtures'
import { renderRoute } from '@/test/render'
import { antiforgery, API, problem, server } from '@/test/server'
import { ALL_PERMISSIONS } from './permissions'
import { grantableRoles } from './TeamPage'

const teamHandlers = () => [
  http.get(`${API}/tenants/${panaderia.id}/members`, () => HttpResponse.json(members)),
  http.get(`${API}/tenants/${panaderia.id}/roles`, () => HttpResponse.json(roles)),
  http.get(`${API}/tenants/${panaderia.id}/invitations`, () => HttpResponse.json([])),
]

describe('company selection', () => {
  it('opens the only company straight away', async () => {
    server.use(signedIn(), myTenants([panaderia]), workspaceHandler(workspace()))

    const { router } = renderRoute('/')

    await waitFor(() => expect(router.state.location.pathname).toBe('/c/panaderia-nunez'))
    expect(await screen.findByRole('heading', { name: 'Panadería Núñez' })).toBeInTheDocument()
  })

  it('offers to create the first company', async () => {
    let created: unknown
    server.use(
      signedIn(),
      myTenants([]),
      antiforgery(),
      http.post(`${API}/tenants`, async ({ request }) => {
        created = await request.json()
        return HttpResponse.json(panaderia, { status: 201 })
      }),
      workspaceHandler(workspace()),
    )
    const user = userEvent.setup()
    const { router } = renderRoute('/empresas')

    await user.type(await screen.findByLabelText('Nombre de la empresa'), 'Panadería Núñez')
    await user.click(screen.getByRole('button', { name: 'Crear empresa' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/c/panaderia-nunez'))
    expect(created).toEqual({ name: 'Panadería Núñez', currency: 'EUR', timeZone: 'Europe/Madrid' })
  })

  it('asks unverified users to confirm their e-mail instead of offering to create a company', async () => {
    server.use(signedIn({ ...ana, emailVerified: false }), myTenants([]))

    renderRoute('/empresas')

    expect(await screen.findByText(/para crear empresas y aceptar invitaciones/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Nombre de la empresa')).not.toBeInTheDocument()
  })

  it('treats a company the user does not belong to as not found', async () => {
    server.use(signedIn(), myTenants([panaderia]))

    renderRoute('/c/otra-empresa')

    expect(await screen.findByRole('heading', { name: 'Página no encontrada' })).toBeInTheDocument()
  })
})

describe('workspace navigation follows permissions', () => {
  it('hides the team sections from roles without members.view', async () => {
    const permissions = ['customers.view', 'sales.view']
    server.use(
      signedIn(),
      myTenants([{ ...panaderia, role: employeeRole }]),
      workspaceHandler(workspace({ role: employeeRole, permissions })),
    )

    renderRoute('/c/panaderia-nunez')

    expect(await screen.findByRole('heading', { name: 'Panadería Núñez' })).toBeInTheDocument()
    expect(screen.queryAllByRole('link', { name: 'Equipo' })).toHaveLength(0)
    expect(screen.getAllByRole('link', { name: 'Inicio' }).length).toBeGreaterThan(0)
  })
})

describe('team page', () => {
  it('lets an owner invite and change roles of others, but never their own', async () => {
    server.use(signedIn(), myTenants([panaderia]), workspaceHandler(workspace()), ...teamHandlers())

    renderRoute('/c/panaderia-nunez/equipo')

    const table = await screen.findByRole('table', { name: 'Miembros de la empresa' })
    expect(screen.getByRole('button', { name: 'Invitar' })).toBeInTheDocument()
    expect(within(table).getByRole('combobox', { name: 'Rol de Eva Ruiz' })).toBeInTheDocument()
    expect(within(table).queryByRole('combobox', { name: 'Rol de Ana Pérez' })).not.toBeInTheDocument()
  })

  it('sends an invitation with the chosen role', async () => {
    let sent: unknown
    server.use(
      signedIn(),
      myTenants([panaderia]),
      workspaceHandler(workspace()),
      ...teamHandlers(),
      antiforgery(),
      http.post(`${API}/tenants/${panaderia.id}/invitations`, async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderRoute('/c/panaderia-nunez/equipo')

    await user.click(await screen.findByRole('button', { name: 'Invitar' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Correo electrónico'), 'nuevo@example.test')
    await user.selectOptions(within(dialog).getByLabelText('Rol'), 'r-admin')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))

    await waitFor(() => expect(sent).toEqual({ email: 'nuevo@example.test', roleId: 'r-admin' }))
  })

  it('shows the reason when the API refuses a change', async () => {
    server.use(
      signedIn(),
      myTenants([panaderia]),
      workspaceHandler(workspace()),
      ...teamHandlers(),
      antiforgery(),
      http.put(`${API}/tenants/${panaderia.id}/members/m-eva/role`, () =>
        problem(422, 'La empresa debe conservar al menos un propietario activo.', 'last_owner'),
      ),
    )
    const user = userEvent.setup()
    renderRoute('/c/panaderia-nunez/equipo')

    await user.selectOptions(await screen.findByRole('combobox', { name: 'Rol de Eva Ruiz' }), 'r-admin')

    expect(
      await screen.findByText('La empresa debe conservar al menos un propietario activo.'),
    ).toBeInTheDocument()
  })
})

describe('invitation link', () => {
  it('shows what it offers and asks visitors to sign in', async () => {
    server.use(
      signedOut(),
      antiforgery(),
      http.post(`${API}/invitations/preview`, () =>
        HttpResponse.json({
          tenantName: 'Taller Ruiz',
          email: 'eva@example.test',
          roleName: 'Responsable',
          invitedBy: 'Elena',
          expiresAt: '2026-10-22T10:00:00Z',
        }),
      ),
    )

    renderRoute('/invitacion?empresa=t-9&token=secret')

    expect(await screen.findByRole('heading', { name: 'Únete a Taller Ruiz' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Iniciar sesión para aceptar' })).toHaveAttribute(
      'href',
      `/iniciar-sesion?next=${encodeURIComponent('/invitacion?empresa=t-9&token=secret')}`,
    )
  })

  it('explains when the link is no longer valid', async () => {
    server.use(
      signedOut(),
      antiforgery(),
      http.post(`${API}/invitations/preview`, () => problem(404, 'No existe.', 'not_found')),
    )

    renderRoute('/invitacion?empresa=t-9&token=used')

    expect(await screen.findByText(/ya se usó o ha caducado/)).toBeInTheDocument()
  })
})

describe('grantableRoles', () => {
  it('offers only roles within the caller permissions, and Owner only to owners', () => {
    const adminPermissions = ALL_PERMISSIONS.filter((p) => p !== 'billing.manage')

    expect(grantableRoles(roles, ALL_PERMISSIONS, true).map((r) => r.id)).toEqual([
      'r-owner',
      'r-admin',
      'r-employee',
    ])
    expect(grantableRoles(roles, adminPermissions, false).map((r) => r.id)).toEqual(['r-admin', 'r-employee'])
    expect(grantableRoles(roles, ['sales.view'], false)).toEqual([])
  })
})
