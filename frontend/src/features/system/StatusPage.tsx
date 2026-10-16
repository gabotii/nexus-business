import { useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, CircleCheck, CircleDashed, RefreshCw, WifiOff } from 'lucide-react'
import { useEffect } from 'react'
import { PRODUCT_NAME } from '@/app/brand'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError } from '@/services/http'
import { metaQuery, readinessQuery, systemKeys } from './api'

type ServiceState = 'checking' | 'operational' | 'degraded' | 'unreachable'

function stateOf(query: { isPending: boolean; isError: boolean; error: unknown }): ServiceState {
  if (query.isPending) return 'checking'
  if (!query.isError) return 'operational'
  return query.error instanceof ApiError && query.error.isNetworkError ? 'unreachable' : 'degraded'
}

const stateView: Record<
  ServiceState,
  { label: string; variant: 'secondary' | 'success' | 'warning' | 'destructive'; icon: typeof CircleCheck }
> = {
  checking: { label: 'Comprobando…', variant: 'secondary', icon: CircleDashed },
  operational: { label: 'Operativo', variant: 'success', icon: CircleCheck },
  degraded: { label: 'No disponible', variant: 'destructive', icon: CircleAlert },
  unreachable: { label: 'Sin conexión', variant: 'warning', icon: WifiOff },
}

function StatusBadge({ state }: { state: ServiceState }) {
  const { label, variant, icon: Icon } = stateView[state]
  return (
    <Badge variant={variant}>
      <Icon aria-hidden="true" />
      {label}
    </Badge>
  )
}

/** Public service status: whether the API answers and whether it is ready (database reachable and isolated). */
export function StatusPage() {
  const queryClient = useQueryClient()
  const meta = useQuery(metaQuery())
  const readiness = useQuery(readinessQuery())

  useEffect(() => {
    document.title = `Estado del servicio · ${PRODUCT_NAME}`
  }, [])

  const api = stateOf(meta)
  const database = stateOf(readiness)
  const overall: ServiceState =
    api === 'checking' || database === 'checking'
      ? 'checking'
      : api === 'operational' && database === 'operational'
        ? 'operational'
        : api === 'unreachable'
          ? 'unreachable'
          : 'degraded'
  const refreshing = meta.isFetching || readiness.isFetching

  return (
    <div className="mx-auto max-w-2xl space-y-8">
      <div className="space-y-2">
        <p className="text-sm font-medium text-primary">{PRODUCT_NAME}</p>
        <h1 className="text-3xl font-semibold tracking-tight">Estado del servicio</h1>
        <p className="text-muted-foreground">Disponibilidad de la plataforma en este momento.</p>
      </div>

      <Card aria-busy={refreshing}>
        <CardHeader>
          <CardTitle>
            <span className="sr-only">Estado general: </span>
            <StatusBadge state={overall} />
          </CardTitle>
          <CardDescription aria-live="polite">
            {overall === 'operational' && 'Todos los sistemas funcionan con normalidad.'}
            {overall === 'checking' && 'Comprobando los servicios…'}
            {overall === 'degraded' &&
              'Algún servicio no responde correctamente. Vuelve a intentarlo en unos minutos.'}
            {overall === 'unreachable' && 'No se pudo contactar con el servidor. Revisa tu conexión.'}
          </CardDescription>
          <CardAction>
            <Button
              variant="outline"
              size="sm"
              disabled={refreshing}
              onClick={() => queryClient.invalidateQueries({ queryKey: systemKeys.all })}
            >
              <RefreshCw className={refreshing ? 'animate-spin' : undefined} aria-hidden="true" />
              Comprobar de nuevo
            </Button>
          </CardAction>
        </CardHeader>
        <CardContent>
          <dl className="divide-y rounded-lg border">
            <div className="flex items-center justify-between gap-4 px-4 py-3">
              <dt className="text-sm font-medium">API</dt>
              <dd>
                <StatusBadge state={api} />
              </dd>
            </div>
            <div className="flex items-center justify-between gap-4 px-4 py-3">
              <dt className="text-sm font-medium">Base de datos</dt>
              <dd>
                <StatusBadge state={database} />
              </dd>
            </div>
            <div className="flex items-center justify-between gap-4 px-4 py-3">
              <dt className="text-sm font-medium">Versión</dt>
              <dd className="tabular text-sm text-muted-foreground">
                {meta.isPending ? (
                  <Skeleton className="h-4 w-20" />
                ) : meta.data ? (
                  `${meta.data.version} · API ${meta.data.apiVersion}`
                ) : (
                  '—'
                )}
              </dd>
            </div>
          </dl>
        </CardContent>
      </Card>
    </div>
  )
}
