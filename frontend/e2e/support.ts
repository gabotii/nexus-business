import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, type Page } from '@playwright/test'

/**
 * E-mails sent by the API under test. With Docker Compose they are caught by Mailpit (E2E_MAILPIT_URL, e.g.
 * http://127.0.0.1:8025); with a local `dotnet run` (Development) they are JSON files in the pickup folder.
 */
const mailpit = process.env.E2E_MAILPIT_URL
const pickupDir =
  process.env.E2E_MAIL_PICKUP_DIR ??
  path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../backend/src/Nexus.Api/mail-pickup')

interface Mail {
  subject: string
  text: string
}

async function mailsTo(to: string): Promise<Mail[]> {
  if (mailpit) {
    const search = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`)
    const { messages } = (await search.json()) as { messages: { ID: string; Subject: string }[] }
    return Promise.all(
      messages.map(async (m) => {
        const message = (await (await fetch(`${mailpit}/api/v1/message/${m.ID}`)).json()) as { Text: string }
        return { subject: m.Subject, text: message.Text }
      }),
    )
  }

  const files = await readdir(pickupDir).catch(() => [] as string[])
  const mails = await Promise.all(
    files
      .filter((f) => f.endsWith('.json'))
      .sort()
      .reverse()
      .map(async (f) => JSON.parse(await readFile(path.join(pickupDir, f), 'utf8')) as Mail & { to: string }),
  )
  return mails.filter((m) => m.to === to)
}

/**
 * Waits for the newest e-mail to `to` whose subject contains `subject` and returns the path + query of its link
 * (the host in the link is the configured public URL, which may differ from the URL the tests use).
 */
export async function linkFromEmail(to: string, subject: string): Promise<string> {
  let link = ''
  await expect
    .poll(
      async () => {
        const mail = (await mailsTo(to)).find((m) => m.subject.includes(subject))
        const url = mail?.text.match(/https?:\/\/\S+/)?.[0]
        link = url ? new URL(url).pathname + new URL(url).search : ''
        return link
      },
      { timeout: 20_000, message: `e-mail "${subject}" to ${to}` },
    )
    .not.toBe('')
  return link
}

export const PASSWORD = 'correct-horse-battery-staple'

export const uniqueEmail = (who: string) =>
  `${who}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`

/** Registers through the UI and confirms the address with the e-mailed link. */
export async function signUp(page: Page, email: string, name: string) {
  await page.goto('/registro')
  await page.getByLabel('Nombre').fill(name)
  await page.getByLabel('Correo electrónico').fill(email)
  await page.getByLabel('Contraseña').fill(PASSWORD)
  await page.getByRole('button', { name: 'Crear cuenta' }).click()
  await expect(page.getByRole('heading', { name: 'Revisa tu correo' })).toBeVisible()

  await page.goto(await linkFromEmail(email, 'Confirma tu correo'))
  await expect(page.getByText(/Tu correo está confirmado/)).toBeVisible()
}

export async function signIn(page: Page, email: string) {
  if (!page.url().includes('/iniciar-sesion')) await page.goto('/iniciar-sesion')
  await page.getByLabel('Correo electrónico').fill(email)
  await page.getByLabel('Contraseña').fill(PASSWORD)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

export async function expectNoHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow).toBeLessThanOrEqual(0)
}
