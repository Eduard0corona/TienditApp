import { expect, test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';
import { ACCOUNT_PROFILE } from './cuenta-cliente.mocks';
import { TRACKING_LINK } from './publico-cuenta-marco.mocks';
import { SEGUIMIENTO_ROUTES } from './seguimiento.mocks';

// Seguimiento (Fase 24C): the detail of an order of María Fernanda's account (the tortas on their way to her home, and a
// latte the business cancelled), and the order of a guest followed with the link of its receipt.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

// ------------------------------------------------------------------ The account's order

test.describe('Detalle del pedido', () => {
  test.use({ profile: ACCOUNT_PROFILE, extraRoutes: SEGUIMIENTO_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`detalle del pedido ${look.suffix}`, async ({ page }) => {
        await open(page, '/mis-pedidos/my-1');
        await expect(page.getByRole('listitem').filter({ hasText: 'En camino, ahora' })).toBeVisible();
        await expect(page.getByRole('link', { name: /Seguir el envío con la paquetería/ })).toBeVisible();
        await expect(page.getByText('Colima 145, depto. 3, Roma Norte, CP 06700')).toBeVisible();
        await snap(page, `detalle-pedido-${look.suffix}`);
      });
    });
  }

  test('detalle de un pedido cancelado', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/mis-pedidos/my-4');
    await expect(page.getByText('Se nos acabó la leche de almendra, perdón.')).toBeVisible();
    await snap(page, 'detalle-pedido-cancelado-telefono');
  });

  test('detalle de un pedido que no es de la cuenta', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/mis-pedidos/de-otra-cuenta');
    await expect(page.getByText(/No encontramos ese pedido en tu cuenta\./)).toBeVisible();
    await snap(page, 'detalle-pedido-no-encontrado-telefono');
  });

  test('mis pedidos ver pedido', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/mis-pedidos');
    await expect(page.getByRole('region', { name: 'En curso' })).toContainText('En camino');
    await page.getByRole('link', { name: 'Ver pedido ORD-000412 de Café La Esquina' }).click();
    await expect(page.getByRole('heading', { name: 'Pedido ORD-000412' })).toBeVisible();
  });
});

// ------------------------------------------------------------------ A guest with the link of the receipt

test.describe('Seguimiento con enlace', () => {
  test.use({ signedIn: false, extraRoutes: SEGUIMIENTO_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`seguimiento ${look.suffix}`, async ({ page }) => {
        await open(page, `/seguimiento#${TRACKING_LINK}`);
        await expect(page.getByRole('listitem').filter({ hasText: 'En preparación, ahora' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Tu pedido a domicilio' })).toBeVisible();
        await snap(page, `seguimiento-${look.suffix}`);
      });
    });
  }

  test('seguimiento sin enlace', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/seguimiento');
    await expect(page.getByRole('heading', { name: 'Abre el enlace de tu recibo' })).toBeVisible();
    await snap(page, 'seguimiento-sin-enlace-telefono');
  });

  test('seguimiento con un enlace vencido', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/seguimiento#AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE');
    await expect(page.getByText('No encontramos ese pedido. El enlace pudo haber vencido.')).toBeVisible();
    await snap(page, 'seguimiento-enlace-vencido-telefono');
  });
});
