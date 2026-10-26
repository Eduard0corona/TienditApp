import { expect, test } from '../fixtures';
import { SIZES, open, settle, snap } from '../helpers';
import { CFDI_EMITIDOS_ROUTES } from './cfdiEmitidos.mocks';

// Reportes › CFDI emitidos (Fase 28H, ADR 0052): October's CFDI with what they add up to, on a desktop, a phone and in
// dark mode, and a month without any.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ extraRoutes: CFDI_EMITIDOS_ROUTES });

test.describe('Reportes › CFDI emitidos', () => {
  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`cfdi emitidos ${look.suffix}`, async ({ page }) => {
        await open(page, '/reports/issued-cfdis');
        await expect(page.getByRole('heading', { name: 'Octubre de 2026' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'XML de la factura F-60' })).toBeVisible();
        await snap(page, `cfdi-emitidos-${look.suffix}`);
      });
    });
  }

  test('cfdi emitidos de un mes sin CFDI', async ({ page }) => {
    await open(page, '/reports/issued-cfdis');
    await page.getByRole('button', { name: 'Mes anterior (septiembre de 2026)' }).click();
    await expect(page.getByRole('heading', { name: 'Septiembre de 2026' })).toBeVisible();
    await expect(page.getByText('No hay CFDI en este mes.')).toBeVisible();
    await settle(page);
    await snap(page, 'cfdi-emitidos-sin-cfdi');
  });
});
