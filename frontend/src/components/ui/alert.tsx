import { cva, type VariantProps } from 'class-variance-authority'
import { CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react'
import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

const alertVariants = cva(
  'flex gap-3 rounded-lg border px-4 py-3 text-sm [&>svg]:mt-0.5 [&>svg]:size-4 [&>svg]:shrink-0',
  {
    variants: {
      variant: {
        info: 'border-primary/20 bg-primary-soft text-foreground [&>svg]:text-primary',
        success: 'border-success/25 bg-success-soft text-foreground [&>svg]:text-success',
        warning: 'border-warning/25 bg-warning-soft text-foreground [&>svg]:text-warning',
        destructive: 'border-destructive/25 bg-destructive-soft text-foreground [&>svg]:text-destructive',
      },
    },
    defaultVariants: { variant: 'info' },
  },
)

const icons = { info: Info, success: CircleCheck, warning: TriangleAlert, destructive: CircleAlert }

/** Inline message. Errors use role="alert" so they are announced; the icon is decorative (the text carries meaning). */
export function Alert({
  className,
  variant = 'info',
  children,
  ...props
}: ComponentProps<'div'> & VariantProps<typeof alertVariants>) {
  const Icon = icons[variant ?? 'info']
  return (
    <div
      role={variant === 'destructive' ? 'alert' : 'status'}
      className={cn(alertVariants({ variant }), className)}
      {...props}
    >
      <Icon aria-hidden="true" />
      <div className="min-w-0 space-y-1">{children}</div>
    </div>
  )
}
