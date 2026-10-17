import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap, snapElement } from '../helpers';
import {
  ACCOUNT_PROFILE,
  CROISSANT_AGOTADO,
  PENDING_CART,
  SIN_DIRECCIONES,
  TAQUERIA_A_DOMICILIO,
  TIENDA_CON_CUENTA_ROUTES,
} from './cuenta-cliente.mocks';
import { MENU_A_DOMICILIO } from './publico-cuenta-marco.mocks';

// Comprar con tu cuenta (Fase 24B): Cuenta with the addresses María Fernanda keeps (her home in Roma Norte, used last,
// and her office in Condesa), Explorar with the postal code given on the device and the cart left without sending, the
// store's cart with her data and addresses, and «Pedir otra vez» from Mis pedidos.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

/** A dialog as the screen shows it: it covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/** What the device keeps before the page opens: the postal code given in Explorar and, if any, the carts left. */
async function remember(page: Page, values: { postalCode?: string; carts?: unknown }) {
  await page.addInitScript(({ postalCode, carts }) => {
    if (postalCode) window.localStorage.setItem('tienditapp.postalCode', postalCode);
    if (carts) window.localStorage.setItem('tienditapp.carts', JSON.stringify(carts));
  }, values);
}

/** Adds a Latte to the cart from its options dialog: large, with almond milk ($75). */
async function addLatte(page: Page) {
  await page.getByRole('button', { name: 'Agregar Latte' }).click();
  const options = await dialog(page, 'Latte');
  await options.getByLabel('Grande').check();
  await options.getByLabel('Almendra').check();
  await options.getByRole('button', { name: /^Agregar · / }).click();
  await expect(options).toBeHidden();
}

// ------------------------------------------------------------------ Cuenta

test.describe('Cuenta', () => {
  test.use({ profile: ACCOUNT_PROFILE });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`cuenta ${look.suffix}`, async ({ page }) => {
        await open(page, '/cuenta');
        await expect(page.getByText('Colima 145, depto. 3, Roma Norte · CP 06700')).toBeVisible();
        await expect(page.getByText('5522107765')).toBeVisible();
        await snap(page, `cuenta-${look.suffix}`);
      });
    });
  }

  test('cuenta agregar una dirección', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cuenta');
    await page.getByRole('button', { name: 'Agregar dirección' }).click();
    const add = await dialog(page, 'Agregar una dirección');
    await add.getByLabel('Nombre (opcional)').fill('Casa de mi mamá');
    await add.getByLabel('Código postal').fill('06760');
    await add.getByLabel('Dirección', { exact: true }).fill('Tlacotalpan 52, Roma Sur');
    await expect(add.getByRole('button', { name: 'Guardar' })).toBeEnabled();
    await snapScreen(page, 'cuenta-agregar-direccion-telefono');
  });

  test('cuenta borrar una dirección', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cuenta');
    await page.getByRole('button', { name: 'Borrar Oficina' }).click();
    const confirm = await dialog(page, 'Borrar dirección');
    await expect(confirm.getByText(/Los pedidos que ya enviaste ahí no cambian/)).toBeVisible();
    await snapScreen(page, 'cuenta-borrar-direccion-telefono');
  });

  test.describe('sin direcciones', () => {
    test.use({ prepareData: SIN_DIRECCIONES });

    test('cuenta sin direcciones', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/cuenta');
      await expect(page.getByText('Todavía no guardas direcciones')).toBeVisible();
      await snap(page, 'cuenta-sin-direcciones-telefono');
    });
  });
});

// ------------------------------------------------------------------ Explorar

test.describe('Explorar con tu código postal', () => {
  test.use({ signedIn: false, prepareData: TAQUERIA_A_DOMICILIO });

  for (const look of LOOKS.slice(0, 2)) {
    test(`explorar con código postal ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await remember(page, { postalCode: '06700', carts: PENDING_CART });
      await open(page, '/explorar');
      // Café La Esquina reaches Roma Norte; Taquería El Güero delivers, but not there.
      await expect(page.getByText('Envío $35.00')).toBeVisible();
      await expect(page.getByText('No llega a tu código postal')).toBeVisible();
      await expect(page.getByText(/Tienes un pedido sin enviar en/)).toContainText('(3 productos)');
      await snap(page, `explorar-codigo-postal-${look.suffix}`);
    });
  }

  test('explorar escribir el código postal', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/explorar');
    await page.getByRole('button', { name: 'Escribe tu código postal' }).click();
    const postal = page.locator('form.ex-postal');
    await postal.getByLabel('Código postal').fill('06140');
    await expect(postal.getByRole('button', { name: 'Listo' })).toBeEnabled();
    await snapElement(postal, 'explorar-codigo-postal-formulario');
    await postal.getByRole('button', { name: 'Listo' }).click();
    // The cards answer for Condesa, and the device remembers it.
    await expect(page.getByText('Entregar en')).toContainText('06140');
    await expect(page.getByText('Envío $45.00')).toBeVisible();
  });

  test('explorar seguir con el pedido', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await remember(page, { carts: PENDING_CART });
    await open(page, '/explorar');
    await page.getByRole('link', { name: 'Seguir con el pedido' }).click();
    // The store puts the cart back with the menu of now.
    await expect(page.getByRole('heading', { name: 'Café La Esquina' })).toBeVisible();
    await expect(page.getByRole('button', { name: /^Ver pedido · 3 · / })).toBeVisible();
  });
});

// ------------------------------------------------------------------ The store with the account

test.describe('Tienda con tu cuenta', () => {
  test.use({ profile: ACCOUNT_PROFILE, prepareData: MENU_A_DOMICILIO, extraRoutes: TIENDA_CON_CUENTA_ROUTES });
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
  });

  /** Two lattes, enough for the minimum of Roma Norte ($150), to an address: the last one the account used first. */
  async function cartToAnAddress(page: Page) {
    await open(page, '/m/cafe-la-esquina');
    await addLatte(page);
    await addLatte(page);
    await page.getByRole('button', { name: /^Ver pedido · 2 · / }).click();
    const cart = await dialog(page, 'Tu pedido');
    await cart.getByRole('radio', { name: 'A domicilio' }).click();
    await expect(cart.getByRole('radio', { name: /^Casa/ })).toHaveAttribute('aria-checked', 'true');
    await expect(cart.getByText('Envío $35.00 · gratis desde $400.00 · pedido mínimo $150.00')).toBeVisible();
    return cart;
  }

  test('tienda con tu cuenta carrito a domicilio', async ({ page }) => {
    const cart = await cartToAnAddress(page);
    // Her name and phone are already written.
    await expect(cart.getByLabel('Tu nombre')).toHaveValue('María Fernanda López');
    await expect(cart.getByLabel('Teléfono (WhatsApp)')).toHaveValue('5522107765');
    await expect(cart.getByLabel('Dirección', { exact: true })).toHaveCount(0);
    await snapScreen(page, 'tienda-cuenta-carrito-a-domicilio-telefono');
  });

  test('tienda con tu cuenta otra dirección', async ({ page }) => {
    const cart = await cartToAnAddress(page);
    await cart.getByRole('radio', { name: 'Otra dirección' }).click();
    await cart.getByLabel('Código postal').fill('06140');
    await expect(cart.getByText('Envío $45.00')).toBeVisible();
    await cart.getByLabel('Dirección', { exact: true }).fill('Durango 260, Roma Norte');
    await expect(cart.getByLabel('Guardar esta dirección en mi cuenta')).toBeChecked();
    await cart.getByLabel('Nombre de la dirección').fill('Gimnasio');
    await snapScreen(page, 'tienda-cuenta-otra-direccion-telefono');
  });

  test('tienda con tu cuenta pedido enviado', async ({ page }) => {
    const cart = await cartToAnAddress(page);
    await cart.getByRole('button', { name: /^Enviar pedido · / }).click();
    await expect(page.getByRole('heading', { name: '¡Pedido enviado!' })).toBeVisible();
    await expect(page.getByText('Lo encuentras en tus pedidos.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Ver mis pedidos' })).toBeVisible();
    await snap(page, 'tienda-cuenta-pedido-enviado-telefono');
  });
});

test.describe('Pedir otra vez', () => {
  test.use({ profile: ACCOUNT_PROFILE, prepareData: CROISSANT_AGOTADO, extraRoutes: TIENDA_CON_CUENTA_ROUTES });

  test('pedir otra vez con un producto agotado', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/mis-pedidos');
    await page.getByRole('button', { name: 'Pedir otra vez ORD-000390 de Café La Esquina' }).click();
    // The large latte comes back at today's price; the croissant is sold out.
    await expect(page.getByRole('status').filter({ hasText: '1 producto ya no está en el menú de ahora' })).toBeVisible();
    await expect(page.getByRole('button', { name: /^Ver pedido · 1 · / })).toBeVisible();
    await snap(page, 'pedir-otra-vez-telefono');
  });
});
