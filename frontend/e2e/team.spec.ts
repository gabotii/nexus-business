import { expect, test } from '@playwright/test'
import { expectNoHorizontalScroll, linkFromEmail, signIn, signUp, uniqueEmail } from './support'

test.describe('identity and companies', () => {
  test('an owner creates a company and invites a teammate, who joins with the invited role', async ({
    page,
    browser,
  }) => {
    const owner = uniqueEmail('owner')
    const teammate = uniqueEmail('teammate')
    const company = `Taller ${Date.now()}`

    // Owner: sign up, confirm, sign in, create the company.
    await signUp(page, owner, 'Elena Ruiz')
    await page.getByRole('link', { name: 'Iniciar sesión' }).click()
    await signIn(page, owner)
    await expect(page.getByRole('heading', { name: 'Tus empresas' })).toBeVisible()
    await page.getByLabel('Nombre de la empresa').fill(company)
    await page.getByRole('button', { name: 'Crear empresa' }).click()
    await expect(page.getByRole('heading', { name: company })).toBeVisible()
    await expect(page).toHaveURL(/\/c\/taller-\d+$/)
    const workspaceUrl = page.url()

    // Invite the teammate as "Responsable".
    await page.getByRole('link', { name: 'Equipo' }).first().click()
    await page.getByRole('button', { name: 'Invitar' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Correo electrónico').fill(teammate)
    await dialog.getByLabel('Rol').selectOption({ label: 'Responsable' })
    await dialog.getByRole('button', { name: 'Enviar invitación' }).click()
    await expect(page.getByText(`Invitación enviada a ${teammate}.`)).toBeVisible()
    await expect(
      page.getByRole('listitem').filter({ hasText: teammate }).filter({ hasText: 'invitada por' }),
    ).toContainText('Responsable')

    // Teammate, in their own browser: sign up, open the invitation, sign in, accept.
    const other = await browser.newContext()
    const second = await other.newPage()
    await signUp(second, teammate, 'Marcos Gil')
    await second.goto(await linkFromEmail(teammate, 'Invitación para unirte'))
    await expect(second.getByRole('heading', { name: `Únete a ${company}` })).toBeVisible()
    await expect(second.getByText('Responsable')).toBeVisible()
    await second.getByRole('link', { name: 'Iniciar sesión para aceptar' }).click()
    await signIn(second, teammate)
    await second.getByRole('button', { name: 'Aceptar invitación' }).click()
    await expect(second.getByRole('heading', { name: company })).toBeVisible()
    await expect(second.getByText('Tu rol: Responsable', { exact: false })).toBeVisible()

    // A manager sees the team but cannot invite or edit roles.
    await second.getByRole('link', { name: 'Equipo' }).first().click()
    await expect(second.getByRole('cell', { name: /Elena Ruiz/ })).toBeVisible()
    await expect(second.getByRole('button', { name: 'Invitar' })).toHaveCount(0)
    await other.close()

    // The owner sees the new member.
    await page.goto(`${workspaceUrl}/equipo`)
    await expect(page.getByRole('cell', { name: /Marcos Gil/ })).toBeVisible()
  })

  test('signing out ends the session and protected pages ask to sign in again @mobile', async ({ page }) => {
    const email = uniqueEmail('solo')
    await signUp(page, email, 'Lucía Martín')
    await page.goto('/iniciar-sesion')
    await signIn(page, email)
    await expect(page.getByRole('heading', { name: 'Tus empresas' })).toBeVisible()
    await expectNoHorizontalScroll(page)

    // This device appears in the account's session list.
    await page.goto('/cuenta')
    await expect(page.getByText('Este dispositivo')).toBeVisible()
    await expectNoHorizontalScroll(page)

    await page.getByRole('button', { name: /Menú de Lucía Martín/ }).click()
    await page.getByRole('menuitem', { name: 'Cerrar sesión' }).click()
    await expect(page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible()

    await page.goto('/cuenta')
    await expect(page).toHaveURL(/\/iniciar-sesion\?next=%2Fcuenta$/)
  })

  test('wrong credentials get one generic message', async ({ page }) => {
    await page.goto('/iniciar-sesion')
    await page.getByLabel('Correo electrónico').fill(uniqueEmail('nobody'))
    await page.getByLabel('Contraseña').fill('not-a-real-password')
    await page.getByRole('button', { name: 'Entrar' }).click()

    await expect(page.getByRole('alert')).toContainText('Correo o contraseña incorrectos')
  })
})
