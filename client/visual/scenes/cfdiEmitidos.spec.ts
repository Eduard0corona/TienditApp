import { expect, test } from '../fixtures';
import { SIZES, dialog, open, settle, snap } from '../helpers';
import { CFDI_EMITIDOS_ROUTES, CFDI_POR_REVISAR } from './cfdiEmitidos.mocks';

// Reportes › CFDI emitidos (Fase 28H, ADR 0052): October's CFDI with what they add up to, on a desktop, a phone and in
// dark mode, and a month without any. Since the Fase 28K (ADR 0056), the CFDI to check in the SAT's portal above the month,
// on a desktop and a phone, and the folio fiscal one of them asks for.

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
        await expect(page.getByRole('button', { name: 'Descargar XML de la factura F-60' })).toBeVisible();
        await snap(page, `cfdi-emitidos-${look.suffix}`);
      });
    });
  }

  test.describe('por revisar en el portal del SAT', () => {
    test.use({ prepareData: CFDI_POR_REVISAR });

    for (const look of LOOKS.slice(0, 2)) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`cfdi por revisar en el portal del sat ${look.suffix}`, async ({ page }) => {
          await open(page, '/reports/issued-cfdis');
          const toReview = page.getByRole('region', { name: 'Por revisar en el portal del SAT' });
          await expect(toReview.getByRole('button', { name: 'Ya se timbró la factura F-52' })).toBeVisible();
          await expect(page.getByRole('button', { name: 'Descargar XML de la factura F-60' })).toBeVisible();
          await snap(page, `cfdi-por-revisar-${look.suffix}`);
        });
      });
    }

    test('cfdi por revisar: su folio fiscal', async ({ page }) => {
      await open(page, '/reports/issued-cfdis');
      await page.getByRole('button', { name: 'Ya se timbró la factura F-52' }).click();
      const stamped = await dialog(page, 'Factura F-52 ya se timbró');
      await stamped.getByLabel('Folio fiscal (UUID)').fill('5F0B7C1E-2A3D-4E5F');
      await stamped.getByRole('button', { name: 'Guardar el timbre' }).click();
      await expect(stamped.getByText('Escribe el folio fiscal (UUID) completo, como aparece en el portal del SAT.')).toBeVisible();
      await settle(page);
      await snap(page, 'cfdi-por-revisar-folio-fiscal');
    });
  });

  test('cfdi emitidos de un mes sin CFDI', async ({ page }) => {
    await open(page, '/reports/issued-cfdis');
    await page.getByRole('button', { name: 'Mes anterior (septiembre de 2026)' }).click();
    await expect(page.getByRole('heading', { name: 'Septiembre de 2026' })).toBeVisible();
    await expect(page.getByText('No hay CFDI en este mes.')).toBeVisible();
    await settle(page);
    await snap(page, 'cfdi-emitidos-sin-cfdi');
  });
});
