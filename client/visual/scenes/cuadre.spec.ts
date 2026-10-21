import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { CON_DIFERENCIAS, RECONCILIATION_ROUTES } from './cuadre.mocks';

// Inventario › Historial por insumo › Revisar que cuadre (Fase 27C): the two stocks of Café La Esquina that are not what
// their movements add up to, why one of them does not, and confirming the reconciliation (cuadre.mocks).

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

async function openReview(page: Page) {
  await open(page, '/inventory');
  await page.getByRole('tab', { name: 'Historial por insumo' }).click();
  await page.getByRole('button', { name: 'Revisar que cuadre' }).click();
  await expect(page.getByText(/Se revisaron 24 existencias: 2 no cuadran con su historial\./)).toBeVisible();
}

for (const look of LOOKS) {
  test.describe(`Cuadre ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode, prepareData: CON_DIFERENCIAS, extraRoutes: RECONCILIATION_ROUTES });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`revisar que cuadre ${look.suffix}`, async ({ page }) => {
      await openReview(page);
      await expect(page.getByRole('row', { name: /Leche entera/ })).toBeVisible();
      await snap(page, `revisar-que-cuadre-${look.suffix}`);
    });

    if (look.suffix !== 'escritorio-oscuro') {
      test(`por que no cuadra ${look.suffix}`, async ({ page }) => {
        await openReview(page);
        await page.getByRole('button', { name: 'Por qué no cuadra Café en grano Chiapas en Sucursal Roma Norte' }).click();
        const modal = await dialog(page, 'Por qué no cuadra');
        await expect(modal.getByText(/la existencia pasó de 10.25 kg a 10 kg sin un movimiento/)).toBeVisible();
        await snap(page, `por-que-no-cuadra-${look.suffix}`, { fullPage: false });
      });
    }

    if (look.suffix === 'escritorio') {
      test(`cuadrar ${look.suffix}`, async ({ page }) => {
        await openReview(page);
        await page.getByRole('button', { name: 'Cuadrar Leche entera en Sucursal Roma Norte' }).click();
        const modal = await dialog(page, 'Cuadrar el historial');
        await expect(modal.getByText(/La existencia no cambia/)).toBeVisible();
        await snap(page, `cuadrar-${look.suffix}`, { fullPage: false });
      });
    }
  });
}
