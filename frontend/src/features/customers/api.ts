import { keepPreviousData, queryOptions, useMutation, useQueryClient } from '@tanstack/react-query'
import { tenantKeys } from '@/features/tenancy/api'
import { request } from '@/services/http'

export type CustomerKind = 'person' | 'company'

export interface Address {
  line: string | null
  city: string | null
  postalCode: string | null
  region: string | null
  countryCode: string | null
}

export interface TagRef {
  id: string
  name: string
  color: string
}

export interface Tag extends TagRef {
  customerCount: number
}

export interface CustomerListItem {
  id: string
  kind: CustomerKind
  name: string
  legalName: string | null
  email: string | null
  phone: string | null
  taxId: string | null
  city: string | null
  tags: TagRef[]
  archived: boolean
  createdAt: string
}

export interface Customer {
  id: string
  kind: CustomerKind
  name: string
  legalName: string | null
  taxId: string | null
  email: string | null
  phone: string | null
  address: Address
  tags: TagRef[]
  archived: boolean
  createdAt: string
  updatedAt: string | null
  version: number
}

export interface CustomerNote {
  id: string
  body: string
  authorName: string
  isYours: boolean
  createdAt: string
}

export interface Page<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface CustomerFilters {
  q: string
  tagId: string
  archived: boolean
  sort: 'name' | 'recent'
  page: number
}

export interface SaveCustomerInput {
  kind: CustomerKind
  name: string
  legalName: string | null
  taxId: string | null
  email: string | null
  phone: string | null
  address: Address
  tagIds: string[]
  version?: number
}

export const customerKeys = {
  all: (tenantId: string) => [...tenantKeys.of(tenantId), 'customers'] as const,
  list: (tenantId: string, filters: CustomerFilters) =>
    [...customerKeys.all(tenantId), 'list', filters] as const,
  detail: (tenantId: string, id: string) => [...customerKeys.all(tenantId), 'detail', id] as const,
  notes: (tenantId: string, id: string) => [...customerKeys.all(tenantId), 'notes', id] as const,
  tags: (tenantId: string) => [...tenantKeys.of(tenantId), 'tags'] as const,
}

const base = (tenantId: string) => `/tenants/${tenantId}`

export const customersQuery = (tenantId: string, filters: CustomerFilters) =>
  queryOptions({
    queryKey: customerKeys.list(tenantId, filters),
    queryFn: ({ signal }) =>
      request<Page<CustomerListItem>>(`${base(tenantId)}/customers`, {
        signal,
        query: {
          q: filters.q,
          tagId: filters.tagId,
          archived: filters.archived || undefined,
          sort: filters.sort === 'recent' ? 'recent' : undefined,
          page: filters.page,
          pageSize: 25,
        },
      }),
    // Keep showing the current page while the next one (or a new search) loads.
    placeholderData: keepPreviousData,
  })

export const customerQuery = (tenantId: string, id: string) =>
  queryOptions({
    queryKey: customerKeys.detail(tenantId, id),
    queryFn: ({ signal }) => request<Customer>(`${base(tenantId)}/customers/${id}`, { signal }),
  })

export const notesQuery = (tenantId: string, id: string) =>
  queryOptions({
    queryKey: customerKeys.notes(tenantId, id),
    queryFn: ({ signal }) => request<CustomerNote[]>(`${base(tenantId)}/customers/${id}/notes`, { signal }),
  })

export const tagsQuery = (tenantId: string) =>
  queryOptions({
    queryKey: customerKeys.tags(tenantId),
    queryFn: ({ signal }) => request<Tag[]>(`${base(tenantId)}/tags`, { signal }),
    staleTime: 60_000,
  })

function useCustomerMutation<TInput, TResult>(tenantId: string, fn: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: customerKeys.all(tenantId) }),
        queryClient.invalidateQueries({ queryKey: customerKeys.tags(tenantId) }),
      ]),
  })
}

export const useCreateCustomer = (tenantId: string) =>
  useCustomerMutation(tenantId, (input: SaveCustomerInput) =>
    request<Customer>(`${base(tenantId)}/customers`, { method: 'POST', body: input }),
  )

export const useUpdateCustomer = (tenantId: string, id: string) =>
  useCustomerMutation(tenantId, (input: SaveCustomerInput) =>
    request<Customer>(`${base(tenantId)}/customers/${id}`, { method: 'PUT', body: input }),
  )

export const useSetCustomerArchived = (tenantId: string, id: string) =>
  useCustomerMutation(tenantId, (archived: boolean) =>
    request<void>(`${base(tenantId)}/customers/${id}/${archived ? 'archive' : 'restore'}`, {
      method: 'POST',
    }),
  )

export const useAddNote = (tenantId: string, id: string) =>
  useCustomerMutation(tenantId, (body: string) =>
    request<CustomerNote>(`${base(tenantId)}/customers/${id}/notes`, { method: 'POST', body: { body } }),
  )

export const useDeleteNote = (tenantId: string, id: string) =>
  useCustomerMutation(tenantId, (noteId: string) =>
    request<void>(`${base(tenantId)}/customers/${id}/notes/${noteId}`, { method: 'DELETE' }),
  )

export const useCreateTag = (tenantId: string) =>
  useCustomerMutation(tenantId, (input: { name: string; color: string }) =>
    request<Tag>(`${base(tenantId)}/tags`, { method: 'POST', body: input }),
  )

export const useUpdateTag = (tenantId: string) =>
  useCustomerMutation(tenantId, ({ id, ...input }: { id: string; name: string; color: string }) =>
    request<Tag>(`${base(tenantId)}/tags/${id}`, { method: 'PUT', body: input }),
  )

export const useDeleteTag = (tenantId: string) =>
  useCustomerMutation(tenantId, (id: string) =>
    request<void>(`${base(tenantId)}/tags/${id}`, { method: 'DELETE' }),
  )

export const TAG_COLORS = [
  'slate',
  'red',
  'orange',
  'amber',
  'green',
  'teal',
  'blue',
  'violet',
  'pink',
] as const

/** Swatch classes per palette colour; the name is always shown too, so colour is never the only cue. */
export const tagColorClass: Record<string, string> = {
  slate: 'bg-slate-500/15 text-slate-700 dark:text-slate-300',
  red: 'bg-red-500/15 text-red-700 dark:text-red-300',
  orange: 'bg-orange-500/15 text-orange-700 dark:text-orange-300',
  amber: 'bg-amber-500/15 text-amber-800 dark:text-amber-300',
  green: 'bg-green-500/15 text-green-700 dark:text-green-300',
  teal: 'bg-teal-500/15 text-teal-700 dark:text-teal-300',
  blue: 'bg-blue-500/15 text-blue-700 dark:text-blue-300',
  violet: 'bg-violet-500/15 text-violet-700 dark:text-violet-300',
  pink: 'bg-pink-500/15 text-pink-700 dark:text-pink-300',
}

export const tagColorLabel: Record<string, string> = {
  slate: 'Gris',
  red: 'Rojo',
  orange: 'Naranja',
  amber: 'Ámbar',
  green: 'Verde',
  teal: 'Turquesa',
  blue: 'Azul',
  violet: 'Violeta',
  pink: 'Rosa',
}
