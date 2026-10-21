import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { CON_TRANSFERENCIAS, TODAY_TRANSFER, TRANSFER_ROUTES, YESTERDAY_TRANSFER } from './transferencias.mocks';

// Inventario › Transferencias (Fase 27B): the list of transfers, one with what each location had before and after, and
// the dialog that makes one. Café La Esquina brought cups and chocolate from Bodega Central this morning and took coffee
// and milk to Coyoacán yesterday (transferencias.mocks).

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

async function openTransfers(page: Page) {
  await open(page, '/inventory');
  await page.getByRole('tab', { name: 'Transferencias' }).click();
  await expect(page.getByRole('button', { name: `Ver la transferencia ${TODAY_TRANSFER}` })).toBeVisible();
}

for (const look of LOOKS) {
  test.describe(`Transferencias ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode, prepareData: CON_TRANSFERENCIAS, extraRoutes: TRANSFER_ROUTES });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`transferencias ${look.suffix}`, async ({ page }) => {
      await openTransfers(page);
      await expect(page.getByRole('button', { name: `Ver la transferencia ${YESTERDAY_TRANSFER}` })).toBeVisible();
      await snap(page, `transferencias-${look.suffix}`);
    });

    test(`transferencia ${look.suffix}`, async ({ page }) => {
      await openTransfers(page);
      await page.getByRole('button', { name: `Ver la transferencia ${YESTERDAY_TRANSFER}` }).click();
      await expect(page.getByRole('heading', { name: new RegExp(YESTERDAY_TRANSFER) })).toBeVisible();
      await expect(page.getByRole('cell', { name: 'Café en grano Chiapas' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Ver entrada INV-20261003-0004' })).toBeVisible();
      await snap(page, `transferencia-${look.suffix}`);
    });

    if (look.suffix !== 'escritorio-oscuro') {
      test(`nueva transferencia ${look.suffix}`, async ({ page }) => {
        await openTransfers(page);
        await page.getByRole('button', { name: 'Nueva transferencia' }).click();
        const modal = await dialog(page, 'Nueva transferencia');
        await modal.getByLabel('Destino').selectOption({ label: 'Sucursal Coyoacán' });
        await modal.getByRole('button', { name: 'Agregar Café en grano Chiapas' }).click();
        await modal.getByRole('button', { name: 'Agregar Leche entera' }).click();
        await modal.getByLabel('Cantidad de Café en grano Chiapas').fill('2.5');
        await modal.getByLabel('Cantidad de Leche entera').fill('6');
        await snap(page, `nueva-transferencia-${look.suffix}`, { fullPage: false });
      });
    }
  });
}
