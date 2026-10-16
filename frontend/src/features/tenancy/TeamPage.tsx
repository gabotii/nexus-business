import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { MailPlus, RotateCw, X } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { PageHeader } from '@/components/page-header'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field } from '@/components/ui/field'
import { Input, NativeSelect } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDate } from '@/lib/format'
import { applyApiErrors } from '@/lib/forms'
import { errorMessage } from '@/services/http'
import {
  invitationsQuery,
  membersQuery,
  rolesQuery,
  useChangeMemberRole,
  useCreateInvitation,
  useResendInvitation,
  useRevokeInvitation,
  useSetMemberEnabled,
  type Member,
  type Role,
} from './api'
import { RoleBadge } from './CompaniesPage'
import { OWNER_ROLE } from './permissions'
import { useWorkspace } from './workspace-context'

/**
 * Roles the caller may hand out, mirroring the API's anti-escalation rule (only roles whose permissions they hold
 * themselves; Owner only by an Owner). The API re-checks every change.
 */
export function grantableRoles(roles: Role[], myPermissions: string[], iAmOwner: boolean): Role[] {
  return roles.filter((r) =>
    r.systemKey === OWNER_ROLE ? iAmOwner : r.permissions.every((p) => myPermissions.includes(p)),
  )
}

export function TeamPage() {
  const { workspace, can } = useWorkspace()
  const members = useQuery(membersQuery(workspace.id))
  const roles = useQuery(rolesQuery(workspace.id))
  const canManage = can('members.manage')
  const [inviting, setInviting] = useState(false)
  const assignable = roles.data
    ? grantableRoles(roles.data, workspace.permissions, workspace.role.systemKey === OWNER_ROLE)
    : []

  return (
    <div className="space-y-8">
      <PageHeader
        title="Equipo"
        documentTitle={`Equipo · ${workspace.name}`}
        description="Personas con acceso a esta empresa y su rol."
        actions={
          canManage && (
            <Button onClick={() => setInviting(true)} disabled={!roles.data}>
              <MailPlus aria-hidden="true" />
              Invitar
            </Button>
          )
        }
      />

      {members.isPending && <Skeleton className="h-40 w-full" />}
      {members.isError && <Alert variant="destructive">{errorMessage(members.error)}</Alert>}
      {members.data && <MembersTable members={members.data} assignable={assignable} canManage={canManage} />}

      {canManage && <PendingInvitations />}

      {inviting && <InviteDialog roles={assignable} onClose={() => setInviting(false)} />}
    </div>
  )
}

function MembersTable({
  members,
  assignable,
  canManage,
}: {
  members: Member[]
  assignable: Role[]
  canManage: boolean
}) {
  const { workspace } = useWorkspace()
  const changeRole = useChangeMemberRole(workspace.id)
  const setEnabled = useSetMemberEnabled(workspace.id)
  const assignableIds = new Set(assignable.map((r) => r.id))

  const onRoleChange = (member: Member, roleId: string) =>
    changeRole.mutate(
      { membershipId: member.id, roleId },
      {
        onSuccess: () => toast.success(`Rol de ${member.displayName} actualizado.`),
        onError: (e) => toast.error(errorMessage(e)),
      },
    )

  const onToggle = (member: Member) =>
    setEnabled.mutate(
      { membershipId: member.id, enabled: member.status === 'disabled' },
      {
        onSuccess: () =>
          toast.success(
            member.status === 'disabled'
              ? `${member.displayName} vuelve a tener acceso.`
              : `${member.displayName} ya no tiene acceso.`,
          ),
        onError: (e) => toast.error(errorMessage(e)),
      },
    )

  return (
    <div className="overflow-x-auto rounded-xl border bg-card">
      <table className="w-full min-w-[40rem] text-sm">
        <caption className="sr-only">Miembros de la empresa</caption>
        <thead className="border-b bg-muted/50 text-left text-xs text-muted-foreground">
          <tr>
            <th scope="col" className="px-4 py-2.5 font-medium">
              Persona
            </th>
            <th scope="col" className="px-4 py-2.5 font-medium">
              Rol
            </th>
            <th scope="col" className="px-4 py-2.5 font-medium">
              Desde
            </th>
            <th scope="col" className="px-4 py-2.5 font-medium">
              Estado
            </th>
            {canManage && (
              <th scope="col" className="px-4 py-2.5">
                <span className="sr-only">Acciones</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody className="divide-y">
          {members.map((m) => {
            // Mirrors the API: not yourself, and only members whose role you could grant.
            const manageable = canManage && !m.isYou && assignableIds.has(m.role.id)
            return (
              <tr key={m.id} className={m.status === 'disabled' ? 'text-muted-foreground' : undefined}>
                <td className="px-4 py-3">
                  <div className="font-medium text-foreground">
                    {m.displayName}{' '}
                    {m.isYou && <span className="text-xs font-normal text-muted-foreground">(tú)</span>}
                  </div>
                  <div className="text-xs break-all text-muted-foreground">{m.email}</div>
                </td>
                <td className="px-4 py-3">
                  {manageable && m.status === 'active' ? (
                    <NativeSelect
                      aria-label={`Rol de ${m.displayName}`}
                      className="h-8 w-auto"
                      value={m.role.id}
                      disabled={changeRole.isPending}
                      onChange={(e) => onRoleChange(m, e.target.value)}
                    >
                      {assignable.map((r) => (
                        <option key={r.id} value={r.id}>
                          {r.name}
                        </option>
                      ))}
                    </NativeSelect>
                  ) : (
                    <RoleBadge name={m.role.name} systemKey={m.role.systemKey} />
                  )}
                </td>
                <td className="px-4 py-3 tabular whitespace-nowrap">{formatDate(m.joinedAt)}</td>
                <td className="px-4 py-3">
                  {m.status === 'active' ? (
                    <Badge variant="success">Activo</Badge>
                  ) : (
                    <Badge variant="secondary">Sin acceso</Badge>
                  )}
                </td>
                {canManage && (
                  <td className="px-4 py-3 text-right">
                    {manageable && (
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={setEnabled.isPending}
                        onClick={() => onToggle(m)}
                      >
                        {m.status === 'active' ? 'Quitar acceso' : 'Devolver acceso'}
                      </Button>
                    )}
                  </td>
                )}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function PendingInvitations() {
  const { workspace } = useWorkspace()
  const invitations = useQuery(invitationsQuery(workspace.id))
  const resend = useResendInvitation(workspace.id)
  const revoke = useRevokeInvitation(workspace.id)

  if (invitations.isPending) return <Skeleton className="h-20 w-full" />
  if (invitations.isError) return <Alert variant="destructive">{errorMessage(invitations.error)}</Alert>
  if (invitations.data.length === 0) return null

  return (
    <Card>
      <CardHeader>
        <CardTitle>Invitaciones pendientes</CardTitle>
        <CardDescription>
          Los enlaces caducan a los 7 días. Reenviar genera un enlace nuevo y anula el anterior.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <ul className="divide-y rounded-lg border">
          {invitations.data.map((inv) => (
            <li
              key={inv.id}
              className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
            >
              <div className="min-w-0">
                <p className="text-sm font-medium break-all">{inv.email}</p>
                <p className="text-xs text-muted-foreground">
                  {inv.role.name} · invitada por {inv.invitedBy} ·{' '}
                  {inv.state === 'expired' ? (
                    <span className="text-warning">caducada</span>
                  ) : (
                    `caduca el ${formatDate(inv.expiresAt)}`
                  )}
                </p>
              </div>
              <div className="flex shrink-0 gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={resend.isPending}
                  onClick={() =>
                    resend.mutate(inv.id, {
                      onSuccess: () => toast.success(`Invitación reenviada a ${inv.email}.`),
                      onError: (e) => toast.error(errorMessage(e)),
                    })
                  }
                >
                  <RotateCw aria-hidden="true" />
                  Reenviar
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  aria-label={`Anular la invitación de ${inv.email}`}
                  disabled={revoke.isPending}
                  onClick={() =>
                    revoke.mutate(inv.id, {
                      onSuccess: () => toast.success('Invitación anulada.'),
                      onError: (e) => toast.error(errorMessage(e)),
                    })
                  }
                >
                  <X aria-hidden="true" />
                  Anular
                </Button>
              </div>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}

const inviteSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, 'Indica el correo electrónico.')
    .email('El correo electrónico no es válido.'),
  roleId: z.string().min(1, 'Elige un rol.'),
})

function InviteDialog({ roles, onClose }: { roles: Role[]; onClose: () => void }) {
  const { workspace } = useWorkspace()
  const invite = useCreateInvitation(workspace.id)
  const [formError, setFormError] = useState<string>()
  const defaultRole =
    roles.find((r) => r.systemKey === 'employee') ?? roles.find((r) => r.systemKey !== OWNER_ROLE)
  const form = useForm<z.infer<typeof inviteSchema>>({
    resolver: zodResolver(inviteSchema),
    defaultValues: { email: '', roleId: defaultRole?.id ?? '' },
  })
  const { errors, isSubmitting } = form.formState

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(undefined)
    try {
      await invite.mutateAsync(values)
      toast.success(`Invitación enviada a ${values.email}.`)
      onClose()
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['email', 'roleId']))
    }
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Invitar a {workspace.name}</DialogTitle>
          <DialogDescription>
            Le enviaremos un enlace por correo. Solo funcionará con esa dirección.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} noValidate className="grid gap-4">
          {formError && <Alert variant="destructive">{formError}</Alert>}
          <Field label="Correo electrónico" error={errors.email?.message}>
            <Input type="email" autoComplete="off" autoFocus {...form.register('email')} />
          </Field>
          <Field label="Rol" error={errors.roleId?.message}>
            <NativeSelect {...form.register('roleId')}>
              {roles.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <DialogFooter>
            <Button variant="outline" onClick={onClose}>
              Cancelar
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Enviando…' : 'Enviar invitación'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
