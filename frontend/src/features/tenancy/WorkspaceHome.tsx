import { ArrowRight, ShieldCheck, Users } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { PageHeader } from '@/components/page-header'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { errorMessage } from '@/services/http'
import { useLeaveTenant } from './api'
import { permissionLabel } from './permissions'
import { useWorkspace } from './workspace-context'

/** Landing page of a company. Business modules (customers, catalog, sales…) arrive from Phase 3 on. */
export function WorkspaceHome() {
  const { workspace, can } = useWorkspace()
  return (
    <div className="space-y-8">
      <PageHeader
        title={workspace.name}
        documentTitle={`Inicio · ${workspace.name}`}
        description={`Tu rol: ${workspace.role.name} · Moneda ${workspace.currency} · ${workspace.timeZone.replaceAll('_', ' ')}`}
      />

      <div className="grid gap-4 md:grid-cols-2">
        {can('members.view') && (
          <ShortcutCard
            to="equipo"
            icon={Users}
            title="Equipo"
            description={
              can('members.manage')
                ? 'Invita a tu equipo y gestiona sus accesos.'
                : 'Consulta quién forma parte de la empresa.'
            }
          />
        )}
        {can('members.view') && (
          <ShortcutCard
            to="roles"
            icon={ShieldCheck}
            title="Roles y permisos"
            description="Qué puede hacer cada rol dentro de la empresa."
          />
        )}
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Tus permisos en esta empresa</CardTitle>
          <CardDescription>
            Los concede tu rol. Si necesitas otro acceso, pídeselo a un administrador.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ul className="flex flex-wrap gap-2">
            {workspace.permissions.map((p) => (
              <li key={p} className="rounded-md bg-secondary px-2 py-1 text-xs">
                {permissionLabel(p)}
              </li>
            ))}
          </ul>
        </CardContent>
      </Card>

      <LeaveCompany />
    </div>
  )
}

function ShortcutCard({
  to,
  icon: Icon,
  title,
  description,
}: {
  to: string
  icon: typeof Users
  title: string
  description: string
}) {
  return (
    <Link to={to} className="group rounded-xl focus-visible:outline-2">
      <Card className="h-full transition-colors group-hover:border-primary/40">
        <CardHeader>
          <CardTitle>
            <span className="flex items-center gap-2">
              <Icon className="size-4 text-primary" aria-hidden="true" />
              {title}
            </span>
          </CardTitle>
          <CardDescription>{description}</CardDescription>
        </CardHeader>
        <CardContent>
          <span className="inline-flex items-center gap-1 text-sm font-medium text-primary">
            Abrir <ArrowRight className="size-4" aria-hidden="true" />
          </span>
        </CardContent>
      </Card>
    </Link>
  )
}

function LeaveCompany() {
  const { workspace } = useWorkspace()
  const leave = useLeaveTenant(workspace.id)
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)

  return (
    <div className="border-t pt-6">
      <Dialog open={open} onOpenChange={setOpen}>
        <Button
          variant="ghost"
          className="text-destructive hover:text-destructive"
          onClick={() => setOpen(true)}
        >
          Abandonar esta empresa
        </Button>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>¿Abandonar {workspace.name}?</DialogTitle>
            <DialogDescription>
              Perderás el acceso a sus datos. Para volver necesitarás una nueva invitación.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <DialogClose asChild>
              <Button variant="outline">Cancelar</Button>
            </DialogClose>
            <Button
              variant="destructive"
              disabled={leave.isPending}
              onClick={() =>
                leave.mutate(undefined, {
                  onSuccess: () => {
                    toast.success(`Has abandonado ${workspace.name}.`)
                    navigate('/empresas', { replace: true })
                  },
                  onError: (error) => {
                    setOpen(false)
                    toast.error(errorMessage(error))
                  },
                })
              }
            >
              Abandonar
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
