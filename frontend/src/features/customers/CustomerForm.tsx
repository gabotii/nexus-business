import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Input, NativeSelect } from '@/components/ui/input'
import { applyApiErrors } from '@/lib/forms'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/http'
import { tagColorClass, type Customer, type SaveCustomerInput, type Tag } from './api'

const optional = (max: number, label: string) =>
  z.string().trim().max(max, `${label} admite como máximo ${max} caracteres.`)

export const customerSchema = z.object({
  kind: z.enum(['person', 'company']),
  name: z
    .string()
    .trim()
    .min(2, 'El nombre debe tener entre 2 y 150 caracteres.')
    .max(150, 'El nombre debe tener entre 2 y 150 caracteres.'),
  legalName: optional(200, 'La razón social'),
  taxId: optional(32, 'El NIF/CIF'),
  email: z.union([z.literal(''), z.string().trim().email('El correo electrónico no es válido.').max(254)]),
  phone: z
    .string()
    .trim()
    .refine(
      (v) => v === '' || /^\+?[0-9 ().-]{6,40}$/.test(v),
      'El teléfono solo puede contener números, espacios, paréntesis y guiones.',
    ),
  line: optional(200, 'La dirección'),
  city: optional(100, 'La localidad'),
  postalCode: optional(20, 'El código postal'),
  region: optional(100, 'La provincia'),
  countryCode: z
    .string()
    .trim()
    .refine((v) => v === '' || /^[A-Za-z]{2}$/.test(v), 'Usa el código ISO de dos letras (p. ej. ES).'),
  tagIds: z.array(z.string()).max(20, 'Un cliente admite como máximo 20 etiquetas.'),
})

export type CustomerFormValues = z.infer<typeof customerSchema>

const blankToNull = (v: string) => (v.trim() === '' ? null : v.trim())

export function toInput(values: CustomerFormValues, version?: number): SaveCustomerInput {
  return {
    kind: values.kind,
    name: values.name,
    legalName: blankToNull(values.legalName),
    taxId: blankToNull(values.taxId),
    email: blankToNull(values.email),
    phone: blankToNull(values.phone),
    address: {
      line: blankToNull(values.line),
      city: blankToNull(values.city),
      postalCode: blankToNull(values.postalCode),
      region: blankToNull(values.region),
      countryCode: blankToNull(values.countryCode)?.toUpperCase() ?? null,
    },
    tagIds: values.tagIds,
    version,
  }
}

function defaults(customer?: Customer): CustomerFormValues {
  return {
    kind: customer?.kind ?? 'company',
    name: customer?.name ?? '',
    legalName: customer?.legalName ?? '',
    taxId: customer?.taxId ?? '',
    email: customer?.email ?? '',
    phone: customer?.phone ?? '',
    line: customer?.address.line ?? '',
    city: customer?.address.city ?? '',
    postalCode: customer?.address.postalCode ?? '',
    region: customer?.address.region ?? '',
    countryCode: customer?.address.countryCode ?? '',
    tagIds: customer?.tags.map((t) => t.id) ?? [],
  }
}

/** Fields the API may report errors on (camelCase JSON paths). */
const fieldPaths = ['kind', 'name', 'legalName', 'taxId', 'email', 'phone', 'tagIds'] as const

export function CustomerForm({
  customer,
  tags,
  slug,
  submitLabel,
  onSubmit,
  onCancel,
}: {
  customer?: Customer
  tags: Tag[]
  slug: string
  submitLabel: string
  onSubmit: (values: CustomerFormValues) => Promise<unknown>
  onCancel?: () => void
}) {
  const [formError, setFormError] = useState<string>()
  const [duplicateOf, setDuplicateOf] = useState<string>()
  const form = useForm<CustomerFormValues>({
    resolver: zodResolver(customerSchema),
    defaultValues: defaults(customer),
  })
  const { errors, isSubmitting } = form.formState
  const kind = form.watch('kind')

  const submit = form.handleSubmit(async (values) => {
    setFormError(undefined)
    setDuplicateOf(undefined)
    try {
      await onSubmit(values)
    } catch (error) {
      if (error instanceof ApiError && error.code === 'customer_duplicate') {
        const existing = error.details.existingCustomerId
        if (typeof existing === 'string') setDuplicateOf(existing)
      }
      setFormError(applyApiErrors(error, form.setError, fieldPaths))
      // Nested address errors arrive as "address.city" and so on.
      if (error instanceof ApiError) {
        for (const [key, messages] of Object.entries(error.fieldErrors)) {
          const nested = key.startsWith('address.') ? (key.slice(8) as keyof CustomerFormValues) : undefined
          if (nested && messages[0]) form.setError(nested, { type: 'server', message: messages[0] })
        }
      }
    }
  })

  return (
    <form onSubmit={submit} noValidate className="grid gap-5">
      {formError && (
        <Alert variant="destructive">
          <p>{formError}</p>
          {duplicateOf && (
            <Link
              to={`/c/${slug}/clientes/${duplicateOf}`}
              className="font-medium text-primary underline-offset-4 hover:underline"
            >
              Ver el cliente existente
            </Link>
          )}
        </Alert>
      )}

      <div className="grid gap-4 sm:grid-cols-[10rem_1fr]">
        <Field label="Tipo">
          <NativeSelect {...form.register('kind')}>
            <option value="company">Empresa</option>
            <option value="person">Particular</option>
          </NativeSelect>
        </Field>
        <Field
          label={kind === 'company' ? 'Nombre comercial' : 'Nombre y apellidos'}
          error={errors.name?.message}
        >
          <Input autoFocus autoComplete="off" {...form.register('name')} />
        </Field>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        {kind === 'company' && (
          <Field label="Razón social" error={errors.legalName?.message}>
            <Input autoComplete="off" {...form.register('legalName')} />
          </Field>
        )}
        <Field label={kind === 'company' ? 'CIF / NIF' : 'NIF / DNI'} error={errors.taxId?.message}>
          <Input autoComplete="off" {...form.register('taxId')} />
        </Field>
        <Field label="Correo electrónico" error={errors.email?.message}>
          <Input type="email" autoComplete="off" {...form.register('email')} />
        </Field>
        <Field label="Teléfono" error={errors.phone?.message}>
          <Input type="tel" autoComplete="off" {...form.register('phone')} />
        </Field>
      </div>

      <fieldset className="grid gap-4 sm:grid-cols-6">
        <legend className="mb-2 text-sm font-medium">Dirección</legend>
        <Field label="Calle y número" error={errors.line?.message} className="sm:col-span-6">
          <Input autoComplete="off" {...form.register('line')} />
        </Field>
        <Field label="Código postal" error={errors.postalCode?.message} className="sm:col-span-2">
          <Input autoComplete="off" {...form.register('postalCode')} />
        </Field>
        <Field label="Localidad" error={errors.city?.message} className="sm:col-span-4">
          <Input autoComplete="off" {...form.register('city')} />
        </Field>
        <Field label="Provincia" error={errors.region?.message} className="sm:col-span-4">
          <Input autoComplete="off" {...form.register('region')} />
        </Field>
        <Field label="País (ISO)" error={errors.countryCode?.message} className="sm:col-span-2">
          <Input autoComplete="off" maxLength={2} placeholder="ES" {...form.register('countryCode')} />
        </Field>
      </fieldset>

      {tags.length > 0 && (
        <Controller
          control={form.control}
          name="tagIds"
          render={({ field }) => (
            <fieldset>
              <legend className="mb-2 text-sm font-medium">Etiquetas</legend>
              <div className="flex flex-wrap gap-2">
                {tags.map((tag) => {
                  const checked = field.value.includes(tag.id)
                  return (
                    <label
                      key={tag.id}
                      className={cn(
                        'inline-flex cursor-pointer items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring',
                        checked ? tagColorClass[tag.color] : 'text-muted-foreground',
                      )}
                    >
                      <input
                        type="checkbox"
                        className="sr-only"
                        checked={checked}
                        onChange={(e) =>
                          field.onChange(
                            e.target.checked
                              ? [...field.value, tag.id]
                              : field.value.filter((id) => id !== tag.id),
                          )
                        }
                      />
                      {checked ? '✓ ' : ''}
                      {tag.name}
                    </label>
                  )
                })}
              </div>
              {errors.tagIds?.message && (
                <p className="mt-2 text-xs text-destructive">{errors.tagIds.message}</p>
              )}
            </fieldset>
          )}
        />
      )}

      <div className="flex flex-wrap justify-end gap-2">
        {onCancel && (
          <Button variant="outline" onClick={onCancel}>
            Cancelar
          </Button>
        )}
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Guardando…' : submitLabel}
        </Button>
      </div>
    </form>
  )
}

export function TagChips({ tags }: { tags: { id: string; name: string; color: string }[] }) {
  if (tags.length === 0) return null
  return (
    <ul className="flex flex-wrap gap-1" aria-label="Etiquetas">
      {tags.map((tag) => (
        <li
          key={tag.id}
          className={cn('rounded-full px-2 py-0.5 text-xs font-medium', tagColorClass[tag.color])}
        >
          {tag.name}
        </li>
      ))}
    </ul>
  )
}
