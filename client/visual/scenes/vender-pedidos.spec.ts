import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import {
  COURIER_ORDERS_DATA,
  CUSTOMER_PROFILE,
  DELIVERY_ORDERS_DATA,
  DELIVERY_ROUTES,
  RAPPI_ORDERS_DATA,
  RAPPI_PRICES_DATA,
  RAPPI_ROUTES,
  SELL_ROUTES,
} from './vender-pedidos.mocks';

// Selling and following orders: Vender, Mesas, Pedidos, Orden detallada, Cocina and a customer's Mis pedidos, and the
// orders of a delivery platform (Rappi) through all of them.

/** The whole page. */
async function snapPage(page: Page, name: string) {
  await snap(page, name);
}

/**
 * A dialog as the screen shows it. Its backdrop covers the screen, not the page: a full-page shot would show the page
 * below the fold uncovered beside it.
 */
async function snapDialog(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/** Scrolls a dialog taller than the screen to its end, for a shot of what is below. */
async function scrollToEnd(panel: Locator) {
  await panel.evaluate((root) => {
    for (const element of [root, ...root.querySelectorAll('*')]) element.scrollTop = element.scrollHeight;
  });
}

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Taps a product tile of Vender, as the cashier does. */
async function tapProduct(page: Page, name: string, times = 1) {
  const tile = page.getByRole('button', { name: new RegExp(`^Agregar ${escapeRegExp(name)},`) });
  for (let tap = 0; tap < times; tap += 1) await tile.click();
}

/** Opens the variants and extras of the latte and picks a large one with almond milk. */
async function chooseLatte(page: Page): Promise<Locator> {
  await tapProduct(page, 'Latte');
  const options = await dialog(page, 'Latte');
  await options.getByLabel('Grande').check();
  await options.getByLabel('Almendra').check();
  return options;
}

/** Two americanos, a croissant and a large almond latte: $183.00 with IVA included. */
async function fillTicket(page: Page) {
  await tapProduct(page, 'Café americano', 2);
  await tapProduct(page, 'Croissant de mantequilla');
  const options = await chooseLatte(page);
  await options.getByRole('button', { name: /^Agregar · / }).click();
  await expect(options).toBeHidden();
}

/** Cobrar from the ticket: the side panel on a desktop, the bar at the bottom on a phone. */
async function openCharge(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: /^Cobrar( \$|$)/ }).click();
  return dialog(page, 'Cobrar');
}

/** Charges the ticket in cash (the bill handed over, or the exact amount) and waits for the sale just recorded. */
async function charge(page: Page, cashReceived?: string): Promise<Locator> {
  const panel = await openCharge(page);
  if (cashReceived) await panel.getByLabel('Efectivo recibido').fill(cashReceived);
  await panel.getByRole('button', { name: /^Cobrar \$/ }).click();
  const sale = await dialog(page, /^Venta ORD-\d+ cobrada$/);
  // The charge dialog closes as the sale opens: only the sale stays.
  await expect(page.getByRole('dialog')).toHaveCount(1);
  return sale;
}

/**
 * The device remembers a screen kept in focus mode and reopens it without the menu. Entering it with the button also
 * asks the browser for full screen, which headless browsers answer differently, so the scenes start focused instead.
 */
async function startFocused(page: Page, area: 'sell' | 'kitchen') {
  await page.addInitScript((focused) => {
    try {
      window.localStorage.setItem('tienditapp.focusedAreas', JSON.stringify([focused]));
    } catch {
      // Storage blocked: the screen opens with the menu.
    }
  }, area);
}

test.describe('Vender', () => {
  test.use({ extraRoutes: SELL_ROUTES });

  test('Vender escritorio', async ({ page }) => {
    await open(page, '/sell');
    await snapPage(page, 'vender-escritorio');
  });

  test('Vender teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await snapPage(page, 'vender-telefono');
  });

  test('Vender tableta', async ({ page }) => {
    await page.setViewportSize(SIZES.tablet);
    await open(page, '/sell');
    await snapPage(page, 'vender-tableta');
  });

  test('Vender con variantes y extras', async ({ page }) => {
    await open(page, '/sell');
    await chooseLatte(page);
    await snapDialog(page, 'vender-opciones-escritorio');
  });

  test('Vender con variantes y extras en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await chooseLatte(page);
    await snapDialog(page, 'vender-opciones-telefono');
  });

  test('Vender con el ticket lleno', async ({ page }) => {
    await open(page, '/sell');
    await fillTicket(page);
    await snapPage(page, 'vender-ticket-escritorio');
  });

  test('Vender con el ticket abierto en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await fillTicket(page);
    await snapPage(page, 'vender-barra-telefono');
    await page.getByRole('button', { name: /artículos · / }).click();
    await dialog(page, 'Ticket');
    await snapDialog(page, 'vender-ticket-telefono');
  });

  test('Cobrar en efectivo con cambio', async ({ page }) => {
    await open(page, '/sell');
    await fillTicket(page);
    const panel = await openCharge(page);
    await panel.getByLabel('Efectivo recibido').fill('200');
    await expect(panel.getByText('Cambio')).toBeVisible();
    await snapDialog(page, 'vender-cobrar-efectivo');
  });

  test('Cobrar con pago mixto', async ({ page }) => {
    await open(page, '/sell');
    await fillTicket(page);
    const panel = await openCharge(page);
    // $100 with card, and the rest in cash with a $100 bill.
    await panel.getByRole('radio', { name: 'Tarjeta' }).click();
    await panel.getByLabel('Monto', { exact: true }).fill('100');
    await panel.getByLabel(/Autorización o últimos 4 dígitos/).fill('4821');
    await panel.getByRole('button', { name: 'Agregar otro pago' }).click();
    await panel.getByLabel('Efectivo recibido').fill('100');
    await expect(panel.getByText('Falta por cobrar')).toBeVisible();
    await snapDialog(page, 'vender-cobrar-mixto');
  });

  test('Cobrar en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await fillTicket(page);
    const panel = await openCharge(page);
    await panel.getByLabel('Efectivo recibido').fill('500');
    await snapDialog(page, 'vender-cobrar-telefono');
  });

  test('Venta cobrada con su ticket', async ({ page }) => {
    await open(page, '/sell');
    await fillTicket(page);
    await charge(page, '200');
    await snapDialog(page, 'vender-venta-cobrada');
  });

  test('Venta cobrada en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await fillTicket(page);
    // The exact amount: no change to hand over.
    await charge(page);
    await snapDialog(page, 'vender-venta-cobrada-telefono');
  });

  test('Ticket impreso', async ({ page }) => {
    await open(page, '/sell');
    await fillTicket(page);
    const sale = await charge(page, '200');
    // Imprimir marks the page to print only the ticket; the print dialog itself is not part of the scene.
    await page.evaluate(() => {
      window.print = () => undefined;
    });
    await sale.getByRole('button', { name: 'Imprimir' }).click();
    await page.emulateMedia({ media: 'print' });
    await snapPage(page, 'vender-ticket-impreso');
  });

  test.describe('Modo enfocado', () => {
    test.beforeEach(async ({ page }) => startFocused(page, 'sell'));

    test('Vender enfocado escritorio', async ({ page }) => {
      await open(page, '/sell');
      await expect(page.getByRole('button', { name: 'Salir del modo enfocado' })).toBeVisible();
      await snapPage(page, 'vender-enfocado-escritorio');
    });

    test('Vender enfocado teléfono', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/sell');
      await expect(page.getByRole('button', { name: 'Salir del modo enfocado' })).toBeVisible();
      await snapPage(page, 'vender-enfocado-telefono');
    });
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('Vender escritorio oscuro', async ({ page }) => {
      await open(page, '/sell');
      await snapPage(page, 'vender-escritorio-oscuro');
    });

    test('Venta cobrada oscuro', async ({ page }) => {
      await open(page, '/sell');
      await fillTicket(page);
      await charge(page, '200');
      await snapDialog(page, 'vender-venta-cobrada-oscuro');
    });
  });
});

test.describe('Pedidos de Rappi', () => {
  // The business takes Rappi orders, which it charges with the prices of its Rappi app: Vender offers Rappi as an
  // origin and Pedidos filters by it.
  test.use({ extraRoutes: RAPPI_ROUTES, prepareData: RAPPI_PRICES_DATA });

  /**
   * A Rappi order on the ticket: the origin chosen and the folio of its app typed. The ticket goes from the menu's prices
   * ($183.00) to those of the Rappi app: two americanos at $42, a croissant at $45 and a large almond latte at $91.
   */
  async function chooseRappi(scope: Page | Locator) {
    await scope.getByRole('radio', { name: 'Rappi' }).click();
    await scope.getByLabel('Folio de Rappi').fill('152390');
    await expect(scope.getByText('Con tus precios de Rappi')).toBeVisible();
  }

  /** Opens the detail of an order from its folio in the list. */
  async function openOrder(page: Page, orderNumber: string) {
    await page.getByRole('button', { name: orderNumber, exact: true }).click();
    return dialog(page, `Pedido ${orderNumber}`);
  }

  test('Vender pedido de Rappi', async ({ page }) => {
    await open(page, '/sell');
    await fillTicket(page);
    await chooseRappi(page);
    await snapPage(page, 'vender-rappi-escritorio');

    // Rappi charged it in its app: one action registers it, without the charge or the drawer.
    const ticket = page.getByRole('complementary', { name: 'Ticket de la venta' });
    await scrollToEnd(ticket);
    await expect(ticket.getByRole('button', { name: /^Registrar pedido de Rappi · \$220\.00/ })).toBeVisible();
    await snapPage(page, 'vender-rappi-escritorio-final');
  });

  test('Vender pedido de Rappi en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await fillTicket(page);
    await page.getByRole('button', { name: /artículos · / }).click();
    const ticket = await dialog(page, 'Ticket');
    await chooseRappi(ticket);
    await snapDialog(page, 'vender-rappi-telefono');

    await scrollToEnd(ticket);
    await expect(ticket.getByText('Rappi ya lo cobró: no entra a tu caja.')).toBeVisible();
    await snapDialog(page, 'vender-rappi-telefono-final');
  });

  test('Pedido de Rappi registrado', async ({ page }) => {
    await open(page, '/sell');
    await fillTicket(page);
    await chooseRappi(page);
    await page.getByRole('button', { name: /^Registrar pedido de Rappi · / }).click();
    const sale = await dialog(page, /^Pedido ORD-\d+ de Rappi registrado$/);
    await expect(sale.getByRole('button', { name: 'Imprimir para la bolsa' })).toBeVisible();
    await snapDialog(page, 'vender-rappi-registrado');
  });

  test.describe('con pedidos de hoy', () => {
    // Orders of a business without prices in the Rappi app: charged with its menu's.
    test.use({ prepareData: RAPPI_ORDERS_DATA });

    test('Pedidos por canal: Rappi', async ({ page }) => {
      await open(page, '/orders');
      await page.getByLabel('Canal').selectOption({ label: 'Rappi' });
      await expect(page.getByRole('button', { name: 'ORD-000348', exact: true })).toBeHidden();
      await expect(page.getByText('Folio 152390')).toBeVisible();
      await snapPage(page, 'pedidos-filtro-rappi');
    });

    test('Detalle de un pedido de Rappi', async ({ page }) => {
      await open(page, '/orders');
      const detail = await openOrder(page, 'ORD-000349');
      await expect(detail.getByText('Plataforma · 152390')).toBeVisible();
      await snapDialog(page, 'pedidos-detalle-rappi');
    });

    test('Cocina con un pedido de Rappi', async ({ page }) => {
      await open(page, '/kitchen');
      // The folio of the app, big, to find the bag; the order number of the business goes small.
      await expect(page.getByRole('heading', { name: '152395' })).toBeVisible();
      await snapPage(page, 'cocina-rappi');
    });
  });
});

test.describe('A domicilio', () => {
  // Café La Esquina delivers to Roma ($35, from $150, free from $400) and Condesa ($45); two orders of today went out.
  test.use({ extraRoutes: DELIVERY_ROUTES, prepareData: DELIVERY_ORDERS_DATA });

  /** Sent to Roma Norte, as the cashier types the address. */
  async function sendToRoma(ticket: Locator) {
    await ticket.getByRole('switch', { name: 'A domicilio' }).check();
    await ticket.getByLabel('Código postal').fill('06700');
    await ticket.getByLabel('Dirección').fill('Colima 145, depto. 3, Roma Norte');
    await ticket.getByLabel('Referencias (opcional)').fill('Portón negro, timbre 3');
    await expect(ticket.getByText('Roma · Envío $35.00 · gratis desde $400.00 · pedido mínimo $150.00')).toBeVisible();
  }

  /** Two tortas and an americano: $175.00, above the minimum of Roma. */
  async function fillDeliveryTicket(page: Page) {
    await tapProduct(page, 'Torta de jamón', 2);
    await tapProduct(page, 'Café americano');
  }

  /** Opens the detail of an order from its folio in the list. */
  async function openOrder(page: Page, customer: string) {
    const row = page.getByRole('row').filter({ hasText: customer });
    const folio = (await row.getByRole('button', { name: /^ORD-\d+$/ }).textContent()) ?? '';
    await row.getByRole('button', { name: folio, exact: true }).click();
    return dialog(page, `Pedido ${folio}`);
  }

  test('Vender a domicilio', async ({ page }) => {
    await open(page, '/sell');
    await fillDeliveryTicket(page);
    await sendToRoma(page.getByRole('complementary', { name: 'Ticket de la venta' }));
    await snapPage(page, 'vender-a-domicilio-escritorio');
  });

  test('Vender a domicilio en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await fillDeliveryTicket(page);
    await page.getByRole('button', { name: /artículos · / }).click();
    const sheet = await dialog(page, 'Ticket');
    await sendToRoma(sheet);
    await snapDialog(page, 'vender-a-domicilio-telefono');
  });

  test('Venta a domicilio cobrada con su ticket', async ({ page }) => {
    await open(page, '/sell');
    await fillDeliveryTicket(page);
    await sendToRoma(page.getByRole('complementary', { name: 'Ticket de la venta' }));
    const sale = await charge(page, '300');
    await expect(sale.getByText('Envío a domicilio')).toBeVisible();
    await snapDialog(page, 'vender-a-domicilio-cobrada');
  });

  test('Detalle de un pedido a domicilio con su envío de Paquetenvia', async ({ page }) => {
    await open(page, '/orders');
    const detail = await openOrder(page, 'Sofía Ramírez');
    await expect(detail.getByRole('link', { name: 'Seguir envío' })).toBeVisible();
    await snapDialog(page, 'pedidos-detalle-a-domicilio');
  });

  test('Registrar el envío de Paquetenvia', async ({ page }) => {
    await open(page, '/orders');
    const detail = await openOrder(page, 'Diego Hernández');
    await detail.getByRole('button', { name: 'Registrar envío de Paquetenvia' }).click();
    await detail.getByLabel('Folio de Paquetenvia').fill('pq 48230');
    await detail.getByLabel('Enlace para seguirlo (opcional)').fill('https://paquetenvia.mx/rastreo/PQ48230');
    await snapDialog(page, 'pedidos-registrar-paquetenvia');
    await detail.getByRole('button', { name: 'Guardar envío' }).click();
    await expect(detail.getByText('folio PQ48230')).toBeVisible();
    await snapDialog(page, 'pedidos-paquetenvia-registrado');
  });

  test('Cocina con un pedido a domicilio', async ({ page }) => {
    await open(page, '/kitchen');
    await expect(page.getByText('A domicilio')).toBeVisible();
    await snapPage(page, 'cocina-a-domicilio');
  });

  test('Vender a domicilio con «paga con» y su cambio', async ({ page }) => {
    await open(page, '/sell');
    await fillDeliveryTicket(page);
    const ticket = page.getByRole('complementary', { name: 'Ticket de la venta' });
    await sendToRoma(ticket);
    await ticket.getByLabel('Paga con (opcional)').fill('500');
    await expect(ticket.getByText('Cambio:')).toBeVisible();
    await snapPage(page, 'vender-a-domicilio-paga-con');
  });

  test.describe('Con Paquetenvia', () => {
    // Fase 22D: besides the two above, an order on its way that Paquetenvia collects and one it delivered and owes.
    test.use({ prepareData: COURIER_ORDERS_DATA });

    test('Salió a entregar, cobrando en efectivo con «paga con»', async ({ page }) => {
      await open(page, '/orders');
      const detail = await openOrder(page, 'Diego Hernández');
      await detail.getByRole('button', { name: 'Salió a entregar' }).click();
      await detail.getByLabel('Folio de Paquetenvia').fill('pq 48230');
      await detail.getByLabel('Paga con (opcional)').fill('500');
      await expect(detail.getByText('Cambio:')).toBeVisible();
      await snapDialog(page, 'pedidos-salio-a-entregar');
      await detail.getByRole('button', { name: 'Salió a entregar' }).click();
      await expect(detail.getByText('En camino')).toBeVisible();
      await snapDialog(page, 'pedidos-en-camino');
    });

    test('Pedidos en camino y por liquidar', async ({ page }) => {
      await open(page, '/orders');
      await expect(page.getByText(/Lo debe Paquetenvia/)).toBeVisible();
      await snapPage(page, 'pedidos-con-paquetenvia');
    });

    test('Pedido entregado que debe Paquetenvia', async ({ page }) => {
      await open(page, '/orders');
      const detail = await openOrder(page, 'Andrés Morales');
      await expect(detail.getByText(/Paquetenvia te debe/)).toBeVisible();
      await snapDialog(page, 'pedidos-debe-paquetenvia');
    });
  });
});

test.describe('Mesas', () => {
  /** Mesa 4: two rounds, the second one ready to take to the table. */
  async function openTableFour(page: Page) {
    await page.getByRole('button', { name: /^Mesa 4, ocupada/ }).click();
    const panel = await dialog(page, /^Mesa 4 · /);
    await expect(panel.getByRole('heading', { name: 'Ronda 2' })).toBeVisible();
  }

  test('Mesas escritorio', async ({ page }) => {
    await open(page, '/tables');
    await snapPage(page, 'mesas-escritorio');
  });

  test('Mesas teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/tables');
    await snapPage(page, 'mesas-telefono');
  });

  test('Cuenta abierta de una mesa', async ({ page }) => {
    await open(page, '/tables');
    await openTableFour(page);
    await snapDialog(page, 'mesas-cuenta-abierta');
  });

  test('Cuenta abierta de una mesa en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/tables');
    await openTableFour(page);
    await snapDialog(page, 'mesas-cuenta-abierta-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('Mesas escritorio oscuro', async ({ page }) => {
      await open(page, '/tables');
      await snapPage(page, 'mesas-escritorio-oscuro');
    });
  });
});

test.describe('Pedidos', () => {
  /** Opens the detail of an order from its folio in the list. */
  async function openOrder(page: Page, orderNumber: string) {
    await page.getByRole('button', { name: orderNumber, exact: true }).click();
    return dialog(page, `Pedido ${orderNumber}`);
  }

  test('Pedidos escritorio', async ({ page }) => {
    await open(page, '/orders');
    await snapPage(page, 'pedidos-escritorio');
  });

  test('Pedidos teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/orders');
    await snapPage(page, 'pedidos-telefono');
  });

  test('Detalle de una venta', async ({ page }) => {
    await open(page, '/orders');
    // Two tortas (one without chipotle) and two aguas, in preparation and not paid yet.
    await openOrder(page, 'ORD-000345');
    await snapDialog(page, 'pedidos-detalle-venta');
  });

  test('Detalle de una venta en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/orders');
    const detail = await openOrder(page, 'ORD-000345');
    await snapDialog(page, 'pedidos-detalle-venta-telefono');
    await scrollToEnd(detail);
    await snapDialog(page, 'pedidos-detalle-venta-telefono-final');
  });

  test('Detalle de un servicio', async ({ page }) => {
    await open(page, '/orders');
    // Only the services of the day: a coffee machine received for maintenance, with its checklist, the piece to track
    // and an advance payment, is the first one.
    await page.getByLabel('Tipo').selectOption({ label: 'Servicio' });
    await expect(page.getByRole('button', { name: 'ORD-000345', exact: true })).toBeHidden();
    const detail = await openOrder(page, 'ORD-000342');
    await snapDialog(page, 'pedidos-detalle-servicio');
    await scrollToEnd(detail);
    await snapDialog(page, 'pedidos-detalle-servicio-final');
  });

  test('Registrar un pago', async ({ page }) => {
    await open(page, '/orders');
    await page.getByRole('row').filter({ hasText: 'ORD-000345' }).getByRole('button', { name: 'Cobrar' }).click();
    const panel = await dialog(page, 'Cobrar ORD-000345');
    await panel.getByLabel('Efectivo recibido').fill('300');
    await snapDialog(page, 'pedidos-registrar-pago');
  });

  test('Pedidos por canal', async ({ page }) => {
    await open(page, '/orders');
    await page.getByLabel('Canal').selectOption({ label: 'WhatsApp' });
    await expect(page.getByRole('button', { name: 'ORD-000348', exact: true })).toBeHidden();
    await expect(page.getByRole('button', { name: 'ORD-000347', exact: true })).toBeVisible();
    await snapPage(page, 'pedidos-filtro-canal');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('Pedidos escritorio oscuro', async ({ page }) => {
      await open(page, '/orders');
      await snapPage(page, 'pedidos-escritorio-oscuro');
    });

    test('Detalle de una venta oscuro', async ({ page }) => {
      await open(page, '/orders');
      await openOrder(page, 'ORD-000345');
      await snapDialog(page, 'pedidos-detalle-venta-oscuro');
    });
  });
});

test.describe('Orden detallada', () => {
  test('Orden detallada escritorio', async ({ page }) => {
    await open(page, '/orders/cart');
    await snapPage(page, 'orden-detallada-escritorio');
  });

  test('Orden detallada teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/orders/cart');
    await snapPage(page, 'orden-detallada-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('Orden detallada escritorio oscuro', async ({ page }) => {
      await open(page, '/orders/cart');
      await snapPage(page, 'orden-detallada-escritorio-oscuro');
    });
  });
});

test.describe('Cocina', () => {
  test('Cocina escritorio', async ({ page }) => {
    await open(page, '/kitchen');
    await snapPage(page, 'cocina-escritorio');
  });

  test('Cocina teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/kitchen');
    await snapPage(page, 'cocina-telefono');
  });

  test.describe('Modo enfocado', () => {
    test.beforeEach(async ({ page }) => startFocused(page, 'kitchen'));

    // The kitchen in focus mode is always dark, whatever the mode of the rest of the app.
    test('Cocina enfocada escritorio', async ({ page }) => {
      await open(page, '/kitchen');
      await expect(page.getByRole('button', { name: 'Salir del modo enfocado' })).toBeVisible();
      await snapPage(page, 'cocina-enfocada-escritorio');
    });

    test('Cocina enfocada tableta', async ({ page }) => {
      await page.setViewportSize(SIZES.tablet);
      await open(page, '/kitchen');
      await expect(page.getByRole('button', { name: 'Salir del modo enfocado' })).toBeVisible();
      await snapPage(page, 'cocina-enfocada-tableta');
    });
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('Cocina escritorio oscuro', async ({ page }) => {
      await open(page, '/kitchen');
      await snapPage(page, 'cocina-escritorio-oscuro');
    });
  });
});

test.describe('Mis pedidos', () => {
  test.use({ profile: CUSTOMER_PROFILE });

  test('Mis pedidos escritorio', async ({ page }) => {
    await open(page, '/mis-pedidos');
    await snapPage(page, 'mis-pedidos-escritorio');
  });

  test('Mis pedidos teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/mis-pedidos');
    await snapPage(page, 'mis-pedidos-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('Mis pedidos escritorio oscuro', async ({ page }) => {
      await open(page, '/mis-pedidos');
      await snapPage(page, 'mis-pedidos-escritorio-oscuro');
    });
  });
});
