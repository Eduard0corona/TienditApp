import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test } from '../fixtures';
import { open, settle } from '../helpers';
import { SPECIMEN, STYLE_PROPERTIES } from '../specimen';

const src = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../src');

/** Every custom property our CSS and components read with var(--…): their values must not move when the template does. */
function variablesWeRead(): string[] {
  const names = new Set<string>();
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir)) {
      const file = path.join(dir, entry);
      if (statSync(file).isDirectory()) walk(file);
      else if (/\.(css|tsx?)$/.test(entry) && !/\.min\.css$/.test(entry)) {
        for (const match of readFileSync(file, 'utf8').matchAll(/var\(\s*(--[\w-]+)/g)) names.add(match[1]);
      }
    }
  };
  walk(src);
  return [...names].sort();
}

const VARIABLES = variablesWeRead();

for (const colorMode of ['light', 'dark'] as const) {
  for (const businessTheme of [0, 1, 2, 3, 4, 5, 6, 7]) {
    test.describe(`Muestrario ${colorMode} tema ${businessTheme}`, () => {
      test.use({ colorMode, businessTheme });

      test(`muestrario ${colorMode} tema ${businessTheme}`, async ({ page }) => {
        await open(page, '/dashboard');
        await page.evaluate((html) => document.body.insertAdjacentHTML('beforeend', html), SPECIMEN);
        await settle(page);

        const styles = await page.evaluate((properties) => Object.fromEntries(
          [...document.querySelectorAll<HTMLElement>('#specimen [data-s]')].map((element) => {
            const computed = getComputedStyle(element);
            return [element.dataset.s, Object.fromEntries(properties.map((name) => [name, computed.getPropertyValue(name)]))];
          })), [...STYLE_PROPERTIES]);
        const variables = await page.evaluate((names) => {
          const computed = getComputedStyle(document.documentElement);
          return Object.fromEntries(names.map((name) => [name, computed.getPropertyValue(name).trim()]));
        }, VARIABLES);

        expect(`${JSON.stringify(styles, null, 1)}\n`).toMatchSnapshot(`estilos-${colorMode}-${businessTheme}.json`);
        expect(`${JSON.stringify(variables, null, 1)}\n`).toMatchSnapshot(`variables-${colorMode}-${businessTheme}.json`);
        // One picture per mode is enough: the themes only change colors, which the styles above already compare.
        if (businessTheme === 0) await expect(page.locator('#specimen')).toHaveScreenshot(`muestrario-${colorMode}.png`);
      });
    });
  }
}
