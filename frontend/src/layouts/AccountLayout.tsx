import { Link, Outlet } from 'react-router'
import { BrandMark } from '@/components/brand-mark'
import { ThemeSwitcher } from '@/components/theme-switcher'
import { UserMenu } from './UserMenu'

/** Signed-in pages outside a company: company list, account settings, accepting an invitation. */
export function AccountLayout() {
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="border-b bg-card/80 backdrop-blur">
        <div className="mx-auto flex h-14 w-full max-w-5xl items-center justify-between gap-3 px-4 sm:px-6">
          <Link to="/empresas" className="rounded-md">
            <BrandMark />
          </Link>
          <div className="flex items-center gap-2">
            <ThemeSwitcher className="hidden sm:inline-flex" />
            <UserMenu />
          </div>
        </div>
      </header>
      <main id="main" className="mx-auto w-full max-w-5xl flex-1 px-4 py-8 sm:px-6 sm:py-12">
        <Outlet />
      </main>
    </div>
  )
}
