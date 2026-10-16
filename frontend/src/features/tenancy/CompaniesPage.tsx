import { zodResolver } from '@hookform/resolvers/zod'
import { useQuery } from '@tanstack/react-query'
import { Building2, ChevronRight, Plus } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { PageHeader } from '@/components/page-header'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/ui/field'
import { Input, NativeSelect } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useMe } from '@/features/auth/api'
import { VerifyReminder } from '@/features/auth/AccountPage'
import { FullPageSpinner } from '@/features/auth/guards'
import { applyApiErrors } from '@/lib/forms'
import { errorMessage } from '@/services/http'
import { myTenantsQuery, useCreateTenant } from './api'

/** Supported by the API (Currencies.cs); EUR is the default. */
const CURRENCIES = ['EUR', 'USD', 'GBP', 'CHF', 'MXN', 'ARS', 'COP', 'CLP', 'PEN', 'UYU', 'JPY']
const TIME_ZONES = [
  'Europe/Madrid',
  'Atlantic/Canary',
  'Europe/Lisbon',
  'Europe/London',
  'America/Mexico_City',
  'America/Bogota',
  'America/Lima',
  'America/Santiago',
  'America/Argentina/Buenos_Aires',
  'America/Montevideo',
  'America/New_York',
]

const createSchema = z.object({
  name: z
    .string()
    .trim()
    .min(2, 'El nombre debe tener entre 2 y 100 caracteres.')
    .max(100, 'El nombre debe tener entre 2 y 100 caracteres.'),
  currency: z.string(),
  timeZone: z.string(),
})

/** After signing in: straight into the only company, otherwise the company list. */
export function HomeRedirect() {
  const tenants = useQuery(myTenantsQuery())
  if (tenants.isPending) return <FullPageSpinner />
  if (tenants.data?.length === 1) return <Navigate to={`/c/${tenants.data[0]!.slug}`} replace />
  return <Navigate to="/empresas" replace />
}

export function CompaniesPage() {
  const me = useMe().data
  const tenants = useQuery(myTenantsQuery())
  const [params] = useSearchParams()
  const [creating, setCreating] = useState(params.has('crear'))
  const showForm = creating || tenants.data?.length === 0

  return (
    <div className="mx-auto max-w-2xl space-y-8">
      <PageHeader
        title="Tus empresas"
        description="Elige con qué empresa quieres trabajar o crea una nueva."
        actions={
          !showForm &&
          me?.emailVerified && (
            <Button onClick={() => setCreating(true)}>
              <Plus aria-hidden="true" />
              Crear empresa
            </Button>
          )
        }
      />

      {me && !me.emailVerified && <VerifyReminder email={me.email} />}

      {tenants.isPending && <Skeleton className="h-24 w-full" />}
      {tenants.isError && <Alert variant="destructive">{errorMessage(tenants.error)}</Alert>}
      {tenants.data && tenants.data.length > 0 && (
        <ul className="divide-y overflow-hidden rounded-xl border bg-card">
          {tenants.data.map((t) => (
            <li key={t.id}>
              <Link
                to={`/c/${t.slug}`}
                className="flex items-center gap-4 px-4 py-4 transition-colors hover:bg-accent focus-visible:bg-accent"
              >
                <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary-soft text-primary">
                  <Building2 className="size-5" aria-hidden="true" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{t.name}</span>
                  <span className="block text-sm text-muted-foreground">Tu rol: {t.role.name}</span>
                </span>
                <ChevronRight className="size-4 text-muted-foreground" aria-hidden="true" />
              </Link>
            </li>
          ))}
        </ul>
      )}

      {tenants.data?.length === 0 && !me?.emailVerified && (
        <p className="text-sm text-muted-foreground">
          Todavía no perteneces a ninguna empresa. Cuando confirmes tu correo podrás crear una, o aceptar la
          invitación que te envíe un compañero.
        </p>
      )}

      {showForm && me?.emailVerified && (
        <CreateCompanyCard onCancel={tenants.data?.length ? () => setCreating(false) : undefined} />
      )}
    </div>
  )
}

function CreateCompanyCard({ onCancel }: { onCancel?: () => void }) {
  const create = useCreateTenant()
  const navigate = useNavigate()
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof createSchema>>({
    resolver: zodResolver(createSchema),
    defaultValues: { name: '', currency: 'EUR', timeZone: 'Europe/Madrid' },
  })
  const { errors, isSubmitting } = form.formState

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(undefined)
    try {
      const tenant = await create.mutateAsync(values)
      navigate(`/c/${tenant.slug}`)
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['name', 'currency', 'timeZone']))
    }
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Nueva empresa</CardTitle>
        <CardDescription>Serás su propietario. Podrás invitar a tu equipo justo después.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={onSubmit} noValidate className="grid gap-4">
          {formError && <Alert variant="destructive">{formError}</Alert>}
          <Field label="Nombre de la empresa" error={errors.name?.message}>
            <Input autoComplete="organization" autoFocus {...form.register('name')} />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Moneda" hint="No podrá cambiarse cuando haya ventas.">
              <NativeSelect {...form.register('currency')}>
                {CURRENCIES.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </NativeSelect>
            </Field>
            <Field label="Zona horaria" hint="Para agrupar ventas e informes por día.">
              <NativeSelect {...form.register('timeZone')}>
                {TIME_ZONES.map((tz) => (
                  <option key={tz} value={tz}>
                    {tz.replaceAll('_', ' ')}
                  </option>
                ))}
              </NativeSelect>
            </Field>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Creando…' : 'Crear empresa'}
            </Button>
            {onCancel && (
              <Button variant="ghost" onClick={onCancel}>
                Cancelar
              </Button>
            )}
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

export function RoleBadge({ name, systemKey }: { name: string; systemKey: string | null }) {
  return <Badge variant={systemKey === 'owner' ? 'default' : 'secondary'}>{name}</Badge>
}
