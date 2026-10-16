import type { ReactNode } from 'react'
import { Link, Outlet } from 'react-router'
import { BrandMark } from '@/components/brand-mark'
import { ThemeSwitcher } from '@/components/theme-switcher'

/** Frame for pages outside a company workspace (sign-in, sign-up, e-mailed links, service status). */
export function PublicLayout({ children }: { children?: ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="border-b bg-card/80 backdrop-blur">
        <div className="mx-auto flex h-14 w-full max-w-5xl items-center justify-between px-4 sm:px-6">
          <BrandMark />
          <ThemeSwitcher />
        </div>
      </header>
      <main id="main" className="mx-auto w-full max-w-5xl flex-1 px-4 py-10 sm:px-6 sm:py-14">
        {children ?? <Outlet />}
      </main>
      <footer className="border-t">
        <p className="mx-auto w-full max-w-5xl px-4 py-5 text-xs text-muted-foreground sm:px-6">
          Plataforma de gestión empresarial multiempresa ·{' '}
          <Link to="/status" className="hover:underline">
            Estado del servicio
          </Link>
        </p>
      </footer>
    </div>
  )
}
