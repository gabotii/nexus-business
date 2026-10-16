import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router/dom'
import { PRODUCT_NAME } from './app/brand'
import { AppProviders, createQueryClient } from './app/providers'
import { createRouter } from './app/router'
import './index.css'

document.title = PRODUCT_NAME

const queryClient = createQueryClient()
const router = createRouter()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders client={queryClient}>
      <RouterProvider router={router} />
    </AppProviders>
  </StrictMode>,
)
