import { expect, test } from '@playwright/test'

test.describe('foundation smoke', () => {
  test('the status page reports the real API and database as operational @mobile', async ({
    page,
    request,
  }) => {
    // Ground truth straight from the API: the page must show what the backend reports, not placeholder data.
    const meta = await (await request.get('/api/v1/meta')).json()
    expect((await request.get('/health/ready')).status()).toBe(200)

    await page.goto('/status')

    await expect(page.getByRole('heading', { name: 'Estado del servicio' })).toBeVisible()
    await expect(page.getByText('Todos los sistemas funcionan con normalidad.')).toBeVisible()
    await expect(page.getByText(`${meta.version} · API ${meta.apiVersion}`)).toBeVisible()
    await expect(page).toHaveTitle(/Estado del servicio/)

    // No horizontal scrolling at any viewport (the mobile project runs this same test on a phone).
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow).toBeLessThanOrEqual(0)
  })

  test('the theme choice applies immediately and survives a reload', async ({ page }) => {
    await page.goto('/status')

    await page.getByRole('radio', { name: 'Oscuro' }).click()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')

    await page.reload()
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
    await expect(page.getByRole('radio', { name: 'Oscuro' })).toHaveAttribute('aria-checked', 'true')
  })

  test('unknown pages show a 404 screen with a way back', async ({ page }) => {
    await page.goto('/this/does/not/exist')

    await expect(page.getByRole('heading', { name: 'Página no encontrada' })).toBeVisible()
    await page.getByRole('link', { name: 'Volver al inicio' }).click()
    // Without a session, home is the sign-in page.
    await expect(page).toHaveURL(/\/iniciar-sesion/)
  })
})
