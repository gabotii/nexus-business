import { useQuery } from '@tanstack/react-query'
import { Building2 } from 'lucide-react'
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { useLogout, useMe } from '@/features/auth/api'
import { AuthCard } from '@/features/auth/AuthCard'
import { FullPageSpinner } from '@/features/auth/guards'
import { formatDate } from '@/lib/format'
import { ApiError, errorMessage } from '@/services/http'
import { previewInvitation, useAcceptInvitation } from './api'

/**
 * Landing page of the e-mailed link (`/invitacion?empresa=…&token=…`). Anyone with the link sees what it offers;
 * accepting needs a signed-in, verified account with the invited address (checked by the API).
 */
export function InvitationPage() {
  const [params] = useSearchParams()
  const tenantId = params.get('empresa') ?? ''
  const token = params.get('token') ?? ''
  const location = useLocation()
  const navigate = useNavigate()
  const me = useMe()
  const logout = useLogout()
  const accept = useAcceptInvitation()
  const valid = Boolean(tenantId && token)
  const preview = useQuery({
    // The token is a secret: keep it out of the shared cache key namespace for tenants and drop it on unmount.
    queryKey: ['invitation-preview', tenantId, token],
    queryFn: () => previewInvitation({ tenantId, token }),
    enabled: valid,
    gcTime: 0,
    retry: false,
  })

  if (!valid) {
    return (
      <AuthCard title="Invitación">
        <Alert variant="destructive">
          El enlace de invitación no es válido. Comprueba que lo has copiado entero.
        </Alert>
      </AuthCard>
    )
  }
  if (preview.isPending || me.isPending) return <FullPageSpinner />
  if (preview.isError) {
    const gone = preview.error instanceof ApiError && preview.error.status === 404
    return (
      <AuthCard title="Invitación no disponible">
        <Alert variant="destructive">
          {gone
            ? 'La invitación no existe, ya se usó o ha caducado. Pide a la empresa que te envíe una nueva.'
            : errorMessage(preview.error)}
        </Alert>
      </AuthCard>
    )
  }

  const invitation = preview.data
  const here = encodeURIComponent(location.pathname + location.search)
  const sameEmail = me.data && me.data.email.toLowerCase() === invitation.email.toLowerCase()

  return (
    <AuthCard title={`Únete a ${invitation.tenantName}`}>
      <div className="grid gap-4 text-sm">
        <div className="flex items-start gap-3 rounded-lg border bg-muted/40 p-3">
          <Building2 className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden="true" />
          <p>
            <strong>{invitation.invitedBy}</strong> te invita a <strong>{invitation.tenantName}</strong> con
            el rol <strong>{invitation.roleName}</strong>. La invitación es para{' '}
            <strong className="break-all">{invitation.email}</strong> y caduca el{' '}
            {formatDate(invitation.expiresAt)}.
          </p>
        </div>

        {accept.isError && <Alert variant="destructive">{errorMessage(accept.error)}</Alert>}

        {!me.data && (
          <div className="grid gap-2">
            <Button asChild>
              <Link to={`/iniciar-sesion?next=${here}`}>Iniciar sesión para aceptar</Link>
            </Button>
            <Button asChild variant="outline">
              <Link to="/registro">Crear una cuenta con {invitation.email}</Link>
            </Button>
          </div>
        )}

        {me.data && !sameEmail && (
          <>
            <Alert variant="warning">
              Has iniciado sesión como <strong className="break-all">{me.data.email}</strong>. Para aceptar,
              entra con la cuenta de <strong className="break-all">{invitation.email}</strong>.
            </Alert>
            <Button
              variant="outline"
              disabled={logout.isPending}
              onClick={() =>
                logout.mutate(undefined, { onSuccess: () => navigate(`/iniciar-sesion?next=${here}`) })
              }
            >
              Cambiar de cuenta
            </Button>
          </>
        )}

        {me.data && sameEmail && (
          <Button
            disabled={accept.isPending}
            onClick={() =>
              accept.mutate(
                { tenantId, token },
                { onSuccess: (tenant) => navigate(`/c/${tenant.slug}`, { replace: true }) },
              )
            }
          >
            {accept.isPending ? 'Uniéndote…' : 'Aceptar invitación'}
          </Button>
        )}
      </div>
    </AuthCard>
  )
}
