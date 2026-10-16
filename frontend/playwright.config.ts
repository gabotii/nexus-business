import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests against a running stack: `vite preview` + the API (the default E2E_BASE_URL), or Docker Compose
 * (E2E_BASE_URL=http://localhost:8080). E2E_CHANNEL selects an installed browser (e.g. msedge).
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  // Each test drives a full journey against one shared API and database; one worker keeps runs deterministic.
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:4173',
    locale: 'es-ES',
    timezoneId: 'Europe/Madrid',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], channel: process.env.E2E_CHANNEL || undefined } },
    {
      name: 'mobile',
      use: { ...devices['Pixel 7'], channel: process.env.E2E_CHANNEL || undefined },
      grep: /@mobile/,
    },
  ],
})
