import { Compass, TriangleAlert } from 'lucide-react'
import { useEffect } from 'react'
import { isRouteErrorResponse, Link, useRouteError } from 'react-router'
import { Button } from '@/components/ui/button'
import { PublicLayout } from '@/layouts/PublicLayout'
import { PRODUCT_NAME } from './brand'

function ErrorScreen({
  icon: Icon,
  title,
  description,
}: {
  icon: typeof Compass
  title: string
  description: string
}) {
  useEffect(() => {
    document.title = `${title} · ${PRODUCT_NAME}`
  }, [title])

  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 py-16 text-center">
      <span className="flex size-12 items-center justify-center rounded-full bg-muted text-muted-foreground">
        <Icon className="size-6" aria-hidden="true" />
      </span>
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <p className="text-muted-foreground">{description}</p>
      <Button asChild variant="outline">
        <Link to="/">Volver al inicio</Link>
      </Button>
    </div>
  )
}

export function NotFoundPage() {
  return (
    <ErrorScreen
      icon={Compass}
      title="Página no encontrada"
      description="La dirección no existe o ya no está disponible."
    />
  )
}

/** Router error boundary: a render error never leaves a blank screen, and internals are not shown to the user. */
export function RouteError() {
  const error = useRouteError()
  if (isRouteErrorResponse(error) && error.status === 404) {
    return (
      <PublicLayout>
        <NotFoundPage />
      </PublicLayout>
    )
  }

  if (import.meta.env.DEV) console.error(error)
  return (
    <PublicLayout>
      <ErrorScreen
        icon={TriangleAlert}
        title="Algo ha fallado"
        description="Se produjo un error inesperado al mostrar esta página. Recarga la página para intentarlo de nuevo."
      />
    </PublicLayout>
  )
}
