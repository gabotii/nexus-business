import { Building2, ChevronDown, LogOut, UserRound } from 'lucide-react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useLogout, useMe } from '@/features/auth/api'
import { errorMessage } from '@/services/http'

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  const letters =
    parts.length > 1 ? [parts[0]![0], parts[parts.length - 1]![0]] : [parts[0]?.[0], parts[0]?.[1]]
  return letters.filter(Boolean).join('').toUpperCase() || '?'
}

/** The signed-in person: account, companies and sign-out. */
export function UserMenu() {
  const me = useMe().data
  const logout = useLogout()
  const navigate = useNavigate()
  if (!me) return null

  const signOut = () =>
    logout.mutate(undefined, {
      onSuccess: () => navigate('/iniciar-sesion', { replace: true }),
      onError: (error) => toast.error(errorMessage(error)),
    })

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="gap-2 px-2" aria-label={`Menú de ${me.displayName}`}>
          <span
            aria-hidden="true"
            className="flex size-7 items-center justify-center rounded-full bg-primary-soft text-xs font-semibold text-primary"
          >
            {initials(me.displayName)}
          </span>
          <span className="hidden max-w-40 truncate sm:inline">{me.displayName}</span>
          <ChevronDown className="text-muted-foreground" aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>
          <span className="block truncate font-medium text-foreground">{me.displayName}</span>
          <span className="block truncate">{me.email}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => navigate('/cuenta')}>
          <UserRound aria-hidden="true" />
          Mi cuenta
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={() => navigate('/empresas')}>
          <Building2 aria-hidden="true" />
          Mis empresas
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={signOut} disabled={logout.isPending}>
          <LogOut aria-hidden="true" />
          Cerrar sesión
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
