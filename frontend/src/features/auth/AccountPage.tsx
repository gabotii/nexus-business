import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { LogOut, Monitor, Smartphone } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import type { z } from 'zod'
import { PageHeader } from '@/components/page-header'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateTime, formatRelative } from '@/lib/format'
import { applyApiErrors } from '@/lib/forms'
import { errorMessage } from '@/services/http'
import {
  sessionsQuery,
  useChangePassword,
  useMe,
  useResendVerification,
  useRevokeOtherSessions,
  useRevokeSession,
  useUpdateProfile,
  type DeviceSession,
  type Me,
} from './api'
import { changePasswordSchema, PASSWORD_MIN, profileSchema } from './schemas'

export function AccountPage() {
  const me = useMe().data
  if (!me) return null
  return (
    <div className="mx-auto max-w-3xl space-y-8">
      <PageHeader
        title="Mi cuenta"
        description="Tus datos personales, tu contraseña y los dispositivos con sesión abierta."
      />
      {!me.emailVerified && <VerifyReminder email={me.email} />}
      <ProfileCard me={me} />
      <PasswordCard />
      <SessionsCard />
    </div>
  )
}

export function VerifyReminder({ email }: { email: string }) {
  const resend = useResendVerification()
  return (
    <Alert variant="warning">
      <p>
        Confirma tu correo <strong className="break-all">{email}</strong> para crear empresas y aceptar
        invitaciones.
      </p>
      {resend.isSuccess ? (
        <p className="text-muted-foreground">Te hemos enviado un nuevo enlace.</p>
      ) : (
        <Button
          variant="link"
          className="h-auto p-0"
          disabled={resend.isPending}
          onClick={() => resend.mutate()}
        >
          Reenviar el enlace de confirmación
        </Button>
      )}
    </Alert>
  )
}

function ProfileCard({ me }: { me: Me }) {
  const update = useUpdateProfile()
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof profileSchema>>({
    resolver: zodResolver(profileSchema),
    values: { displayName: me.displayName },
  })
  const { errors, isSubmitting, isDirty } = form.formState

  const onSubmit = form.handleSubmit(async ({ displayName }) => {
    setFormError(undefined)
    try {
      await update.mutateAsync(displayName)
      toast.success('Perfil actualizado.')
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['displayName']))
    }
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Perfil</CardTitle>
        <CardDescription>Tu nombre es visible para los miembros de tus empresas.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="grid gap-4 sm:max-w-sm">
          {formError && <Alert variant="destructive">{formError}</Alert>}
          <Field
            label="Correo electrónico"
            hint={me.emailVerified ? 'Confirmado.' : 'Pendiente de confirmar.'}
          >
            <Input value={me.email} readOnly disabled />
          </Field>
          <Field label="Nombre" error={errors.displayName?.message}>
            <Input autoComplete="name" {...form.register('displayName')} />
          </Field>
          <div>
            <Button type="submit" disabled={isSubmitting || !isDirty}>
              Guardar
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

function PasswordCard() {
  const change = useChangePassword()
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof changePasswordSchema>>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirm: '' },
  })
  const { errors, isSubmitting } = form.formState

  const onSubmit = form.handleSubmit(async ({ currentPassword, newPassword }) => {
    setFormError(undefined)
    try {
      await change.mutateAsync({ currentPassword, newPassword })
      form.reset()
      toast.success('Contraseña cambiada. Se han cerrado tus otras sesiones.')
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['currentPassword', 'newPassword']))
    }
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Contraseña</CardTitle>
        <CardDescription>Al cambiarla se cierran todas tus sesiones excepto esta.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="grid gap-4 sm:max-w-sm">
          {formError && <Alert variant="destructive">{formError}</Alert>}
          <Field label="Contraseña actual" error={errors.currentPassword?.message}>
            <Input type="password" autoComplete="current-password" {...form.register('currentPassword')} />
          </Field>
          <Field
            label="Nueva contraseña"
            error={errors.newPassword?.message}
            hint={`Al menos ${PASSWORD_MIN} caracteres.`}
          >
            <Input type="password" autoComplete="new-password" {...form.register('newPassword')} />
          </Field>
          <Field label="Repite la nueva contraseña" error={errors.confirm?.message}>
            <Input type="password" autoComplete="new-password" {...form.register('confirm')} />
          </Field>
          <div>
            <Button type="submit" disabled={isSubmitting}>
              Cambiar contraseña
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

/** "Chrome en Windows" from a user agent; good enough to recognise a device, never used for decisions. */
export function describeDevice(userAgent: string | null): { label: string; mobile: boolean } {
  if (!userAgent) return { label: 'Dispositivo desconocido', mobile: false }
  const browser = /Edg\//.test(userAgent)
    ? 'Edge'
    : /Firefox\//.test(userAgent)
      ? 'Firefox'
      : /Chrome\//.test(userAgent)
        ? 'Chrome'
        : /Safari\//.test(userAgent)
          ? 'Safari'
          : 'Navegador'
  const os = /Android/.test(userAgent)
    ? 'Android'
    : /iPhone|iPad/.test(userAgent)
      ? 'iOS'
      : /Windows/.test(userAgent)
        ? 'Windows'
        : /Mac OS X/.test(userAgent)
          ? 'macOS'
          : /Linux/.test(userAgent)
            ? 'Linux'
            : null
  return { label: os ? `${browser} en ${os}` : browser, mobile: /Mobile|Android|iPhone/.test(userAgent) }
}

function SessionsCard() {
  const sessions = useQuery(sessionsQuery())
  const revoke = useRevokeSession()
  const revokeOthers = useRevokeOtherSessions()
  const others = sessions.data?.filter((s) => !s.isCurrent).length ?? 0

  return (
    <Card>
      <CardHeader>
        <CardTitle>Sesiones abiertas</CardTitle>
        <CardDescription>
          Si no reconoces un dispositivo, cierra su sesión y cambia tu contraseña.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {sessions.isPending && <Skeleton className="h-16 w-full" />}
        {sessions.isError && <Alert variant="destructive">{errorMessage(sessions.error)}</Alert>}
        {sessions.data && (
          <ul className="divide-y rounded-lg border">
            {sessions.data.map((s) => (
              <SessionRow
                key={s.id}
                session={s}
                busy={revoke.isPending && revoke.variables === s.id}
                onRevoke={() =>
                  revoke.mutate(s.id, {
                    onSuccess: () => toast.success('Sesión cerrada.'),
                    onError: (e) => toast.error(errorMessage(e)),
                  })
                }
              />
            ))}
          </ul>
        )}
        {others > 0 && (
          <Button
            variant="outline"
            disabled={revokeOthers.isPending}
            onClick={() =>
              revokeOthers.mutate(undefined, {
                onSuccess: ({ revoked }) => toast.success(`${revoked} sesión(es) cerrada(s).`),
                onError: (e) => toast.error(errorMessage(e)),
              })
            }
          >
            <LogOut aria-hidden="true" />
            Cerrar las demás sesiones
          </Button>
        )}
      </CardContent>
    </Card>
  )
}

function SessionRow({
  session,
  busy,
  onRevoke,
}: {
  session: DeviceSession
  busy: boolean
  onRevoke: () => void
}) {
  const device = describeDevice(session.userAgent)
  const Icon = device.mobile ? Smartphone : Monitor
  return (
    <li className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex min-w-0 items-start gap-3">
        <Icon className="mt-0.5 size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
        <div className="min-w-0 space-y-0.5">
          <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
            {device.label}
            {session.isCurrent && <Badge variant="success">Este dispositivo</Badge>}
          </p>
          <p className="text-xs text-muted-foreground">
            Activa {formatRelative(session.lastSeenAt)} · Iniciada el {formatDateTime(session.createdAt)}
            {session.ipPrefix && ` · Red ${session.ipPrefix}`}
          </p>
        </div>
      </div>
      {!session.isCurrent && (
        <Button variant="outline" size="sm" disabled={busy} onClick={onRevoke}>
          Cerrar sesión
        </Button>
      )}
    </li>
  )
}
