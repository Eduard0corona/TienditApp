import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config'

// Unit and component tests of the client: utilities that mirror API rules, the access matrix and the screens that
// show money, stock and errors. The API stays the authority; these keep the client from drifting from it.
export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      // CSS stays out of the tests, except theme.css: businessThemes.test.ts reads its colors to check them.
      css: { include: [/assets\/css\/theme\.css/] },
      restoreMocks: true,
    },
  }),
)
