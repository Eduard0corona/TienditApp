import { expect, test } from '../fixtures';
import { SIZES, open, settle, snap } from '../helpers';

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

// The frame drawn by our own CSS since the Fase 20C (shell.css): each size and state of the menu, in both modes.
test.describe('Marco en modo oscuro', () => {
  test.use({ colorMode: 'dark' });

  for (const size of ['desktop', 'tablet', 'phone'] as const) {
    test(`Inicio oscuro ${size}`, async ({ page }) => {
      await page.setViewportSize(SIZES[size]);
      await open(page, '/dashboard');
      await snap(page, `inicio-oscuro-${size}`);
    });
  }

  test('menú del teléfono abierto en oscuro', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/dashboard');
    await page.getByRole('button', { name: 'Mostrar u ocultar el menú' }).click();
    await snap(page, 'menu-telefono-oscuro', { fullPage: false });
  });

  // The name grows out on the accent, which is light in dark mode: its text must be the dark one made for it.
  test('puntero sobre el menú condensado en oscuro', async ({ page }) => {
    await page.setViewportSize(SIZES.tablet);
    await open(page, '/dashboard');
    // Condensed, the labels are hidden (not in the accessible name): the entry is found by where it goes.
    await page.getByRole('navigation', { name: 'Menú principal' }).locator('a[href="/orders"]').hover();
    await settle(page);
    await expect(page).toHaveScreenshot('menu-condensado-puntero-oscuro.png');
  });
});

test.describe('Estados del menú', () => {
  test('menú condensado en escritorio', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/dashboard');
    await page.getByRole('button', { name: 'Mostrar u ocultar el menú' }).click();
    await snap(page, 'inicio-escritorio-condensado');
  });

  for (const link of ['Inicio', 'Pedidos']) {
    test(`puntero sobre ${link} en el menú completo`, async ({ page }) => {
      await page.setViewportSize(SIZES.desktop);
      await open(page, '/dashboard');
      await page.getByRole('navigation', { name: 'Menú principal' }).getByRole('link', { name: link, exact: true }).hover();
      await settle(page);
      await expect(page).toHaveScreenshot(`menu-puntero-${link.toLowerCase()}.png`);
    });
  }

  // A tablet with the browser's bars leaves less room than the menu needs: the menu scrolls inside the window, the
  // edge that hides links fades, and the name of the last one is not cut.
  test('menú condensado más alto que la ventana', async ({ page }) => {
    await page.setViewportSize({ ...SIZES.tablet, height: 600 });
    await open(page, '/dashboard');
    // Hovering scrolls the last entry into view first.
    await page.getByRole('navigation', { name: 'Menú principal' }).locator('a[href="/settings/business"]').hover();
    await settle(page);
    await expect(page).toHaveScreenshot('menu-condensado-ventana-baja.png');
  });

  test('menú de la cuenta en un teléfono chico', async ({ page }) => {
    await page.setViewportSize(SIZES.smallPhone);
    await open(page, '/dashboard');
    await page.getByRole('button', { name: 'Menú de tu cuenta' }).click();
    await page.getByRole('link', { name: 'Mi perfil' }).waitFor({ state: 'visible' });
    await snap(page, 'menu-cuenta-telefono-chico', { fullPage: false });
  });
});

// On paper only the page goes: no bars, menu, title or footer.
test('impresión de una pantalla con título y migas', async ({ page }) => {
  await page.setViewportSize(SIZES.desktop);
  await open(page, '/products/new');
  await page.emulateMedia({ media: 'print' });
  await snap(page, 'impresion-con-titulo');
});
