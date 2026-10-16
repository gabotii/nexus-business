import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { Lock, Pencil, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { PageHeader } from '@/components/page-header'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { applyApiErrors } from '@/lib/forms'
import { errorMessage } from '@/services/http'
import { rolesQuery, useCreateRole, useDeleteRole, useUpdateRole, type Role } from './api'
import { PERMISSION_GROUPS, permissionLabel } from './permissions'
import { useWorkspace } from './workspace-context'

const roleSchema = z.object({
  name: z
    .string()
    .trim()
    .min(2, 'El nombre debe tener entre 2 y 60 caracteres.')
    .max(60, 'El nombre debe tener entre 2 y 60 caracteres.'),
  description: z.string().trim().max(200, 'La descripción admite como máximo 200 caracteres.'),
  permissions: z.array(z.string()),
})

export function RolesPage() {
  const { workspace, can } = useWorkspace()
  const roles = useQuery(rolesQuery(workspace.id))
  const canManage = can('roles.manage')
  const [editing, setEditing] = useState<Role | 'new' | null>(null)

  return (
    <div className="space-y-8">
      <PageHeader
        title="Roles y permisos"
        documentTitle={`Roles · ${workspace.name}`}
        description="Cada miembro tiene un rol; el rol decide qué puede ver y hacer en la empresa."
        actions={
          canManage && (
            <Button onClick={() => setEditing('new')}>
              <Plus aria-hidden="true" />
              Nuevo rol
            </Button>
          )
        }
      />
      {roles.isPending && <Skeleton className="h-48 w-full" />}
      {roles.isError && <Alert variant="destructive">{errorMessage(roles.error)}</Alert>}
      <div className="grid gap-4 lg:grid-cols-2">
        {roles.data?.map((role) => (
          <RoleCard key={role.id} role={role} canManage={canManage} onEdit={() => setEditing(role)} />
        ))}
      </div>
      {editing && <RoleDialog role={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}

function RoleCard({ role, canManage, onEdit }: { role: Role; canManage: boolean; onEdit: () => void }) {
  const { workspace } = useWorkspace()
  const remove = useDeleteRole(workspace.id)
  const isMine = role.id === workspace.role.id
  // Mirrors the API: you cannot edit the role you hold, nor roles granting permissions you lack.
  const withinMine = role.permissions.every((p) => workspace.permissions.includes(p))
  const editable = canManage && role.editable && !isMine && withinMine

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <span className="flex flex-wrap items-center gap-2">
            {role.name}
            {role.systemKey ? (
              <Badge variant="secondary">Predefinido</Badge>
            ) : (
              <Badge variant="outline">Personalizado</Badge>
            )}
            {isMine && <Badge>Tu rol</Badge>}
          </span>
        </CardTitle>
        <CardDescription>
          {role.description ?? 'Sin descripción.'} · {role.memberCount}{' '}
          {role.memberCount === 1 ? 'miembro' : 'miembros'}
        </CardDescription>
        {canManage && (
          <CardAction>
            {editable ? (
              <div className="flex gap-1">
                <Button variant="ghost" size="icon" aria-label={`Editar ${role.name}`} onClick={onEdit}>
                  <Pencil aria-hidden="true" />
                </Button>
                {role.deletable && (
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={`Eliminar ${role.name}`}
                    disabled={remove.isPending}
                    onClick={() =>
                      remove.mutate(role.id, {
                        onSuccess: () => toast.success(`Rol ${role.name} eliminado.`),
                        onError: (e) => toast.error(errorMessage(e)),
                      })
                    }
                  >
                    <Trash2 aria-hidden="true" />
                  </Button>
                )}
              </div>
            ) : (
              <Lock className="size-4 text-muted-foreground" aria-label="No editable por ti" />
            )}
          </CardAction>
        )}
      </CardHeader>
      <CardContent>
        {role.permissions.length === 0 ? (
          <p className="text-sm text-muted-foreground">Sin permisos.</p>
        ) : (
          <ul className="flex flex-wrap gap-1.5" aria-label={`Permisos de ${role.name}`}>
            {role.permissions.map((p) => (
              <li key={p} className="rounded-md bg-secondary px-2 py-0.5 text-xs">
                {permissionLabel(p)}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function RoleDialog({ role, onClose }: { role: Role | null; onClose: () => void }) {
  const { workspace } = useWorkspace()
  const create = useCreateRole(workspace.id)
  const update = useUpdateRole(workspace.id)
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof roleSchema>>({
    resolver: zodResolver(roleSchema),
    defaultValues: {
      name: role?.name ?? '',
      description: role?.description ?? '',
      permissions: role?.permissions ?? [],
    },
  })
  const { errors, isSubmitting } = form.formState

  const onSubmit = form.handleSubmit(async ({ name, description, permissions }) => {
    setFormError(undefined)
    const input = { name, description: description || null, permissions }
    try {
      if (role) await update.mutateAsync({ roleId: role.id, ...input })
      else await create.mutateAsync(input)
      toast.success(role ? 'Rol actualizado.' : 'Rol creado.')
      onClose()
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['name', 'description', 'permissions']))
    }
  })

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{role ? `Editar ${role.name}` : 'Nuevo rol'}</DialogTitle>
          <DialogDescription>Solo puedes conceder permisos que tú mismo tienes.</DialogDescription>
        </DialogHeader>
        <form onSubmit={onSubmit} noValidate className="grid gap-5">
          {formError && <Alert variant="destructive">{formError}</Alert>}
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Nombre" error={errors.name?.message}>
              <Input autoFocus {...form.register('name')} />
            </Field>
            <Field label="Descripción (opcional)" error={errors.description?.message}>
              <Input {...form.register('description')} />
            </Field>
          </div>
          <Controller
            control={form.control}
            name="permissions"
            render={({ field }) => (
              <fieldset className="grid gap-4 sm:grid-cols-2">
                <legend className="mb-2 text-sm font-medium">Permisos</legend>
                {PERMISSION_GROUPS.map((group) => (
                  <div key={group.label} className="space-y-2 rounded-lg border p-3">
                    <p className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                      {group.label}
                    </p>
                    {group.permissions.map((p) => {
                      const mine = workspace.permissions.includes(p.key)
                      const checked = field.value.includes(p.key)
                      return (
                        <label
                          key={p.key}
                          className={`flex items-center gap-2 text-sm ${mine ? '' : 'text-muted-foreground'}`}
                          title={mine ? undefined : 'No tienes este permiso, así que no puedes concederlo.'}
                        >
                          <input
                            type="checkbox"
                            className="size-4 accent-primary"
                            checked={checked}
                            disabled={!mine && !checked}
                            onChange={(e) =>
                              field.onChange(
                                e.target.checked
                                  ? [...field.value, p.key]
                                  : field.value.filter((v) => v !== p.key),
                              )
                            }
                          />
                          {p.label}
                        </label>
                      )
                    })}
                  </div>
                ))}
              </fieldset>
            )}
          />
          <DialogFooter>
            <Button variant="outline" onClick={onClose}>
              Cancelar
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando…' : 'Guardar'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
