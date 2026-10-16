import { createBrowserRouter, type RouteObject } from 'react-router'
import { AccountPage } from '@/features/auth/AccountPage'
import {
  ForgotPasswordPage,
  LoginPage,
  RegisterPage,
  ResetPasswordPage,
  VerifyEmailPage,
} from '@/features/auth/AuthPages'
import { GuestOnly, RequireSession } from '@/features/auth/guards'
import { StatusPage } from '@/features/system/StatusPage'
import { CompaniesPage, HomeRedirect } from '@/features/tenancy/CompaniesPage'
import { InvitationPage } from '@/features/tenancy/InvitationPage'
import { RolesPage } from '@/features/tenancy/RolesPage'
import { TeamPage } from '@/features/tenancy/TeamPage'
import { WorkspaceHome } from '@/features/tenancy/WorkspaceHome'
import { AccountLayout } from '@/layouts/AccountLayout'
import { PublicLayout } from '@/layouts/PublicLayout'
import { WorkspaceLayout } from '@/layouts/WorkspaceLayout'
import { NotFoundPage, RouteError } from './ErrorPages'

/**
 * URLs are in Spanish (the v1 UI language). Companies are addressed by slug (`/c/:slug`); the API is always called
 * with the company id resolved from the caller's own memberships (ADR-0003).
 */
export const routes: RouteObject[] = [
  {
    errorElement: <RouteError />,
    children: [
      {
        element: <PublicLayout />,
        children: [
          {
            element: <GuestOnly />,
            children: [
              { path: 'iniciar-sesion', element: <LoginPage /> },
              { path: 'registro', element: <RegisterPage /> },
              { path: 'recuperar-contrasena', element: <ForgotPasswordPage /> },
            ],
          },
          // Reachable with or without a session: they come from e-mailed links.
          { path: 'verificar-correo', element: <VerifyEmailPage /> },
          { path: 'restablecer-contrasena', element: <ResetPasswordPage /> },
          { path: 'invitacion', element: <InvitationPage /> },
          { path: 'status', element: <StatusPage /> },
        ],
      },
      {
        element: <RequireSession />,
        children: [
          { index: true, element: <HomeRedirect /> },
          {
            element: <AccountLayout />,
            children: [
              { path: 'empresas', element: <CompaniesPage /> },
              { path: 'cuenta', element: <AccountPage /> },
            ],
          },
          {
            path: 'c/:slug',
            element: <WorkspaceLayout />,
            children: [
              { index: true, element: <WorkspaceHome /> },
              { path: 'equipo', element: <TeamPage /> },
              { path: 'roles', element: <RolesPage /> },
              { path: '*', element: <NotFoundPage /> },
            ],
          },
        ],
      },
      {
        element: <PublicLayout />,
        children: [{ path: '*', element: <NotFoundPage /> }],
      },
    ],
  },
]

export const createRouter = () => createBrowserRouter(routes)
