import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, settle, snap, snapElement } from '../helpers';
import { PUBLICO_ROUTES } from './publico-cuenta-marco.mocks';
import { CON_PORTADA, TIENDA, TIENDA_FAVORITA } from './menu-tienda.mocks';
import { CUSTOMER_PROFILE } from './vender-pedidos.mocks';

// The online menu with the shape of the delivery apps (Fase 24E, ADR 0026): Café La Esquina with its cover, logo and the
// photos of its products; a product's sheet, the search, the tabs that follow the scroll, the store's information, the
// floating button and the order as a checkout page; and the owner's card for the cover in Configuración. The menu
// without a cover or photos (the colors of the business) is in publico-cuenta-marco.spec.ts.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
  { suffix: 'telefono-oscuro', colorMode: 'dark', viewport: SIZES.phone },
] as const;

/** The Latte's sheet, from its row in «Bebidas calientes»: large, almond milk and two of them. */
async function chooseLattes(page: Page) {
  await page.getByRole('region', { name: 'Bebidas calientes' }).getByRole('button', { name: 'Latte', exact: true }).click();
  const sheet = await dialog(page, 'Latte');
  await sheet.getByLabel('Grande').check();
  await sheet.getByLabel('Almendra').check();
  await sheet.getByRole('button', { name: 'Uno más' }).click();
  await expect(sheet.getByRole('button', { name: /^Agregar 2 · / })).toBeEnabled();
  return sheet;
}

/** The screen as the scene left it: no going back to the top (a tab took the page down). */
async function snapHere(page: Page, name: string) {
  await page.mouse.move(0, 0);
  await settle(page);
  await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: false });
}

/** Two lattes with their note, an americano and a concha: the floating button counts four. */
async function fillOrder(page: Page) {
  const sheet = await chooseLattes(page);
  await sheet.getByLabel('Instrucciones especiales').fill('Sin azúcar, bien caliente');
  await sheet.getByRole('button', { name: /^Agregar 2 · / }).click();
  await expect(sheet).toBeHidden();
  await page.getByRole('button', { name: 'Agregar Café americano' }).first().click();
  await page.getByRole('button', { name: 'Agregar Concha de vainilla' }).click();
  await expect(page.getByRole('button', { name: /^Ver pedido · 4 · / })).toBeVisible();
}

test.describe('La tienda', () => {
  test.use({ signedIn: false, extraRoutes: PUBLICO_ROUTES, prepareData: TIENDA });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`tienda ${look.suffix}`, async ({ page }) => {
        await open(page, '/m/cafe-la-esquina');
        await expect(page.getByRole('heading', { name: 'Café La Esquina' })).toBeVisible();
        await expect(page.getByRole('region', { name: 'Destacados' })).toBeVisible();
        await snap(page, `tienda-${look.suffix}`);
      });
    });
  }

  test('tienda producto teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina');
    await chooseLattes(page);
    await snap(page, 'tienda-producto-telefono', { fullPage: false });
  });

  test('tienda producto escritorio', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/m/cafe-la-esquina');
    await chooseLattes(page);
    await snap(page, 'tienda-producto-escritorio', { fullPage: false });
  });

  test('tienda buscar', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina');
    await page.getByRole('searchbox', { name: 'Buscar en Café La Esquina' }).fill('cafe');
    await expect(page.getByRole('heading', { name: /productos$/ })).toBeVisible();
    await snap(page, 'tienda-buscar-telefono');
  });

  test('tienda pestañas', async ({ page }) => {
    // Without the smooth scroll, the tab takes there at once and the shot is the same every time.
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina');
    const tabs = page.getByRole('navigation', { name: 'Categorías' });
    await tabs.getByRole('button', { name: 'Panadería' }).click();
    await expect(tabs.getByRole('button', { name: 'Panadería' })).toHaveAttribute('aria-current', 'true');
    await snapHere(page, 'tienda-pestanas-telefono');
  });

  test('tienda información', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina');
    await page.getByRole('button', { name: /^Hoy:/ }).click();
    const info = await dialog(page, 'Café La Esquina');
    await expect(info.getByRole('list', { name: 'Horario de la semana' })).toBeVisible();
    await snap(page, 'tienda-informacion-telefono', { fullPage: false });
  });

  test('tienda botón del pedido', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina');
    await fillOrder(page);
    await snap(page, 'tienda-boton-del-pedido-telefono', { fullPage: false });
  });

  test('tienda pedido teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina');
    await fillOrder(page);
    await page.getByRole('button', { name: /^Ver pedido · 4 · / }).click();
    const order = await dialog(page, 'Tu pedido');
    await expect(order.getByText('«Sin azúcar, bien caliente»')).toBeVisible();
    await snap(page, 'tienda-pedido-telefono', { fullPage: false });
  });

  test('tienda pedido escritorio', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/m/cafe-la-esquina');
    await fillOrder(page);
    await page.getByRole('button', { name: /^Ver pedido · 4 · / }).click();
    await dialog(page, 'Tu pedido');
    await snap(page, 'tienda-pedido-escritorio', { fullPage: false });
  });
});

// The heart over the cover (Fase 24F): a visitor's goes to sign in (the scenes above); a customer's keeps the store.
test.describe('La tienda de un favorito', () => {
  test.use({ profile: CUSTOMER_PROFILE, extraRoutes: PUBLICO_ROUTES, prepareData: TIENDA_FAVORITA });

  test('tienda favorita teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina');
    await expect(page.getByRole('button', { name: 'Guardar Café La Esquina en tus favoritos', pressed: true })).toBeVisible();
    await snap(page, 'tienda-favorita-telefono', { fullPage: false });
  });
});

test.describe('La portada en Configuración', () => {
  test('configuracion portada sin foto', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/settings/business');
    const card = page.getByRole('region', { name: 'Portada del menú' });
    await expect(card.getByRole('button', { name: 'Subir portada' })).toBeVisible();
    await snapElement(card, 'configuracion-portada-sin-foto-escritorio');
  });

  test.describe('con foto', () => {
    test.use({ prepareData: CON_PORTADA });

    test('configuracion portada con foto', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/settings/business');
      const card = page.getByRole('region', { name: 'Portada del menú' });
      await expect(card.getByRole('button', { name: 'Quitar portada' })).toBeVisible();
      await snapElement(card, 'configuracion-portada-con-foto-telefono');
    });
  });
});
