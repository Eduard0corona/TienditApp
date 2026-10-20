import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { CON_SALDO_A_FAVOR } from './saldo-a-favor.mocks';
import { SELL_ROUTES } from './vender-pedidos.mocks';

// Saldo a favor (Fase 26D): what customers left in their favor in Clientes › Por cobrar, the account with its balance and
// its movements, leaving it, paying what they owe with it and giving it back, the change of a payment left in their favor,
// the customer card of whoever owes nothing but has a balance, paying with it in Vender and a refund to it in Pedidos.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/** Opens the account of a customer from Por cobrar and waits for its balance in their favor. */
async function openAccount(page: Page, name: string): Promise<Locator> {
  await page.getByRole('button', { name: `Ver cuenta de ${name}` }).click();
  const account = await dialog(page, 'Estado de cuenta');
  await expect(account.getByRole('heading', { name: 'Saldo a favor' })).toBeVisible();
  return account;
}

/** Opens a movement of the balance from the account of a customer. */
async function openMovement(page: Page, name: string, button: string, title: string): Promise<Locator> {
  const account = await openAccount(page, name);
  await account.getByRole('button', { name: button }).click();
  const panel = await dialog(page, title);
  await expect(panel.getByLabel('Monto')).toBeVisible();
  return panel;
}

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Taps a product tile of Vender, as the cashier does. */
async function tapProduct(page: Page, name: string, times = 1) {
  const tile = page.getByRole('button', { name: new RegExp(`^Agregar ${escapeRegExp(name)},`) });
  for (let tap = 0; tap < times; tap += 1) await tile.click();
}

/** An americano and two conchas ($71.00) for María Fernanda, and Cobrar: she has $150.00 in her favor. */
async function chargeToMafer(page: Page): Promise<Locator> {
  await tapProduct(page, 'Café americano');
  await tapProduct(page, 'Concha de vainilla', 2);
  await page.getByRole('button', { name: /^Cliente.* notas \(opcional\)$/ }).click();
  await page.getByLabel('Teléfono').fill('55 4123 8890');
  await page.getByRole('button', { name: /^Cobrar \$/ }).click();
  const panel = await dialog(page, 'Cobrar');
  await expect(panel.getByText(/tiene.*a favor\./)).toContainText('$150.00');
  return panel;
}

test.use({ prepareData: CON_SALDO_A_FAVOR });

// ------------------------------------------------------------------ Por cobrar y la cuenta

test.describe('Por cobrar con saldo a favor', () => {
  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`por cobrar con saldo a favor ${look.suffix}`, async ({ page }) => {
        await open(page, '/customers/receivables');
        await expect(page.getByText(/a favor de tus clientes\./)).toContainText('$950.00');
        await expect(page.getByRole('row').filter({ hasText: 'Oficinas Grupo Altavista' }).getByText('A favor')).toBeVisible();
        await snap(page, `por-cobrar-saldo-a-favor-${look.suffix}`);
      });
    });
  }

  test('estado de cuenta con saldo a favor', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'Oficinas Grupo Altavista');
    await expect(account.getByText('Anticipo del pedido del viernes')).toBeVisible();
    await expect(account.getByRole('button', { name: 'Usar saldo a favor' })).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-saldo-a-favor-escritorio');
  });

  test('estado de cuenta con saldo a favor en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'María Fernanda López');
    await expect(account.getByText('Lo pidió para el camión')).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-saldo-a-favor-telefono');
  });

  test('dejar a favor', async ({ page }) => {
    await open(page, '/customers/receivables');
    const panel = await openMovement(page, 'María Fernanda López', 'Dejar a favor', 'Dejar a favor');
    // A deposit needs an amount: empty, nothing is left.
    await expect(panel.getByRole('button', { name: 'Dejar a favor $0.00' })).toBeDisabled();
    await panel.getByLabel('Monto').fill('300');
    await panel.getByRole('radio', { name: 'Transferencia' }).click();
    await panel.getByLabel('Nota (opcional)').fill('Anticipo de la rosca');
    await expect(panel.getByRole('button', { name: 'Dejar a favor $300.00' })).toBeEnabled();
    await snapScreen(page, 'por-cobrar-dejar-a-favor-escritorio');
  });

  test('dejar a favor en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers/receivables');
    const panel = await openMovement(page, 'Oficinas Grupo Altavista', 'Dejar a favor', 'Dejar a favor');
    await panel.getByLabel('Monto').fill('250');
    await snapScreen(page, 'por-cobrar-dejar-a-favor-telefono');
  });

  test('dejado a favor', async ({ page }) => {
    await open(page, '/customers/receivables');
    const panel = await openMovement(page, 'María Fernanda López', 'Dejar a favor', 'Dejar a favor');
    await panel.getByLabel('Monto').fill('300');
    await panel.getByRole('button', { name: 'Dejar a favor $300.00' }).click();
    const account = await dialog(page, 'Estado de cuenta');
    await expect(account.getByText('$450.00')).toBeVisible();
    await snapScreen(page, 'por-cobrar-dejado-a-favor');
  });

  test('usar saldo a favor', async ({ page }) => {
    await open(page, '/customers/receivables');
    const panel = await openMovement(page, 'Oficinas Grupo Altavista', 'Usar saldo a favor', 'Usar saldo a favor');
    // Empty takes what they have or what they owe, whichever is less; no method: the money entered when they left it.
    await expect(panel.getByRole('button', { name: 'Usar $500.00' })).toBeEnabled();
    await expect(panel.getByRole('radiogroup')).toHaveCount(0);
    await snapScreen(page, 'por-cobrar-usar-saldo-a-favor');
  });

  test('devolver saldo a favor', async ({ page }) => {
    await open(page, '/customers/receivables');
    const panel = await openMovement(page, 'María Fernanda López', 'Devolver saldo', 'Devolver saldo a favor');
    // Money that leaves: the amount is written, never all of it with one click.
    await expect(panel.getByRole('button', { name: 'Devolver $0.00' })).toBeDisabled();
    await panel.getByLabel('Monto').fill('150');
    await expect(panel.getByRole('button', { name: 'Devolver $150.00' })).toBeEnabled();
    await snapScreen(page, 'por-cobrar-devolver-saldo-a-favor');
  });

  test('abonar con el cambio a su favor', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'María Fernanda López');
    await account.getByRole('button', { name: 'Abonar' }).click();
    const paying = await dialog(page, 'Abonar a la cuenta');
    await paying.getByLabel('Efectivo recibido').fill('500');
    await paying.getByLabel('Dejar el cambio a su favor').check();
    await expect(paying.getByText('Queda a su favor')).toBeVisible();
    await snapScreen(page, 'por-cobrar-abonar-cambio-a-favor');
  });
});

// ------------------------------------------------------------------ Clientes

test.describe('Clientes con saldo a favor', () => {
  test('ficha de quien tiene saldo a favor', async ({ page }) => {
    await open(page, '/customers');
    await page.getByRole('button', { name: 'Ficha de Sofía Ramírez Cruz' }).click();
    const card = await dialog(page, 'Ficha del cliente');
    await expect(card.getByText('A favor')).toBeVisible();
    await snapScreen(page, 'clientes-ficha-saldo-a-favor');
  });

  test('cuenta de quien no debe y tiene saldo a favor', async ({ page }) => {
    await open(page, '/customers');
    await page.getByRole('button', { name: 'Ficha de Sofía Ramírez Cruz' }).click();
    await (await dialog(page, 'Ficha del cliente')).getByRole('link', { name: 'Ver cuenta' }).click();
    const account = await dialog(page, 'Estado de cuenta');
    await expect(account.getByText('Anticipo del pastel de cumpleaños')).toBeVisible();
    await expect(account.getByRole('button', { name: 'Usar saldo a favor' })).toHaveCount(0);
    await snapScreen(page, 'clientes-cuenta-solo-saldo-a-favor');
  });
});

// ------------------------------------------------------------------ Vender y Pedidos

test.describe('Vender y Pedidos con saldo a favor', () => {
  test.use({ extraRoutes: SELL_ROUTES });

  test('cobrar con saldo a favor', async ({ page }) => {
    await open(page, '/sell');
    const panel = await chargeToMafer(page);
    await panel.getByRole('radio', { name: /Saldo a favor/ }).click();
    await expect(panel.getByLabel('Monto')).toHaveAttribute('placeholder', '71.00');
    await expect(panel.getByRole('button', { name: 'Cobrar $71.00' })).toBeEnabled();
    await snapScreen(page, 'vender-cobrar-saldo-a-favor');
  });

  test('cobrar con saldo a favor en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/sell');
    await tapProduct(page, 'Café americano');
    await tapProduct(page, 'Concha de vainilla', 2);
    await page.getByRole('button', { name: /artículos · / }).click();
    const ticket = await dialog(page, 'Ticket');
    await ticket.getByRole('button', { name: /^Cliente.* notas \(opcional\)$/ }).click();
    await ticket.getByLabel('Teléfono').fill('55 4123 8890');
    await ticket.getByRole('button', { name: /^Cobrar \$/ }).click();
    const panel = await dialog(page, 'Cobrar');
    await panel.getByRole('radio', { name: /Saldo a favor/ }).click();
    await panel.getByLabel('Monto').fill('50');
    await panel.getByRole('button', { name: 'Agregar otro pago' }).click();
    await expect(panel.getByText(/tiene.*a favor\./)).toContainText('$100.00');
    await snapScreen(page, 'vender-cobrar-saldo-a-favor-telefono');
  });

  test('venta cobrada con saldo a favor', async ({ page }) => {
    await open(page, '/sell');
    const panel = await chargeToMafer(page);
    await panel.getByRole('radio', { name: /Saldo a favor/ }).click();
    await panel.getByRole('button', { name: 'Cobrar $71.00' }).click();
    await dialog(page, 'Venta ORD-000351 cobrada');
    await expect(page.getByRole('dialog')).toHaveCount(1);
    await snapScreen(page, 'vender-venta-saldo-a-favor');
  });

  test('reembolsar a saldo a favor', async ({ page }) => {
    await open(page, '/orders');
    const row = page.getByRole('row').filter({ hasText: 'ORD-000350' });
    await row.getByRole('button', { name: 'ORD-000350', exact: true }).click();
    const detail = await dialog(page, 'Pedido ORD-000350');
    await detail.getByRole('button', { name: 'Reembolsar' }).click();
    const refund = await dialog(page, 'Reembolsar ORD-000350');
    await refund.getByLabel('Se devuelve en').selectOption({ label: 'Saldo a favor' });
    await refund.getByLabel('Monto a devolver').fill('45');
    await refund.getByLabel('Motivo').fill('El croissant salió quemado');
    await expect(refund.getByText(/Queda a favor del cliente/)).toBeVisible();
    await snapScreen(page, 'pedidos-reembolsar-a-saldo-a-favor');
  });
});
