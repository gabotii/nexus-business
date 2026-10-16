import type { ReactNode } from 'react'
import { usePageTitle } from '@/components/page-header'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

/** The frame shared by sign-in, sign-up and recovery screens. */
export function AuthCard({
  title,
  description,
  children,
  footer,
}: {
  title: string
  description?: ReactNode
  children: ReactNode
  footer?: ReactNode
}) {
  usePageTitle(title)
  return (
    <div className="mx-auto w-full max-w-sm space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>
            <span className="text-xl tracking-tight">{title}</span>
          </CardTitle>
          {description && <CardDescription>{description}</CardDescription>}
        </CardHeader>
        <CardContent>{children}</CardContent>
      </Card>
      {footer && <div className="text-center text-sm text-muted-foreground">{footer}</div>}
    </div>
  )
}
