import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, open, settle } from '../helpers';
import { SPECIMEN } from '../specimen';

/*
 * Keyboard focus (Fase 20F): the ring each piece of the specimen and of the frame draws when the keyboard reaches it,
 * and what a click leaves behind. Hyper took the outline off links and buttons and nothing showed where the keyboard
 * was; these keep the ring from going away again unnoticed. A Tab first tells the browser the keyboard is in use, so
 * the focus the scene moves next counts as the keyboard's (:focus-visible), as if Tab had taken it there.
 */

const RING = ['outline-style', 'outline-width', 'outline-color', 'outline-offset', 'box-shadow'];

function ringOf(locator: Locator) {
  return locator.evaluate((element, properties) => {
    const computed = getComputedStyle(element);
    return Object.fromEntries(properties.map((name) => [name, computed.getPropertyValue(name)]));
  }, RING);
}

/** Focuses an element and compares the screen around it, with room for the ring drawn outside it. */
async function snapRing(page: Page, locator: Locator, name: string) {
  await locator.scrollIntoViewIfNeeded();
  await locator.focus();
  await settle(page);
  const box = await locator.boundingBox();
  expect(box, `${name}: the element is on screen`).not.toBeNull();
  const room = 8;
  const x = Math.max(0, box!.x - room);
  const y = Math.max(0, box!.y - room);
  await expect(page).toHaveScreenshot(`${name}.png`, {
    clip: { x, y, width: box!.x + box!.width + room - x, height: box!.y + box!.height + room - y },
  });
}

for (const colorMode of ['light', 'dark'] as const) {
  test.describe(`Foco del teclado ${colorMode}`, () => {
    test.use({ colorMode });

    test(`foco en el muestrario ${colorMode}`, async ({ page }) => {
      await open(page, '/dashboard');
      await page.evaluate((html) => document.body.insertAdjacentHTML('beforeend', html), SPECIMEN);
      await settle(page);

      // After a click a button, a link or a check draws nothing; a field shows where the typing goes.
      const clicked: Record<string, Record<string, string>> = {};
      for (const piece of ['btn-primary', 'a', 'form-radio', 'form-control']) {
        const locator = page.locator(`#specimen [data-s="${piece}"]`);
        await locator.click();
        clicked[piece] = await ringOf(locator);
      }

      await page.keyboard.press('Tab');
      const keyboard: Record<string, Record<string, string>> = {};
      for (const locator of await page.locator('#specimen [data-s]').all()) {
        if (!(await locator.evaluate((element) => element.matches('a, button, input, select, textarea')))) continue;
        await locator.focus();
        keyboard[(await locator.getAttribute('data-s'))!] = await ringOf(locator);
      }
      expect.soft(`${JSON.stringify({ clicked, keyboard }, null, 1)}\n`).toMatchSnapshot(`foco-${colorMode}.json`);

      await snapRing(page, page.locator('#specimen [data-s="btn-primary"]'), `foco-boton-${colorMode}`);
      await snapRing(page, page.locator('#specimen [data-s="form-control"]'), `foco-campo-${colorMode}`);
      await snapRing(page, page.locator('#specimen [data-s="nav-link"]'), `foco-pestana-${colorMode}`);
    });

    test(`foco en el marco ${colorMode}`, async ({ page }) => {
      await page.setViewportSize(SIZES.desktop);
      await open(page, '/dashboard');
      await page.keyboard.press('Tab');
      const menu = page.getByRole('navigation', { name: 'Menú principal' });
      await snapRing(page, menu.getByRole('link', { name: 'Inicio', exact: true }), `foco-menu-${colorMode}`);
      await snapRing(page, page.getByRole('button', { name: /^Avisos/ }), `foco-barra-${colorMode}`);

      await page.setViewportSize(SIZES.phone);
      await open(page, '/dashboard');
      await page.keyboard.press('Tab');
      const tabs = page.getByRole('navigation', { name: 'Accesos rápidos' });
      await snapRing(page, tabs.getByRole('link', { name: 'Inicio', exact: true }), `foco-pestanas-${colorMode}`);
    });
  });
}
