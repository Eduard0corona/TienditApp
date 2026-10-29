import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { FACTURA_GLOBAL_ROUTES } from './facturaGlobal.mocks';

// The global invoice (Fase 28B, ADR 0046): Reportes › Factura global (the list), the preview of yesterday's global
// invoice ready to stamp, one whose period has not ended, the detail of a stamped one and the dialog to cancel it; since the
// Fase 28I (ADR 0054), the detail of one with the credit notes of its sales' refunds, which go before cancelling it.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ extraRoutes: FACTURA_GLOBAL_ROUTES });

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

test.describe('Reportes › Factura global', () => {
  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`factura global lista ${look.suffix}`, async ({ page }) => {
        await open(page, '/reports/global-invoices');
        await expect(page.getByRole('link', { name: /^Ver la factura global F-58 timbrada/ })).toBeVisible();
        await snap(page, `factura-global-lista-${look.suffix}`);
      });

      test(`factura global nueva ${look.suffix}`, async ({ page }) => {
        await open(page, '/reports/global-invoices/new');
        await expect(page.getByRole('heading', { name: 'Diaria · 3 oct. 2026' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Timbrar factura global' })).toBeEnabled();
        await snap(page, `factura-global-nueva-${look.suffix}`);
      });

      test(`factura global detalle ${look.suffix}`, async ({ page }) => {
        await open(page, '/reports/global-invoices/5f0c8a3e-2d41-4b7a-9e6f-1c2b3a4d5e58');
        await expect(page.getByRole('heading', { name: 'Diaria · 3 oct. 2026' })).toBeVisible();
        await snap(page, `factura-global-detalle-${look.suffix}`);
      });
    });
  }

  test('factura global de un periodo sin terminar', async ({ page }) => {
    await open(page, '/reports/global-invoices/new');
    await expect(page.getByRole('heading', { name: 'Diaria · 3 oct. 2026' })).toBeVisible();
    await page.getByLabel('Periodicidad').selectOption({ label: 'Mensual' });
    await expect(page.getByText('El periodo termina el 31 de octubre de 2026: su factura global se emite a partir del día siguiente.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Timbrar factura global' })).toBeDisabled();
    await snap(page, 'factura-global-sin-terminar');
  });

  for (const look of LOOKS.slice(0, 2)) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`factura global con notas de crédito ${look.suffix}`, async ({ page }) => {
        await open(page, '/reports/global-invoices/7a2d4c6e-8b1f-4e3a-9c5d-2f4e6a8b0c71');
        await expect(page.getByRole('heading', { name: 'Notas de crédito (3)' })).toBeVisible();
        await expect(page.getByText(/^Para cancelarla, cancela primero sus notas de crédito vigentes \(NC-6, NC-7\)/)).toBeVisible();
        await snap(page, `factura-global-notas-de-credito-${look.suffix}`);
      });
    });
  }

  test('cancelar factura global', async ({ page }) => {
    await open(page, '/reports/global-invoices/5f0c8a3e-2d41-4b7a-9e6f-1c2b3a4d5e58');
    await page.getByRole('button', { name: 'Cancelar factura global' }).click();
    const cancel = await dialog(page, 'Cancelar la factura global F-58');
    await expect(cancel.getByLabel('04 · Un cliente pidió la factura de una de sus ventas')).toBeChecked();
    await snapScreen(page, 'factura-global-cancelar');
  });
});
