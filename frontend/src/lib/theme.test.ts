import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createElement } from 'react'
import { render } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ThemeSwitcher } from '@/components/theme-switcher'
import { readThemePreference, resolveTheme, saveThemePreference } from './theme'

afterEach(() => {
  window.localStorage.clear()
  delete document.documentElement.dataset.theme
})

describe('theme', () => {
  it('resolves "system" from the operating system preference', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
    expect(resolveTheme('light', true)).toBe('light')
  })

  it('persists the preference and applies it to the document', () => {
    saveThemePreference('dark')

    expect(readThemePreference()).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
  })

  it('ignores unknown stored values and blocked storage', () => {
    window.localStorage.setItem('nexus.theme', 'neon')
    expect(readThemePreference()).toBe('system')

    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    expect(readThemePreference()).toBe('system')
  })
})

describe('ThemeSwitcher', () => {
  it('is a keyboard-operable radio group', async () => {
    render(createElement(ThemeSwitcher))
    const group = screen.getByRole('radiogroup', { name: 'Tema de color' })
    expect(screen.getByRole('radio', { name: 'Sistema' })).toHaveAttribute('aria-checked', 'true')

    await userEvent.click(screen.getByRole('radio', { name: 'Oscuro' }))
    expect(document.documentElement.dataset.theme).toBe('dark')

    group.querySelector<HTMLButtonElement>('[aria-checked="true"]')?.focus()
    await userEvent.keyboard('{ArrowLeft}')
    expect(screen.getByRole('radio', { name: 'Claro' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('radio', { name: 'Claro' })).toHaveFocus()
    expect(readThemePreference()).toBe('light')
  })
})
