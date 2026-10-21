import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { LIQUIDACIONES_ROUTES, RAPPI_CLOSED_ID, STATEMENT_CSV, UBER_OPEN_ID } from './liquidaciones.mocks';

// Reportes › Liquidaciones: what each delivery platform owes and the settlements of its deposits. The list and an open
// settlement at the three base looks; then a new one, a closed one, the dialog to void it and the import of a statement
// (its columns and the comparison on the desktop and the phone, and the settlement once applied). A dialog is taken at the size
// of the screen (not the whole page): it is fixed over the page, and the shot shows it where the person sees it.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

for (const look of LOOKS) {
  test.describe(`Liquidaciones ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode, extraRoutes: LIQUIDACIONES_ROUTES });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`liquidaciones ${look.suffix}`, async ({ page }) => {
      // Uber Eats has one open; Rappi and DiDi Food are settled up to a week, and the list keeps a voided one as history.
      await open(page, '/reports/settlements');
      await expect(page.getByRole('link', { name: 'Continuar la liquidación abierta de Uber Eats', exact: true })).toBeVisible();
      await expect(page.getByRole('link', { name: 'Registrar depósito de DiDi Food', exact: true })).toBeVisible();
      await expect(page.getByRole('link', { name: /^Ver la liquidación anulada de Rappi del 14 al 20 sept?\.$/ })).toBeVisible();
      await snap(page, `liquidaciones-${look.suffix}`);
    });

    if (look.suffix !== 'escritorio-oscuro') {
      test(`liquidación abierta, importar estado de cuenta ${look.suffix}`, async ({ page }) => {
        await open(page, `/reports/settlements/${UBER_OPEN_ID}`);
        await page.getByRole('button', { name: 'Importar estado de cuenta' }).click();
        const importing = await dialog(page, 'Importar estado de cuenta');
        await importing.getByLabel('Archivo CSV').setInputFiles({
          name: 'uber-eats-semana-38.csv',
          mimeType: 'text/csv',
          buffer: Buffer.from(STATEMENT_CSV),
        });
        await expect(importing.getByLabel('Folio del pedido')).toHaveValue('1');
        await expect(importing.getByText(/^Línea 2: folio A7K2P/)).toBeVisible();
        await snap(page, `liquidacion-importar-columnas-${look.suffix}`, { fullPage: false });

        await importing.getByRole('button', { name: 'Comparar con mis pedidos' }).click();
        await expect(importing.getByText(/El estado de cuenta suma/)).toBeVisible();
        await snap(page, `liquidacion-importar-comparacion-${look.suffix}`, { fullPage: false });
      });
    }

    test(`liquidación abierta ${look.suffix}`, async ({ page }) => {
      // Its five orders and two adjustments leave $42.30 to explain; a pending order of before waits unchecked.
      await open(page, `/reports/settlements/${UBER_OPEN_ID}`);
      await expect(page.getByRole('checkbox', { name: 'Incluir el pedido J5W1B (ORD-000188)' })).not.toBeChecked();
      await expect(page.getByRole('checkbox', { name: 'Incluir el pedido A7K2P (ORD-000201)' })).toBeChecked();
      await expect(page.getByText(/Llegó \$42\.30 menos de lo esperado/)).toBeVisible();
      await snap(page, `liquidacion-abierta-${look.suffix}`);
    });
  });
}

test.describe('Liquidaciones', () => {
  test.use({ extraRoutes: LIQUIDACIONES_ROUTES });

  test('liquidación nueva', async ({ page }) => {
    // DiDi Food, last week: the orders of the week included, the one of before not; the deposit came $25.00 short.
    await open(page, '/reports/settlements/new?channel=5');
    await expect(page.getByRole('checkbox', { name: 'Incluir el pedido 88213 (ORD-000181)' })).not.toBeChecked();
    await expect(page.getByRole('checkbox', { name: 'Incluir el pedido 88262 (ORD-000241)' })).toBeChecked();
    await page.getByLabel('Lo que depositó').fill('650.50');
    await expect(page.getByText(/Llegó \$25\.00 menos de lo esperado/)).toBeVisible();
    await snap(page, 'liquidacion-nueva');
  });

  test('liquidación abierta, estado de cuenta aplicado', async ({ page }) => {
    // The statement's orders in, the one it does not bring (H8N3R) out, and its adjustments after those written by hand.
    await open(page, `/reports/settlements/${UBER_OPEN_ID}`);
    await page.getByRole('button', { name: 'Importar estado de cuenta' }).click();
    const importing = await dialog(page, 'Importar estado de cuenta');
    await importing.getByLabel('Archivo CSV').setInputFiles({ name: 'uber-eats-semana-38.csv', mimeType: 'text/csv', buffer: Buffer.from(STATEMENT_CSV) });
    await importing.getByRole('button', { name: 'Comparar con mis pedidos' }).click();
    await importing.getByRole('button', { name: 'Aplicar a la liquidación' }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('checkbox', { name: 'Incluir el pedido J5W1B (ORD-000188)' })).toBeChecked();
    await expect(page.getByRole('checkbox', { name: 'Incluir el pedido H8N3R (ORD-000247)' })).not.toBeChecked();
    await snap(page, 'liquidacion-estado-de-cuenta-aplicado');
  });

  test('liquidación cerrada', async ({ page }) => {
    // Closed, it only shows: its orders with what each brought and the adjustments that explain the rest.
    await open(page, `/reports/settlements/${RAPPI_CLOSED_ID}`);
    await expect(page.getByText('Cuadra')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Anular' })).toBeVisible();
    await snap(page, 'liquidacion-cerrada');
  });

  test('liquidación cerrada, anular', async ({ page }) => {
    await open(page, `/reports/settlements/${RAPPI_CLOSED_ID}`);
    await page.getByRole('button', { name: 'Anular' }).click();
    const voiding = await dialog(page, 'Anular liquidación');
    await voiding.getByLabel('Motivo').fill('El depósito era de otra semana');
    await snap(page, 'liquidacion-anular-dialogo', { fullPage: false });
  });
});
