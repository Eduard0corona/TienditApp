import { expect, test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';
import { ACCOUNT_PROFILE } from './cuenta-cliente.mocks';
import { TRACKING_LINK } from './publico-cuenta-marco.mocks';
import { SEGUIMIENTO_ROUTES, WAITING_TRACKING_LINK } from './seguimiento.mocks';

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

  test('detalle de un pedido por confirmar', async ({ page }) => {
    // Fase 29R: while the business has not accepted it, she may cancel it.
    await page.setViewportSize(SIZES.phone);
    await open(page, '/mis-pedidos/my-5');
    await expect(page.getByRole('button', { name: 'Cancelar pedido' })).toBeVisible();
    await expect(page.getByText('Puedes cancelarlo mientras el negocio no lo acepte.')).toBeVisible();
    await snap(page, 'detalle-pedido-por-confirmar-telefono');
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

  test('seguimiento de un pedido por confirmar', async ({ page }) => {
    // Fase 29R: the guest cancels it from the link while the business has not accepted it.
    await open(page, `/seguimiento#${WAITING_TRACKING_LINK}`);
    await page.getByRole('button', { name: 'Cancelar pedido' }).click();
    const dialog = page.getByRole('dialog', { name: 'Cancelar pedido' });
    await expect(dialog).toContainText('El negocio todavía no acepta tu pedido ORD-000361.');
    await snap(page, 'seguimiento-cancelar-pedido-escritorio');
  });

  test('seguimiento de un pedido que el cliente canceló', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/seguimiento#${WAITING_TRACKING_LINK}`);
    await page.getByRole('button', { name: 'Cancelar pedido' }).click();
    await page.getByRole('dialog', { name: 'Cancelar pedido' }).getByRole('button', { name: 'Cancelar pedido' }).click();
    await expect(page.getByText('Lo canceló el cliente.')).toBeVisible();
    await expect(page.getByText('Cancelaste tu pedido ORD-000361.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Cancelar pedido' })).toHaveCount(0);
    await snap(page, 'seguimiento-cancelado-por-el-cliente-telefono');
  });

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
