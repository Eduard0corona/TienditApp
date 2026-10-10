import { test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';

// The frame of the app around every screen: side menu, top bar, tab bar on phones and their menus.
test.describe('Inicio en cada tamaño', () => {
  for (const [size, viewport] of Object.entries(SIZES)) {
    test(`Inicio ${size}`, async ({ page }) => {
      await page.setViewportSize(viewport);
      await open(page, '/dashboard');
      await snap(page, `inicio-${size}`);
    });
  }
});
