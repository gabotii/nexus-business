import { PRODUCT_NAME } from '@/app/brand'
import { cn } from '@/lib/utils'

/** Logo glyph + configured product name. The glyph is decorative; the name carries the meaning. */
export function BrandMark({ className, inverted = false }: { className?: string; inverted?: boolean }) {
  return (
    <span className={cn('inline-flex items-center gap-2.5 font-semibold tracking-tight', className)}>
      <svg viewBox="0 0 32 32" className="size-8 shrink-0" aria-hidden="true">
        <rect width="32" height="32" rx="8" className={inverted ? 'fill-sidebar-primary' : 'fill-primary'} />
        <path
          d="M9 23V9h2.6l7.8 9.6V9H23v14h-2.6l-7.8-9.6V23z"
          className={inverted ? 'fill-sidebar-primary-foreground' : 'fill-primary-foreground'}
        />
      </svg>
      <span className="text-[15px]">{PRODUCT_NAME}</span>
    </span>
  )
}
