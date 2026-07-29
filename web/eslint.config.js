import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import prettierConfig from 'eslint-config-prettier'
import noRawHexColors from './eslint-rules/no-raw-hex-colors.js'

export default tseslint.config(
  { ignores: ['dist', 'coverage', 'node_modules'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.recommended, prettierConfig],
    languageOptions: {
      ecmaVersion: 2022,
      globals: { ...globals.browser, ...globals.node },
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'warn',
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
    },
  },
  {
    // P0-FE-02 DoD: raw hex color literals are banned outside theme.ts /
    // tailwind.config.ts (both plain .ts files, so untouched by this rule).
    files: ['**/*.{tsx,jsx}'],
    plugins: {
      local: { rules: { 'no-raw-hex-colors': noRawHexColors } },
    },
    rules: {
      'local/no-raw-hex-colors': 'error',
    },
  },
  {
    files: ['**/*.test.{ts,tsx}', 'src/setupTests.ts'],
    languageOptions: {
      globals: { ...globals.node },
    },
  },
)
