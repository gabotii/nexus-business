import { useEffect, type ReactNode } from 'react'
import { PRODUCT_NAME } from '@/app/brand'

/** Page title (also the document title) with an optional description and actions. */
export function PageHeader({
  title,
  description,
  actions,
  documentTitle,
}: {
  title: string
  description?: ReactNode
  actions?: ReactNode
  /** Defaults to the title; the product name is appended. */
  documentTitle?: string
}) {
  usePageTitle(documentTitle ?? title)
  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0 space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

export function usePageTitle(title: string) {
  useEffect(() => {
    document.title = `${title} · ${PRODUCT_NAME}`
  }, [title])
}
