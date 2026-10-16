import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, ChevronsUpDown, House, Plus, ShieldCheck, Users } from 'lucide-react'
import { useEffect, useMemo } from 'react'
import { NavLink, Outlet, useNavigate, useParams } from 'react-router'
import { BrandMark } from '@/components/brand-mark'
import { ThemeSwitcher } from '@/components/theme-switcher'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { NotFoundPage } from '@/app/ErrorPages'
import { FullPageSpinner } from '@/features/auth/guards'
import { myTenantsQuery, tenantKeys, workspaceQuery, type MyTenant } from '@/features/tenancy/api'
import { WorkspaceContext } from '@/features/tenancy/workspace-context'
import { cn } from '@/lib/utils'
import { ApiError, errorMessage } from '@/services/http'
import { UserMenu } from './UserMenu'

interface NavItem {
  to: string
  label: string
  icon: typeof House
  end?: boolean
  permission?: string
}

const navItems: NavItem[] = [
  { to: '', label: 'Inicio', icon: House, end: true },
  { to: 'equipo', label: 'Equipo', icon: Users, permission: 'members.view' },
  { to: 'roles', label: 'Roles y permisos', icon: ShieldCheck, permission: 'members.view' },
]

/**
 * Shell of an opened company (`/c/:slug`). The slug is resolved through the caller's own memberships; a company
 * they do not belong to is simply "not found", as the API answers too.
 */
export function WorkspaceLayout() {
  const { slug = '' } = useParams()
  const tenants = useQuery(myTenantsQuery())
  const tenant = tenants.data?.find((t) => t.slug === slug)
  const workspace = useQuery({ ...workspaceQuery(tenant?.id ?? ''), enabled: Boolean(tenant) })
  const queryClient = useQueryClient()

  // Opening a company drops cached data of any other company (R11), so nothing can render from the wrong one.
  useEffect(() => {
    if (!tenant) return
    queryClient.removeQueries({
      queryKey: tenantKeys.all,
      predicate: (query) => query.queryKey[1] !== tenant.id,
    })
  }, [tenant, queryClient])

  const context = useMemo(
    () =>
      workspace.data
        ? { workspace: workspace.data, can: (p: string) => workspace.data.permissions.includes(p) }
        : null,
    [workspace.data],
  )

  if (tenants.isPending || (tenant && workspace.isPending)) return <FullPageSpinner />
  if (tenants.isError) return <ErrorState error={tenants.error} />
  if (!tenant || (workspace.error instanceof ApiError && workspace.error.status === 404))
    return <NotFoundPage />
  if (workspace.isError || !context) return <ErrorState error={workspace.error} />

  const items = navItems.filter((item) => !item.permission || context.can(item.permission))

  return (
    <WorkspaceContext.Provider value={context}>
      <div className="flex min-h-dvh">
        <aside className="hidden w-64 shrink-0 flex-col bg-sidebar text-sidebar-foreground md:flex">
          <div className="flex h-14 items-center border-b border-sidebar-border px-4">
            <BrandMark inverted className="text-sidebar-accent-foreground" />
          </div>
          <nav aria-label="Secciones de la empresa" className="flex-1 space-y-1 p-3">
            {items.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  cn(
                    'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
                    isActive && 'bg-sidebar-accent text-sidebar-accent-foreground',
                  )
                }
              >
                <item.icon className="size-4" aria-hidden="true" />
                {item.label}
              </NavLink>
            ))}
          </nav>
          <div className="border-t border-sidebar-border p-3">
            <ThemeSwitcher className="border-sidebar-border bg-sidebar-accent" />
          </div>
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="border-b bg-card/80 backdrop-blur">
            <div className="flex h-14 items-center justify-between gap-3 px-4 sm:px-6">
              <CompanySwitcher current={tenant} all={tenants.data} />
              <UserMenu />
            </div>
            <nav
              aria-label="Secciones de la empresa"
              className="flex gap-1 overflow-x-auto px-2 pb-2 md:hidden"
            >
              {items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.end}
                  className={({ isActive }) =>
                    cn(
                      'shrink-0 rounded-md px-3 py-1.5 text-sm font-medium text-muted-foreground',
                      isActive && 'bg-secondary text-foreground',
                    )
                  }
                >
                  {item.label}
                </NavLink>
              ))}
            </nav>
          </header>
          <main id="main" className="w-full max-w-6xl flex-1 px-4 py-6 sm:px-6 sm:py-8">
            <Outlet />
          </main>
        </div>
      </div>
    </WorkspaceContext.Provider>
  )
}

function CompanySwitcher({ current, all }: { current: MyTenant; all: MyTenant[] }) {
  const navigate = useNavigate()
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          className="min-w-0 gap-2 px-2"
          aria-label={`Empresa actual: ${current.name}. Cambiar de empresa`}
        >
          <span className="truncate font-semibold">{current.name}</span>
          <ChevronsUpDown className="text-muted-foreground" aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="w-64">
        <DropdownMenuLabel>Tus empresas</DropdownMenuLabel>
        {all.map((t) => (
          <DropdownMenuItem key={t.id} onSelect={() => navigate(`/c/${t.slug}`)}>
            <span className="min-w-0 flex-1 truncate">{t.name}</span>
            {t.id === current.id && <Check aria-label="Actual" />}
          </DropdownMenuItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => navigate('/empresas?crear=1')}>
          <Plus aria-hidden="true" />
          Crear otra empresa
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function ErrorState({ error }: { error: unknown }) {
  return (
    <div className="mx-auto max-w-md py-16">
      <Alert variant="destructive">{errorMessage(error)}</Alert>
    </div>
  )
}
