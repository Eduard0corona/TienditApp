import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { CON_PLAZO } from './condonar.mocks';
import { SELL_ROUTES } from './vender-pedidos.mocks';

// Condonar y plazo del fiado (Fase 26B): Clientes › Por cobrar with a term of 7 days, what is overdue and «Solo vencidos»,
// the term of the fiado, the account with the due day of each order and what was forgiven, «Condonar» and «Deshacer»,
// what is overdue when charging in Vender and what was forgiven of a sale in Pedidos; and (Fase 28J) a discount after the
// sale as the reason, with what it says.

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

/** Opens «Condonar» in the account of a customer. */
async function openWriteOff(page: Page, name: string): Promise<Locator> {
  const account = await openAccount(page, name);
  await account.getByRole('button', { name: 'Condonar' }).click();
  const panel = await dialog(page, 'Condonar');
  await expect(panel.getByLabel('¿Qué condonas?')).toBeVisible();
  return panel;
}

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Taps a product tile of Vender, as the cashier does. */
async function tapProduct(page: Page, name: string, times = 1) {
  const tile = page.getByRole('button', { name: new RegExp(`^Agregar ${escapeRegExp(name)},`) });
  for (let tap = 0; tap < times; tap += 1) await tile.click();
}

test.use({ prepareData: CON_PLAZO });

// ------------------------------------------------------------------ Por cobrar

test.describe('Por cobrar con plazo', () => {
  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`por cobrar con plazo ${look.suffix}`, async ({ page }) => {
        await open(page, '/customers/receivables');
        await expect(page.getByText('Plazo de 7 días')).toBeVisible();
        await expect(page.getByRole('row').filter({ hasText: 'Oficinas Grupo Altavista' }).getByText('Vencido')).toBeVisible();
        await snap(page, `por-cobrar-con-plazo-${look.suffix}`);
      });
    });
  }

  test('solo vencidos', async ({ page }) => {
    await open(page, '/customers/receivables');
    await expect(page.getByText('María Fernanda López')).toBeVisible();
    await page.getByLabel('Solo vencidos').check();
    await expect(page.getByText('María Fernanda López')).toBeHidden();
    await expect(page.getByText('Roberto Sánchez Vega')).toBeVisible();
    await snap(page, 'por-cobrar-solo-vencidos-escritorio');
  });

  test('plazo del fiado', async ({ page }) => {
    await open(page, '/customers/receivables');
    await page.getByRole('button', { name: 'Cambiar plazo' }).click();
    const term = await dialog(page, 'Plazo del fiado');
    await expect(term.getByLabel('Te pagan en')).toHaveValue('7');
    await snapScreen(page, 'por-cobrar-plazo-del-fiado');
  });

  test('estado de cuenta con vencido y condonado', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'Oficinas Grupo Altavista');
    await expect(account.getByText(/^Venció el /)).toBeVisible();
    await expect(account.getByRole('heading', { name: 'Lo condonado' })).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-vencido-escritorio');
  });

  test('estado de cuenta con vencido y condonado en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'María Fernanda López');
    await expect(account.getByText('Se cobró un agua de jamaica de más')).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-condonado-telefono');
  });

  test('condonar', async ({ page }) => {
    await open(page, '/customers/receivables');
    const panel = await openWriteOff(page, 'Roberto Sánchez Vega');
    // The amount starts empty: a slip never forgives the whole account.
    await expect(panel.getByRole('button', { name: 'Condonar $0.00' })).toBeDisabled();
    await panel.getByLabel('Cuánto').fill('360');
    await expect(panel.getByRole('button', { name: 'Condonar $360.00' })).toBeEnabled();
    await snapScreen(page, 'por-cobrar-condonar-escritorio');
  });

  test('condonar por otro motivo en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers/receivables');
    const panel = await openWriteOff(page, 'Oficinas Grupo Altavista');
    await panel.getByLabel('Cuánto').fill('150');
    await panel.getByLabel('Motivo').selectOption({ label: 'Otro' });
    await panel.getByLabel(/^Nota/).fill('Pagaron con un servicio de impresión');
    await expect(panel.getByRole('button', { name: 'Condonar $150.00' })).toBeEnabled();
    await snapScreen(page, 'por-cobrar-condonar-telefono');
  });

  test('condonar con un descuento', async ({ page }) => {
    await open(page, '/customers/receivables');
    const panel = await openWriteOff(page, 'Roberto Sánchez Vega');
    await panel.getByLabel('Cuánto').fill('50');
    await panel.getByLabel('Motivo').selectOption({ label: 'Descuento o bonificación' });
    // A discount is of one sale: the whole account is not offered for it.
    await expect(panel.getByText('Un descuento es de una venta: elige su pedido en «¿Qué condonas?».')).toBeVisible();
    await expect(panel.getByRole('button', { name: 'Condonar $50.00' })).toBeDisabled();
    await snapScreen(page, 'por-cobrar-condonar-con-descuento-escritorio');
  });

  test('condonado', async ({ page }) => {
    await open(page, '/customers/receivables');
    const panel = await openWriteOff(page, 'Roberto Sánchez Vega');
    await panel.getByLabel('Cuánto').fill('360');
    await panel.getByRole('button', { name: 'Condonar $360.00' }).click();
    const account = await dialog(page, 'Estado de cuenta');
    await expect(account.getByText('No debe nada.')).toBeVisible();
    await snapScreen(page, 'por-cobrar-condonado-escritorio');
  });

  test('deshacer lo condonado', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'Oficinas Grupo Altavista');
    await account.getByRole('button', { name: 'Deshacer lo condonado de ORD-000262' }).click();
    const confirm = await dialog(page, 'Deshacer lo condonado');
    await expect(confirm.getByText(/ORD-000262 vuelve a deber \$100\.00/)).toBeVisible();
    await snapScreen(page, 'por-cobrar-deshacer-lo-condonado');
  });

  test('lo condonado deshecho', async ({ page }) => {
    await open(page, '/customers/receivables');
    const account = await openAccount(page, 'Oficinas Grupo Altavista');
    await account.getByRole('button', { name: 'Deshacer lo condonado de ORD-000262' }).click();
    await (await dialog(page, 'Deshacer lo condonado')).getByRole('button', { name: 'Deshacer' }).click();
    await expect(account.getByText(/^Deshecho el .*: lo vuelve a deber\.$/)).toBeVisible();
    await expect(account.getByRole('button', { name: /^Deshacer lo condonado/ })).toBeHidden();
    await snapScreen(page, 'por-cobrar-condonado-deshecho');
  });
});

// ------------------------------------------------------------------ Vender y Pedidos

test.describe('Vender y Pedidos con plazo', () => {
  test.use({ extraRoutes: SELL_ROUTES });

  test('cobrar a quien tiene algo vencido', async ({ page }) => {
    await open(page, '/sell');
    await tapProduct(page, 'Café americano');
    await tapProduct(page, 'Concha de vainilla', 2);
    await page.getByRole('button', { name: /^Cliente.* notas \(opcional\)$/ }).click();
    await page.getByLabel('Teléfono').fill('55 5208 9100');
    await page.getByRole('button', { name: /^Cobrar \$/ }).click();
    const panel = await dialog(page, 'Cobrar');
    await expect(panel.getByText(/vencido\./)).toBeVisible();
    await snapScreen(page, 'vender-cobrar-con-vencido');
  });

  test('pedido con lo condonado', async ({ page }) => {
    await open(page, '/orders');
    const row = page.getByRole('row').filter({ hasText: 'ORD-000349' });
    await expect(row.getByText('Condonado $20.00')).toBeVisible();
    await row.getByRole('button', { name: 'ORD-000349', exact: true }).click();
    const detail = await dialog(page, 'Pedido ORD-000349');
    await expect(detail.getByRole('heading', { name: 'Condonado' })).toBeVisible();
    await detail.getByRole('heading', { name: 'Condonado' }).scrollIntoViewIfNeeded();
    await snapScreen(page, 'pedidos-condonado-escritorio');
  });
});
