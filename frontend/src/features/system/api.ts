import { queryOptions } from '@tanstack/react-query'
import { request } from '@/services/http'

export interface Meta {
  productName: string
  apiVersion: string
  version: string
}

/** Global (not tenant-scoped) keys: deployment information is the same for every company. */
export const systemKeys = {
  all: ['system'] as const,
  meta: () => [...systemKeys.all, 'meta'] as const,
  readiness: () => [...systemKeys.all, 'readiness'] as const,
}

export const metaQuery = () =>
  queryOptions({
    queryKey: systemKeys.meta(),
    queryFn: ({ signal }) => request<Meta>('/meta', { signal }),
    staleTime: 5 * 60_000,
  })

/** `/health/ready` answers 200 "Healthy" or 503 when the database (or its isolation guarantees) is not ready. */
export const readinessQuery = () =>
  queryOptions({
    queryKey: systemKeys.readiness(),
    queryFn: ({ signal }) => request<string>('/health/ready', { signal, responseType: 'text' }),
    staleTime: 0,
    retry: false,
  })
