import { z } from 'zod'

/** Mirrors the API rules (PasswordRules): length is the only requirement (NIST SP 800-63B). */
export const PASSWORD_MIN = 10
export const PASSWORD_MAX = 128

const email = z
  .string()
  .trim()
  .min(1, 'Indica tu correo electrónico.')
  .max(254, 'El correo electrónico es demasiado largo.')
  .email('El correo electrónico no es válido.')

const newPassword = z
  .string()
  .min(PASSWORD_MIN, `La contraseña debe tener al menos ${PASSWORD_MIN} caracteres.`)
  .max(PASSWORD_MAX, `La contraseña admite como máximo ${PASSWORD_MAX} caracteres.`)

export const displayName = z
  .string()
  .trim()
  .min(2, 'El nombre debe tener entre 2 y 100 caracteres.')
  .max(100, 'El nombre debe tener entre 2 y 100 caracteres.')

export const loginSchema = z.object({
  email,
  password: z.string().min(1, 'Indica tu contraseña.'),
  rememberMe: z.boolean(),
})

export const registerSchema = z.object({ displayName, email, password: newPassword })

export const forgotSchema = z.object({ email })

export const resetSchema = z
  .object({ newPassword, confirm: z.string() })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'Las contraseñas no coinciden.' })

export const changePasswordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Indica tu contraseña actual.'),
    newPassword,
    confirm: z.string(),
  })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'Las contraseñas no coinciden.' })
  .refine((v) => v.newPassword !== v.currentPassword, {
    path: ['newPassword'],
    message: 'La nueva contraseña debe ser distinta de la actual.',
  })

export const profileSchema = z.object({ displayName })
