import js from '@eslint/js'
import reactHooks from 'eslint-plugin-react-hooks'
import globals from 'globals'
import tseslint from 'typescript-eslint'

export default tseslint.config(
  { ignores: ['dist', 'coverage', 'playwright-report', 'test-results', 'public'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    plugins: { 'react-hooks': reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      // Raw HTML injection is never needed in this app; keep the escape hatch closed (XSS).
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message: 'dangerouslySetInnerHTML is forbidden.',
        },
      ],
      // Browser storage is per device and readable by any script: never for session or tenant data.
      'no-restricted-globals': [
        'error',
        { name: 'localStorage', message: 'Use src/lib/preferences.ts (UI preferences only).' },
        { name: 'sessionStorage', message: 'Do not persist session or tenant data in the browser.' },
      ],
    },
  },
  {
    files: ['src/lib/preferences.ts'],
    rules: { 'no-restricted-globals': 'off' },
  },
)
