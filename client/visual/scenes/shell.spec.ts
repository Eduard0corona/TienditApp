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
