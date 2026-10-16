import { queryOptions, useMutation, useQueryClient } from '@tanstack/react-query'
import { request } from '@/services/http'

export interface RoleRef {
  id: string
  name: string
  systemKey: string | null
}

export interface MyTenant {
  id: string
  name: string
  slug: string
  role: RoleRef
}

export interface Workspace {
  id: string
  name: string
  slug: string
  currency: string
  timeZone: string
  locale: string
  role: RoleRef
  /** For showing or hiding actions only; the API checks every permission itself. */
  permissions: string[]
}

export interface Member {
  id: string
  userId: string
  displayName: string
  email: string
  role: RoleRef
  status: 'active' | 'disabled'
  joinedAt: string
  isYou: boolean
}

export interface Role {
  id: string
  name: string
  description: string | null
  systemKey: string | null
  editable: boolean
  deletable: boolean
  permissions: string[]
  memberCount: number
}

export interface Invitation {
  id: string
  email: string
  role: RoleRef
  state: 'pending' | 'expired'
  createdAt: string
  expiresAt: string
  invitedBy: string
}

export interface InvitationPreview {
  tenantName: string
  email: string
  roleName: string
  invitedBy: string
  expiresAt: string
}

/** Every company-scoped key starts with ['tenant', tenantId] (ADR-0004 §8): caches never mix companies. */
export const tenantKeys = {
  all: ['tenant'] as const,
  of: (tenantId: string) => [...tenantKeys.all, tenantId] as const,
  workspace: (tenantId: string) => [...tenantKeys.of(tenantId), 'workspace'] as const,
  members: (tenantId: string) => [...tenantKeys.of(tenantId), 'members'] as const,
  roles: (tenantId: string) => [...tenantKeys.of(tenantId), 'roles'] as const,
  invitations: (tenantId: string) => [...tenantKeys.of(tenantId), 'invitations'] as const,
}

export const myTenantsKey = ['account', 'tenants'] as const

const base = (tenantId: string) => `/tenants/${tenantId}`

export const myTenantsQuery = () =>
  queryOptions({
    queryKey: myTenantsKey,
    queryFn: ({ signal }) => request<MyTenant[]>('/tenants', { signal }),
  })

export const workspaceQuery = (tenantId: string) =>
  queryOptions({
    queryKey: tenantKeys.workspace(tenantId),
    queryFn: ({ signal }) => request<Workspace>(base(tenantId), { signal }),
  })

export const membersQuery = (tenantId: string) =>
  queryOptions({
    queryKey: tenantKeys.members(tenantId),
    queryFn: ({ signal }) => request<Member[]>(`${base(tenantId)}/members`, { signal }),
  })

export const rolesQuery = (tenantId: string) =>
  queryOptions({
    queryKey: tenantKeys.roles(tenantId),
    queryFn: ({ signal }) => request<Role[]>(`${base(tenantId)}/roles`, { signal }),
  })

export const invitationsQuery = (tenantId: string) =>
  queryOptions({
    queryKey: tenantKeys.invitations(tenantId),
    queryFn: ({ signal }) => request<Invitation[]>(`${base(tenantId)}/invitations`, { signal }),
  })

export function useCreateTenant() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { name: string; currency?: string; timeZone?: string }) =>
      request<MyTenant>('/tenants', { method: 'POST', body: input }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: myTenantsKey }),
  })
}

/** Team mutations refresh the whole company subtree: members, roles (member counts) and the caller's workspace. */
function useTenantMutation<TInput, TResult = void>(
  tenantId: string,
  fn: (input: TInput) => Promise<TResult>,
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: tenantKeys.of(tenantId) }),
  })
}

export const useChangeMemberRole = (tenantId: string) =>
  useTenantMutation(tenantId, ({ membershipId, roleId }: { membershipId: string; roleId: string }) =>
    request<void>(`${base(tenantId)}/members/${membershipId}/role`, { method: 'PUT', body: { roleId } }),
  )

export const useSetMemberEnabled = (tenantId: string) =>
  useTenantMutation(tenantId, ({ membershipId, enabled }: { membershipId: string; enabled: boolean }) =>
    request<void>(`${base(tenantId)}/members/${membershipId}/${enabled ? 'enable' : 'disable'}`, {
      method: 'POST',
    }),
  )

export interface SaveRoleInput {
  name: string
  description: string | null
  permissions: string[]
}

export const useCreateRole = (tenantId: string) =>
  useTenantMutation(tenantId, (input: SaveRoleInput) =>
    request<Role>(`${base(tenantId)}/roles`, { method: 'POST', body: input }),
  )

export const useUpdateRole = (tenantId: string) =>
  useTenantMutation(tenantId, ({ roleId, ...input }: SaveRoleInput & { roleId: string }) =>
    request<Role>(`${base(tenantId)}/roles/${roleId}`, { method: 'PUT', body: input }),
  )

export const useDeleteRole = (tenantId: string) =>
  useTenantMutation(tenantId, (roleId: string) =>
    request<void>(`${base(tenantId)}/roles/${roleId}`, { method: 'DELETE' }),
  )

export const useCreateInvitation = (tenantId: string) =>
  useTenantMutation(tenantId, (input: { email: string; roleId: string }) =>
    request<Invitation>(`${base(tenantId)}/invitations`, { method: 'POST', body: input }),
  )

export const useResendInvitation = (tenantId: string) =>
  useTenantMutation(tenantId, (invitationId: string) =>
    request<void>(`${base(tenantId)}/invitations/${invitationId}/resend`, { method: 'POST' }),
  )

export const useRevokeInvitation = (tenantId: string) =>
  useTenantMutation(tenantId, (invitationId: string) =>
    request<void>(`${base(tenantId)}/invitations/${invitationId}`, { method: 'DELETE' }),
  )

export function useLeaveTenant(tenantId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => request<void>(`${base(tenantId)}/leave`, { method: 'POST' }),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: tenantKeys.of(tenantId) })
      return queryClient.invalidateQueries({ queryKey: myTenantsKey })
    },
  })
}

export const previewInvitation = (input: { tenantId: string; token: string }) =>
  request<InvitationPreview>('/invitations/preview', { method: 'POST', body: input })

export function useAcceptInvitation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { tenantId: string; token: string }) =>
      request<MyTenant>('/invitations/accept', { method: 'POST', body: input }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: myTenantsKey }),
  })
}
