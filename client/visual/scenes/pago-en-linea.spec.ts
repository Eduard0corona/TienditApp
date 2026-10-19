import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import {
  CHECKOUT_ID,
  PAGOS_CON_AVISOS,
  PAGOS_CONECTADOS,
  PAGOS_NO_DISPONIBLES,
  PAGOS_SIN_CONECTAR,
  PAGO_EN_LINEA_ROUTES,
  PEDIDO_PAGADO_EN_LINEA,
  REGRESO_NO_SE_ENVIO,
  REGRESO_PEDIDO_RECIBIDO,
  REGRESO_SIN_PAGO,
  REGRESO_VENCIDO,
  TIENDA_PAGA_EN_LINEA,
  TIENDA_PAGA_EN_LINEA_A_DOMICILIO,
} from './pago-en-linea.mocks';
import { SELL_ROUTES } from './vender-pedidos.mocks';

// Paying online with Mercado Pago (Fase 24I, ADR 0031): Configuración › Pagos en línea (to connect, connected and
// offered, with its notices, and not available), the store's cart that pays online for pickup and to an address, the page
// Mercado Pago sends the customer back to (placed, not placed with its refund, expired and still to pay), and an order
// paid online with its «En línea» refunds and the refund dialog that offers it.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ extraRoutes: PAGO_EN_LINEA_ROUTES });

/** A dialog or the cart's panel as the screen shows it: its backdrop covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

test.describe('Configuración › Pagos en línea', () => {
  test.describe('conectada', () => {
    test.use({ prepareData: PAGOS_CONECTADOS });

    for (const look of LOOKS) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`pagos en línea conectada ${look.suffix}`, async ({ page }) => {
          await open(page, '/settings/online-payments');
          await expect(page.getByRole('switch', { name: 'Ofrecer pagar en línea en mi menú' })).toBeChecked();
          await snap(page, `pagos-en-linea-conectada-${look.suffix}`);
        });
      });
    }
  });

  test.describe('sin conectar', () => {
    test.use({ prepareData: PAGOS_SIN_CONECTAR });

    test('pagos en línea sin conectar', async ({ page }) => {
      await open(page, '/settings/online-payments');
      await expect(page.getByRole('button', { name: 'Conectar Mercado Pago' })).toBeEnabled();
      await snap(page, 'pagos-en-linea-sin-conectar');
    });

    test('pagos en línea sin conectar teléfono', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/settings/online-payments');
      await expect(page.getByRole('button', { name: 'Conectar Mercado Pago' })).toBeEnabled();
      await snap(page, 'pagos-en-linea-sin-conectar-telefono');
    });

    test('pagos en línea sin autorizar en Mercado Pago', async ({ page }) => {
      // Back from Mercado Pago without authorizing: the address forgets what it sent, and nothing is asked of the server.
      await open(page, '/settings/online-payments?error=access_denied&state=visual');
      await expect(page.getByText('No se conectó la cuenta: la autorización en Mercado Pago no se completó.')).toBeVisible();
      await snap(page, 'pagos-en-linea-sin-autorizar');
    });
  });

  test.describe('con avisos', () => {
    test.use({ prepareData: PAGOS_CON_AVISOS });

    test('pagos en línea con avisos', async ({ page }) => {
      await open(page, '/settings/online-payments');
      await expect(page.getByRole('button', { name: 'Desconectar' })).toBeDisabled();
      await expect(page.getByText(/Mercado Pago no hizo una devolución/)).toBeVisible();
      await snap(page, 'pagos-en-linea-con-avisos');
    });
  });

  test.describe('no disponible', () => {
    test.use({ prepareData: PAGOS_NO_DISPONIBLES });

    test('pagos en línea no disponible', async ({ page }) => {
      await open(page, '/settings/online-payments');
      await expect(page.getByRole('heading', { name: 'El pago en línea no está disponible por ahora' })).toBeVisible();
      await snap(page, 'pagos-en-linea-no-disponible');
    });
  });
});

test.describe('La tienda que cobra en línea', () => {
  test.use({ signedIn: false });
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
  });

  /** Two conchas and an americano in the cart, with the customer's name and phone written. */
  async function fillCart(page: Page) {
    await open(page, '/m/cafe-la-esquina');
    await page.getByRole('button', { name: 'Agregar Café americano' }).click();
    await page.getByRole('button', { name: 'Agregar Concha de vainilla' }).click();
    await page.getByRole('button', { name: 'Agregar Concha de vainilla' }).click();
    await page.getByRole('button', { name: /^Ver pedido · 3 · / }).click();
    const cart = await dialog(page, 'Tu pedido');
    await cart.getByLabel('Tu nombre').fill('Valeria Morales');
    await cart.getByLabel('Teléfono (WhatsApp)').fill('55 2210 7765');
    return cart;
  }

  test.describe('para recoger', () => {
    test.use({ prepareData: TIENDA_PAGA_EN_LINEA });

    test('tienda pagar en línea', async ({ page }) => {
      const cart = await fillCart(page);
      const pay = cart.getByRole('radiogroup', { name: 'Pago' });
      await expect(pay.getByRole('radio', { name: /^Pagar ahora en línea/ })).toBeChecked();
      await pay.scrollIntoViewIfNeeded();
      await expect(cart.getByRole('button', { name: /^Pagar · / })).toBeEnabled();
      await snapScreen(page, 'tienda-pagar-en-linea-telefono');
    });

    test('tienda pagar al recoger', async ({ page }) => {
      const cart = await fillCart(page);
      const pay = cart.getByRole('radiogroup', { name: 'Pago' });
      await pay.getByRole('radio', { name: /^Pagar al recoger/ }).check();
      await pay.scrollIntoViewIfNeeded();
      await expect(cart.getByRole('button', { name: /^Hacer pedido · / })).toBeEnabled();
      await snapScreen(page, 'tienda-pagar-al-recoger-telefono');
    });
  });

  test.describe('a domicilio', () => {
    test.use({ prepareData: TIENDA_PAGA_EN_LINEA_A_DOMICILIO });

    test('tienda pagar en línea a domicilio', async ({ page }) => {
      const cart = await fillCart(page);
      await cart.getByRole('radio', { name: 'A domicilio' }).click();
      await cart.getByLabel('Código postal').fill('06140');
      await expect(cart.getByText('Envío $45.00')).toBeVisible();
      await cart.getByLabel('Dirección').fill('Ámsterdam 210, Condesa');
      // Paid online, nothing is paid to the courier: no «¿Con cuánto pagas?».
      await expect(cart.getByLabel('¿Con cuánto pagas? (opcional)')).toHaveCount(0);
      await cart.getByRole('radiogroup', { name: 'Pago' }).scrollIntoViewIfNeeded();
      await snapScreen(page, 'tienda-pagar-en-linea-a-domicilio-telefono');
    });
  });
});

test.describe('El regreso de Mercado Pago', () => {
  test.use({ signedIn: false });
  const RETURN = `/m/cafe-la-esquina/pago/${CHECKOUT_ID}`;

  test.describe('pedido recibido', () => {
    test.use({ prepareData: REGRESO_PEDIDO_RECIBIDO });

    for (const look of LOOKS) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`regreso pedido recibido ${look.suffix}`, async ({ page }) => {
          await open(page, `${RETURN}?collection_id=128734659012&collection_status=approved&external_reference=${CHECKOUT_ID}`);
          await expect(page.getByRole('heading', { name: '¡Pago recibido!' })).toBeVisible();
          await expect(page.getByRole('link', { name: 'Seguir tu pedido' })).toBeVisible();
          await snap(page, `regreso-pedido-recibido-${look.suffix}`);
        });
      });
    }
  });

  test.describe('no se envió', () => {
    test.use({ prepareData: REGRESO_NO_SE_ENVIO });

    test('regreso pedido no enviado', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, `${RETURN}?collection_id=128734659012&collection_status=approved`);
      await expect(page.getByRole('heading', { name: 'No pudimos enviar tu pedido' })).toBeVisible();
      await snap(page, 'regreso-pedido-no-enviado-telefono');
    });
  });

  test.describe('vencido', () => {
    test.use({ prepareData: REGRESO_VENCIDO });

    test('regreso tiempo vencido', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      // A literal route: the coverage scene reads the routes each scene opens.
      await open(page, '/m/cafe-la-esquina/pago/7c1e9a40-52b8-4f6d-9e3a-1b0d4c2f8a65');
      await expect(page.getByRole('heading', { name: 'Se acabó el tiempo para pagar' })).toBeVisible();
      await snap(page, 'regreso-tiempo-vencido-telefono');
    });
  });

  test.describe('sin pago', () => {
    test.use({ prepareData: REGRESO_SIN_PAGO });

    test('regreso pago rechazado', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, `${RETURN}?collection_id=128734659013&collection_status=rejected`);
      await expect(page.getByRole('link', { name: 'Intentar de nuevo' })).toBeVisible();
      await snap(page, 'regreso-pago-rechazado-telefono');
    });

    test('regreso sin pagar', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, `${RETURN}?collection_status=null`);
      await expect(page.getByRole('link', { name: 'Pagar en Mercado Pago' })).toBeVisible();
      await snap(page, 'regreso-sin-pagar-telefono');
    });
  });
});

test.describe('Un pedido pagado en línea', () => {
  test.use({ extraRoutes: SELL_ROUTES, prepareData: PEDIDO_PAGADO_EN_LINEA });

  async function openOrder(page: Page) {
    await open(page, '/orders');
    const row = page.getByRole('row').filter({ hasText: 'Renata Ibarra' });
    const folio = (await row.getByRole('button', { name: /^ORD-\d+$/ }).textContent()) ?? '';
    await row.getByRole('button', { name: folio, exact: true }).click();
    return { detail: await dialog(page, `Pedido ${folio}`), folio };
  }

  test('pedidos detalle con devoluciones en línea', async ({ page }) => {
    const { detail } = await openOrder(page);
    const refunds = detail.getByRole('table').filter({ hasText: 'Motivo' });
    await expect(refunds.getByText('Mercado Pago ya lo devolvió.')).toBeVisible();
    await refunds.scrollIntoViewIfNeeded();
    await snapScreen(page, 'pedidos-detalle-devoluciones-en-linea');
  });

  test('pedidos reembolsar en línea', async ({ page }) => {
    const { detail, folio } = await openOrder(page);
    await detail.getByRole('button', { name: 'Reembolsar' }).click();
    const refund = await dialog(page, `Reembolsar ${folio}`);
    await expect(refund.getByLabel('Se devuelve en')).toHaveValue('En línea');
    await refund.getByLabel('Monto a devolver').fill('38');
    await refund.getByLabel('Motivo').fill('Faltó un café americano');
    await snapScreen(page, 'pedidos-reembolsar-en-linea');
  });
});
