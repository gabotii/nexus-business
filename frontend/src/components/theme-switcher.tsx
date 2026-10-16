import { Monitor, Moon, Sun } from 'lucide-react'
import { useEffect, useState, type KeyboardEvent } from 'react'
import { cn } from '@/lib/utils'
import {
  applyTheme,
  readThemePreference,
  saveThemePreference,
  watchSystemTheme,
  type ThemePreference,
} from '@/lib/theme'

const options: { value: ThemePreference; label: string; icon: typeof Sun }[] = [
  { value: 'light', label: 'Claro', icon: Sun },
  { value: 'dark', label: 'Oscuro', icon: Moon },
  { value: 'system', label: 'Sistema', icon: Monitor },
]

/** Three-way theme control: a radio group (arrow keys move between options, as the ARIA pattern expects). */
export function ThemeSwitcher({ className }: { className?: string }) {
  const [preference, setPreference] = useState<ThemePreference>(readThemePreference)

  useEffect(() => {
    if (preference !== 'system') return
    return watchSystemTheme(() => applyTheme('system'))
  }, [preference])

  function select(value: ThemePreference) {
    setPreference(value)
    saveThemePreference(value)
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const index = options.findIndex((o) => o.value === preference)
    const delta =
      event.key === 'ArrowRight' || event.key === 'ArrowDown'
        ? 1
        : event.key === 'ArrowLeft' || event.key === 'ArrowUp'
          ? -1
          : 0
    if (delta === 0) return
    event.preventDefault()
    const next = options[(index + delta + options.length) % options.length]!
    select(next.value)
    event.currentTarget.querySelector<HTMLButtonElement>(`[data-value="${next.value}"]`)?.focus()
  }

  return (
    <div
      role="radiogroup"
      aria-label="Tema de color"
      onKeyDown={onKeyDown}
      className={cn('inline-flex rounded-lg border bg-card p-0.5', className)}
    >
      {options.map(({ value, label, icon: Icon }) => {
        const checked = preference === value
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={checked}
            aria-label={label}
            title={label}
            data-value={value}
            tabIndex={checked ? 0 : -1}
            onClick={() => select(value)}
            className={cn(
              'inline-flex size-7 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground',
              checked && 'bg-secondary text-foreground shadow-xs',
            )}
          >
            <Icon className="size-3.5" aria-hidden="true" />
          </button>
        )
      })}
    </div>
  )
}
