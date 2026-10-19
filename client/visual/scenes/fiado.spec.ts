import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { CON_FIADO } from './fiado.mocks';
import { SELL_ROUTES } from './vender-pedidos.mocks';

// Fiado (Fase 26A): Clientes › Por cobrar with who owes Café La Esquina, the account of a customer and a payment to it,
// the limit from Clientes and the customer card, «Fiar» when charging in Vender and the sale on credit in Pedidos.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/** Opens the account of a customer from Por cobrar and waits for the orders they owe. */
async function openAccount(page: Page, name: string): Promise<Locator> {
  await page.getByRole('button', { name: `Ver cuenta de ${name}` }).click();
  const account = await dialog(page, 'Estado de cuenta');
  await expect(account.getByText('Lo que debe')).toBeVisible();
  return account;
}

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Taps a product tile of Vender, as the cashier does. */
async function tapProduct(page: Page, name: string, times = 1) {
  const tile = page.getByRole('button', { name: new RegExp(`^Agregar ${escapeRegExp(name)},`) });
  for (let tap = 0; tap < times; tap += 1) await tile.click();
}

/** An americano and two conchas ($71.00) for the customer with this phone, and Cobrar. */
async function chargeTo(page: Page, phone: string): Promise<Locator> {
  await tapProduct(page, 'Café americano');
  await tapProduct(page, 'Concha de vainilla', 2);
  await page.getByRole('button', { name: /^Cliente.* notas \(opcional\)$/ }).click();
  await page.getByLabel('Teléfono').fill(phone);
  await page.getByRole('button', { name: /^Cobrar \$/ }).click();
  const panel = await dialog(page, 'Cobrar');
  await expect(panel.getByText(/tiene fiado: le quedan/)).toBeVisible();
  return panel;
}

test.use({ prepareData: CON_FIADO });

// ------------------------------------------------------------------ Por cobrar

test.describe('Por cobrar', () => {
  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`por cobrar ${look.suffix}`, async ({ page }) => {
        await open(page, '/customers/receivables');
        await expect(page.getByText('Oficinas Grupo Altavista')).toBeVisible();
        await expect(page.getByText('Pasó su límite')).toBeVisible();
        await snap(page, `por-cobrar-${look.suffix}`);
      });
    });
  }

  test('estado de cuenta', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'María Fernanda López');
    await expect(account.getByText('ORD-000349')).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-escritorio');
  });

  test('estado de cuenta de quien pasó su límite en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'Roberto Sánchez Vega');
    await expect(account.getByText(/Debe más que su límite/)).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-telefono');
  });

  test('estado de cuenta sin ficha de cliente', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'Antonia Reyes');
    await expect(account.getByText('ORD-000294')).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-sin-ficha');
  });

  test('abonar a la cuenta', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'María Fernanda López');
    await account.getByRole('button', { name: 'Abonar' }).click();
    const paying = await dialog(page, 'Abonar a la cuenta');
    await paying.getByLabel('Efectivo recibido').fill('200');
    await expect(paying.getByRole('button', { name: 'Abonar $200.00' })).toBeEnabled();
    await snapScreen(page, 'por-cobrar-abonar');
  });

  test('abonar a la cuenta en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'María Fernanda López');
    await account.getByRole('button', { name: 'Abonar' }).click();
    const paying = await dialog(page, 'Abonar a la cuenta');
    await paying.getByLabel('Efectivo recibido').fill('500');
    await snapScreen(page, 'por-cobrar-abonar-telefono');
  });
});

// ------------------------------------------------------------------ Clientes

test.describe('Clientes con fiado', () => {
  test('clientes con su límite de fiado', async ({ page }) => {
    await open(page, '/customers');
    await expect(page.getByText('Fiado hasta $800.00')).toBeVisible();
    await snap(page, 'clientes-fiado-escritorio');
  });

  test('límite de fiado de un cliente', async ({ page }) => {
    await open(page, '/customers');
    await page.getByRole('button', { name: 'Fiado de María Fernanda López' }).click();
    const limit = await dialog(page, 'Fiado de María Fernanda López');
    await expect(limit.getByLabel('Le fías hasta')).toHaveValue('800');
    await snapScreen(page, 'clientes-fiado-limite');
  });

  test('límite de fiado de un cliente en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers');
    await page.getByRole('button', { name: 'Fiado de Juan Carlos Hernández' }).click();
    const limit = await dialog(page, 'Fiado de Juan Carlos Hernández');
    await limit.getByLabel('Le fías hasta').fill('300');
    await snapScreen(page, 'clientes-fiado-limite-telefono');
  });

  test('ficha del cliente con fiado', async ({ page }) => {
    await open(page, '/customers');
    await page.getByRole('button', { name: 'Ficha de María Fernanda López' }).click();
    const card = await dialog(page, 'Ficha del cliente');
    await expect(card.getByText('Fiado: le quedan $484.00 de $800.00')).toBeVisible();
    await snapScreen(page, 'clientes-ficha-fiado');
  });
});

// ------------------------------------------------------------------ Vender y Pedidos

test.describe('Vender fiado', () => {
  test.use({ extraRoutes: SELL_ROUTES });

  test('cobrar con fiado', async ({ page }) => {
    await open(page, '/sell');
    const panel = await chargeTo(page, '55 4123 8890');
    await expect(panel.getByRole('button', { name: 'Fiar $71.00' })).toBeEnabled();
    await snapScreen(page, 'vender-cobrar-fiado');
  });

  test('cobrar con fiado en el teléfono', async ({ page }) => {
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
    await panel.getByLabel('Efectivo recibido').fill('20');
    await expect(panel.getByRole('button', { name: 'Fiar $51.00' })).toBeEnabled();
    await snapScreen(page, 'vender-cobrar-fiado-telefono');
  });

  test('cobrar a quien pasó su límite', async ({ page }) => {
    await open(page, '/sell');
    const panel = await chargeTo(page, '55 9921 0087');
    await expect(panel.getByText(/cobra al menos \$71\.00/)).toBeVisible();
    await snapScreen(page, 'vender-cobrar-fiado-sin-credito');
  });

  test('venta fiada', async ({ page }) => {
    await open(page, '/sell');
    const panel = await chargeTo(page, '55 4123 8890');
    await panel.getByRole('button', { name: 'Fiar $71.00' }).click();
    await dialog(page, 'Venta ORD-000350 fiada');
    await expect(page.getByRole('dialog')).toHaveCount(1);
    await snapScreen(page, 'vender-venta-fiada');
  });

  test('pedidos con una venta fiada', async ({ page }) => {
    await open(page, '/orders');
    await expect(page.getByRole('row', { name: /ORD-000349/ }).getByText('Fiado', { exact: true })).toBeVisible();
    await snap(page, 'pedidos-fiado-escritorio');
  });
});
