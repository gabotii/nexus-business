import { zodResolver } from '@hookform/resolvers/zod'
import { MailCheck } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { applyApiErrors, safeNextPath } from '@/lib/forms'
import { errorMessage } from '@/services/http'
import { useForgotPassword, useLogin, useMe, useRegister, useResetPassword, useVerifyEmail } from './api'
import { AuthCard } from './AuthCard'
import { forgotSchema, loginSchema, PASSWORD_MIN, registerSchema, resetSchema } from './schemas'
import type { z } from 'zod'

const passwordHint = `Al menos ${PASSWORD_MIN} caracteres. Una frase larga es más segura que una palabra compleja.`

export function LoginPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const login = useLogin()
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof loginSchema>>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '', rememberMe: false },
  })
  const { errors, isSubmitting } = form.formState

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(undefined)
    try {
      await login.mutateAsync(values)
      navigate(safeNextPath(params.get('next')), { replace: true })
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['email', 'password']))
    }
  })

  return (
    <AuthCard
      title="Iniciar sesión"
      description="Accede a las empresas de las que formas parte."
      footer={
        <>
          ¿No tienes cuenta?{' '}
          <Link to="/registro" className="font-medium text-primary hover:underline">
            Crea una
          </Link>
        </>
      }
    >
      <form onSubmit={onSubmit} noValidate className="grid gap-4">
        {formError && <Alert variant="destructive">{formError}</Alert>}
        <Field label="Correo electrónico" error={errors.email?.message}>
          <Input type="email" autoComplete="username" autoFocus {...form.register('email')} />
        </Field>
        <Field label="Contraseña" error={errors.password?.message}>
          <Input type="password" autoComplete="current-password" {...form.register('password')} />
        </Field>
        <div className="flex items-center justify-between gap-2 text-sm">
          <label className="inline-flex items-center gap-2">
            <input type="checkbox" className="size-4 accent-primary" {...form.register('rememberMe')} />
            Recordarme 14 días
          </label>
          <Link to="/recuperar-contrasena" className="text-primary hover:underline">
            ¿Olvidaste tu contraseña?
          </Link>
        </div>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Entrando…' : 'Entrar'}
        </Button>
      </form>
    </AuthCard>
  )
}

export function RegisterPage() {
  const register = useRegister()
  const [sentTo, setSentTo] = useState<string>()
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof registerSchema>>({
    resolver: zodResolver(registerSchema),
    defaultValues: { displayName: '', email: '', password: '' },
  })
  const { errors, isSubmitting } = form.formState

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(undefined)
    try {
      await register.mutateAsync(values)
      setSentTo(values.email)
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['displayName', 'email', 'password']))
    }
  })

  if (sentTo) {
    return (
      <AuthCard
        title="Revisa tu correo"
        footer={
          <Link to="/iniciar-sesion" className="text-primary hover:underline">
            Ir a iniciar sesión
          </Link>
        }
      >
        <div className="space-y-3 text-sm">
          <MailCheck className="size-8 text-primary" aria-hidden="true" />
          <p>
            Hemos enviado un enlace de confirmación a <strong className="break-all">{sentTo}</strong>. Ábrelo
            para activar tu cuenta; caduca en 24 horas.
          </p>
          <p className="text-muted-foreground">
            Si ya tenías una cuenta con ese correo, te hemos escrito con las instrucciones para acceder.
          </p>
        </div>
      </AuthCard>
    )
  }

  return (
    <AuthCard
      title="Crear una cuenta"
      description="Con tu cuenta podrás crear empresas o unirte a las que te inviten."
      footer={
        <>
          ¿Ya tienes cuenta?{' '}
          <Link to="/iniciar-sesion" className="font-medium text-primary hover:underline">
            Inicia sesión
          </Link>
        </>
      }
    >
      <form onSubmit={onSubmit} noValidate className="grid gap-4">
        {formError && <Alert variant="destructive">{formError}</Alert>}
        <Field label="Nombre" error={errors.displayName?.message}>
          <Input autoComplete="name" autoFocus {...form.register('displayName')} />
        </Field>
        <Field label="Correo electrónico" error={errors.email?.message}>
          <Input type="email" autoComplete="email" {...form.register('email')} />
        </Field>
        <Field label="Contraseña" error={errors.password?.message} hint={passwordHint}>
          <Input type="password" autoComplete="new-password" {...form.register('password')} />
        </Field>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Creando cuenta…' : 'Crear cuenta'}
        </Button>
      </form>
    </AuthCard>
  )
}

/** Opened from the e-mail link: confirms the address once, on arrival. */
export function VerifyEmailPage() {
  const [params] = useSearchParams()
  const verify = useVerifyEmail()
  const me = useMe()
  const started = useRef(false)
  const userId = params.get('uid') ?? ''
  const token = params.get('token') ?? ''

  useEffect(() => {
    // StrictMode mounts effects twice in development; the link must be submitted once.
    if (started.current || !userId || !token) return
    started.current = true
    verify.mutate({ userId, token })
  }, [userId, token, verify])

  const next = me.data ? { to: '/', label: 'Continuar' } : { to: '/iniciar-sesion', label: 'Iniciar sesión' }

  return (
    <AuthCard title="Confirmar correo electrónico">
      <div className="grid gap-4">
        {(!userId || !token) && (
          <Alert variant="destructive">El enlace no es válido. Comprueba que lo has copiado entero.</Alert>
        )}
        {verify.isPending && <p className="text-sm text-muted-foreground">Confirmando tu dirección…</p>}
        {verify.isSuccess && (
          <Alert variant="success">
            Tu correo está confirmado. Ya puedes crear empresas y aceptar invitaciones.
          </Alert>
        )}
        {verify.isError && <Alert variant="destructive">{errorMessage(verify.error)}</Alert>}
        {(verify.isSuccess || verify.isError) && (
          <Button asChild>
            <Link to={next.to}>{next.label}</Link>
          </Button>
        )}
      </div>
    </AuthCard>
  )
}

export function ForgotPasswordPage() {
  const forgot = useForgotPassword()
  const [sent, setSent] = useState(false)
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof forgotSchema>>({
    resolver: zodResolver(forgotSchema),
    defaultValues: { email: '' },
  })
  const { errors, isSubmitting } = form.formState

  const onSubmit = form.handleSubmit(async ({ email }) => {
    setFormError(undefined)
    try {
      await forgot.mutateAsync(email)
      setSent(true)
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['email']))
    }
  })

  return (
    <AuthCard
      title="Recuperar contraseña"
      description="Te enviaremos un enlace para elegir una nueva."
      footer={
        <Link to="/iniciar-sesion" className="text-primary hover:underline">
          Volver a iniciar sesión
        </Link>
      }
    >
      {sent ? (
        <Alert variant="success">
          Si existe una cuenta con ese correo, recibirás un enlace en unos minutos. Caduca en 1 hora.
        </Alert>
      ) : (
        <form onSubmit={onSubmit} noValidate className="grid gap-4">
          {formError && <Alert variant="destructive">{formError}</Alert>}
          <Field label="Correo electrónico" error={errors.email?.message}>
            <Input type="email" autoComplete="email" autoFocus {...form.register('email')} />
          </Field>
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Enviando…' : 'Enviar enlace'}
          </Button>
        </form>
      )}
    </AuthCard>
  )
}

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const reset = useResetPassword()
  const [done, setDone] = useState(false)
  const [formError, setFormError] = useState<string>()
  const form = useForm<z.infer<typeof resetSchema>>({
    resolver: zodResolver(resetSchema),
    defaultValues: { newPassword: '', confirm: '' },
  })
  const { errors, isSubmitting } = form.formState
  const userId = params.get('uid') ?? ''
  const token = params.get('token') ?? ''

  const onSubmit = form.handleSubmit(async ({ newPassword }) => {
    setFormError(undefined)
    try {
      await reset.mutateAsync({ userId, token, newPassword })
      setDone(true)
    } catch (error) {
      setFormError(applyApiErrors(error, form.setError, ['newPassword']))
    }
  })

  return (
    <AuthCard title="Elegir una nueva contraseña">
      {done ? (
        <div className="grid gap-4">
          <Alert variant="success">
            Contraseña actualizada. Por seguridad hemos cerrado todas tus sesiones abiertas.
          </Alert>
          <Button asChild>
            <Link to="/iniciar-sesion">Iniciar sesión</Link>
          </Button>
        </div>
      ) : !userId || !token ? (
        <Alert variant="destructive">
          El enlace no es válido. Solicita uno nuevo desde «¿Olvidaste tu contraseña?».
        </Alert>
      ) : (
        <form onSubmit={onSubmit} noValidate className="grid gap-4">
          {formError && <Alert variant="destructive">{formError}</Alert>}
          <Field label="Nueva contraseña" error={errors.newPassword?.message} hint={passwordHint}>
            <Input type="password" autoComplete="new-password" autoFocus {...form.register('newPassword')} />
          </Field>
          <Field label="Repite la contraseña" error={errors.confirm?.message}>
            <Input type="password" autoComplete="new-password" {...form.register('confirm')} />
          </Field>
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Guardando…' : 'Guardar contraseña'}
          </Button>
        </form>
      )}
    </AuthCard>
  )
}
