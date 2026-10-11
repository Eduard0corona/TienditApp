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
      // CSS stays out of the tests, except a stylesheet a test reads as text (?raw): businessThemes.test.ts checks the
      // colors of theme.css, hyperClasses.test.ts the classes of every stylesheet and focusRing.test.ts that none,
      // Sass included, takes the focus ring away. Without this, ?raw reads empty.
      css: { include: [/\.s?css\?raw$/] },
      restoreMocks: true,
    },
  }),
)
