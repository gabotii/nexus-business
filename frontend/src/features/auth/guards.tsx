import { LoaderCircle } from 'lucide-react'
import type { ReactNode } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { errorMessage } from '@/services/http'
import { useMe } from './api'

export function FullPageSpinner({ label = 'Cargando…' }: { label?: string }) {
  return (
    <div
      role="status"
      className="flex min-h-[50dvh] items-center justify-center gap-2 text-sm text-muted-foreground"
    >
      <LoaderCircle className="size-4 animate-spin" aria-hidden="true" />
      {label}
    </div>
  )
}

function SessionError({ error, retry }: { error: unknown; retry: () => void }) {
  return (
    <div className="mx-auto max-w-md space-y-4 py-16">
      <Alert variant="destructive">{errorMessage(error)}</Alert>
      <Button variant="outline" onClick={retry}>
        Reintentar
      </Button>
    </div>
  )
}

/**
 * Children render only with a valid session; otherwise the visitor goes to the sign-in page and comes back here
 * afterwards. Hiding screens is convenience: the API enforces authentication on every request.
 */
export function RequireSession({ children }: { children?: ReactNode }) {
  const me = useMe()
  const location = useLocation()

  if (me.isPending) return <FullPageSpinner />
  if (me.isError) return <SessionError error={me.error} retry={() => void me.refetch()} />
  if (!me.data) {
    const next = encodeURIComponent(location.pathname + location.search)
    return <Navigate to={`/iniciar-sesion?next=${next}`} replace />
  }
  return children ?? <Outlet />
}

/** Sign-in and sign-up screens are pointless with a session: go to the app instead. */
export function GuestOnly({ children }: { children?: ReactNode }) {
  const me = useMe()
  if (me.isPending) return <FullPageSpinner />
  if (me.data) return <Navigate to="/" replace />
  return children ?? <Outlet />
}
