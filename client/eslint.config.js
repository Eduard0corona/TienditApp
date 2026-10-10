import js from '@eslint/js'
import globals from 'globals'
import jsxA11y from 'eslint-plugin-jsx-a11y'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  globalIgnores(['visual/.work', 'visual/.snapshots']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      // Labels tied to their fields and controls with a name: the diagnosis found 3 aria-labels in the whole app.
      jsxA11y.flatConfigs.recommended,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    rules: {
      // Option cards are labels around their radio, with the text a few elements deep.
      'jsx-a11y/label-has-associated-control': ['error', { assert: 'either', depth: 4 }],
      // Dialogs focus their main field on purpose: at the counter the cashier types the amount right away.
      'jsx-a11y/no-autofocus': 'off',
    },
  },
  // The visual scenes run in Node through Playwright: they read files and the process, and have no React.
  {
    files: ['visual/**/*.ts'],
    languageOptions: { globals: { ...globals.node, ...globals.browser } },
    rules: { 'react-hooks/rules-of-hooks': 'off' },
  },
])
