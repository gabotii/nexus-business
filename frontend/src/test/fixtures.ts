import { http, HttpResponse } from 'msw'
import type { Me } from '@/features/auth/api'
import type { Member, MyTenant, Role, Workspace } from '@/features/tenancy/api'
import { ALL_PERMISSIONS } from '@/features/tenancy/permissions'
import { API, problem } from './server'

export const ana: Me = {
  id: 'u-ana',
  email: 'ana@example.test',
  displayName: 'Ana Pérez',
  emailVerified: true,
}

export const signedIn = (me: Me = ana) => http.get(`${API}/account/me`, () => HttpResponse.json(me))

export const signedOut = () =>
  http.get(`${API}/account/me`, () => problem(401, 'No autenticado', 'unauthorized'))

export const ownerRole = { id: 'r-owner', name: 'Propietario', systemKey: 'owner' }
export const employeeRole = { id: 'r-employee', name: 'Empleado', systemKey: 'employee' }

export const panaderia: MyTenant = {
  id: 't-1',
  name: 'Panadería Núñez',
  slug: 'panaderia-nunez',
  role: ownerRole,
}

export const myTenants = (tenants: MyTenant[]) => http.get(`${API}/tenants`, () => HttpResponse.json(tenants))

export function workspace(overrides: Partial<Workspace> = {}): Workspace {
  return {
    id: panaderia.id,
    name: panaderia.name,
    slug: panaderia.slug,
    currency: 'EUR',
    timeZone: 'Europe/Madrid',
    locale: 'es-ES',
    role: ownerRole,
    permissions: ALL_PERMISSIONS,
    ...overrides,
  }
}

export const workspaceHandler = (ws: Workspace) =>
  http.get(`${API}/tenants/${ws.id}`, () => HttpResponse.json(ws))

export const roles: Role[] = [
  {
    id: 'r-owner',
    name: 'Propietario',
    description: null,
    systemKey: 'owner',
    editable: false,
    deletable: false,
    permissions: ALL_PERMISSIONS,
    memberCount: 1,
  },
  {
    id: 'r-admin',
    name: 'Administrador',
    description: null,
    systemKey: 'administrator',
    editable: true,
    deletable: false,
    permissions: ALL_PERMISSIONS.filter((p) => p !== 'billing.manage'),
    memberCount: 1,
  },
  {
    id: 'r-employee',
    name: 'Empleado',
    description: null,
    systemKey: 'employee',
    editable: true,
    deletable: false,
    permissions: [
      'customers.view',
      'products.view',
      'inventory.view',
      'sales.view',
      'customers.manage',
      'sales.create',
    ],
    memberCount: 1,
  },
]

export const members: Member[] = [
  {
    id: 'm-ana',
    userId: 'u-ana',
    displayName: 'Ana Pérez',
    email: ana.email,
    role: ownerRole,
    status: 'active',
    joinedAt: '2026-10-01T10:00:00Z',
    isYou: true,
  },
  {
    id: 'm-luis',
    userId: 'u-luis',
    displayName: 'Luis Gómez',
    email: 'luis@example.test',
    role: { id: 'r-admin', name: 'Administrador', systemKey: 'administrator' },
    status: 'active',
    joinedAt: '2026-10-02T10:00:00Z',
    isYou: false,
  },
  {
    id: 'm-eva',
    userId: 'u-eva',
    displayName: 'Eva Ruiz',
    email: 'eva@example.test',
    role: employeeRole,
    status: 'active',
    joinedAt: '2026-10-03T10:00:00Z',
    isYou: false,
  },
]
