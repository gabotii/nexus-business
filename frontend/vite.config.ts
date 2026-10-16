/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The dev server proxies /api and /health to the ASP.NET Core API so the browser sees a single origin: the session
// cookie can stay SameSite=Strict and no CORS is needed (ADR-0002), exactly like nginx in production.
const apiTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://127.0.0.1:5100'
const proxy = {
  '/api': { target: apiTarget, changeOrigin: false },
  '/health': { target: apiTarget, changeOrigin: false },
}

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: { port: 5173, strictPort: true, proxy },
  // `vite preview` serves the production build with the same proxy (used by the end-to-end tests).
  preview: { port: 4173, strictPort: true, proxy },
  test: {
    globals: true,
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
  },
})
