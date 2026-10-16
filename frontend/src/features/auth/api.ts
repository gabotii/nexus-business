import { queryOptions, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, request } from '@/services/http'

export interface Me {
  id: string
  email: string
  displayName: string
  emailVerified: boolean
}

export interface DeviceSession {
  id: string
  createdAt: string
  lastSeenAt: string
  expiresAt: string
  userAgent: string | null
  ipPrefix: string | null
  isCurrent: boolean
}

/**
 * Keys of data that belongs to the signed-in person, not to a company. Company data lives under
 * `['tenant', tenantId, …]` (see features/tenancy/api.ts). Signing in or out clears the whole cache.
 */
export const accountKeys = {
  all: ['account'] as const,
  me: () => [...accountKeys.all, 'me'] as const,
  sessions: () => [...accountKeys.all, 'sessions'] as const,
}

/** The signed-in user, or null when there is no valid session (401 is an answer, not an error). */
export const meQuery = () =>
  queryOptions({
    queryKey: accountKeys.me(),
    queryFn: async ({ signal }) => {
      try {
        return await request<Me>('/account/me', { signal })
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    staleTime: 60_000,
  })

export const useMe = () => useQuery(meQuery())

export interface LoginInput {
  email: string
  password: string
  rememberMe: boolean
}

export interface RegisterInput {
  email: string
  password: string
  displayName: string
}

export function useLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: LoginInput) => request<Me>('/auth/login', { method: 'POST', body: input }),
    onSuccess: (me) => {
      // A new identity: nothing cached for a previous user may survive (R11).
      queryClient.clear()
      queryClient.setQueryData(accountKeys.me(), me)
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => request<void>('/auth/logout', { method: 'POST' }),
    onSettled: () => {
      queryClient.clear()
      queryClient.setQueryData(accountKeys.me(), null)
    },
  })
}

export const useRegister = () =>
  useMutation({
    mutationFn: (input: RegisterInput) => request<void>('/auth/register', { method: 'POST', body: input }),
  })

export function useVerifyEmail() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { userId: string; token: string }) =>
      request<void>('/auth/verify-email', { method: 'POST', body: input }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountKeys.me() }),
  })
}

export const useResendVerification = () =>
  useMutation({ mutationFn: () => request<void>('/auth/resend-verification', { method: 'POST' }) })

export const useForgotPassword = () =>
  useMutation({
    mutationFn: (email: string) =>
      request<void>('/auth/forgot-password', { method: 'POST', body: { email } }),
  })

export const useResetPassword = () =>
  useMutation({
    mutationFn: (input: { userId: string; token: string; newPassword: string }) =>
      request<void>('/auth/reset-password', { method: 'POST', body: input }),
  })

export function useUpdateProfile() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (displayName: string) =>
      request<Me>('/account/profile', { method: 'PUT', body: { displayName } }),
    onSuccess: (me) => queryClient.setQueryData(accountKeys.me(), me),
  })
}

export function useChangePassword() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { currentPassword: string; newPassword: string }) =>
      request<void>('/account/password', { method: 'POST', body: input }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountKeys.sessions() }),
  })
}

export const sessionsQuery = () =>
  queryOptions({
    queryKey: accountKeys.sessions(),
    queryFn: ({ signal }) => request<DeviceSession[]>('/account/sessions', { signal }),
  })

export function useRevokeSession() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (sessionId: string) => request<void>(`/account/sessions/${sessionId}`, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountKeys.sessions() }),
  })
}

export function useRevokeOtherSessions() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => request<{ revoked: number }>('/account/sessions/revoke-others', { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: accountKeys.sessions() }),
  })
}
