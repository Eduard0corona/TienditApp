import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { RETENCIONES_ROUTES, UPLOAD_FILES } from './retenciones.mocks';

// Reportes › Liquidaciones › Retenciones (Fase 22J): month by month, the ISR and IVA of Uber Eats's CFDI de retenciones
// against what TienditApp calculated, at the three base looks; then the dialog to upload several XML with what came of
// each, and the one to remove a CFDI. A dialog is taken at the size of the screen: it is fixed over the page.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

const PAGE = '/reports/settlements/withholdings?channel=4&year=2026';

for (const look of LOOKS) {
  test.describe(`Retenciones ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode, extraRoutes: RETENCIONES_ROUTES });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`retenciones ${look.suffix}`, async ({ page }) => {
      // July and August add up, September withheld more ISR, October has no CFDI yet; a removed one stays in the list.
      await open(page, PAGE);
      await expect(page.getByRole('table', { name: 'Retenciones de Uber Eats por mes de 2026' })).toBeVisible();
      await expect(page.getByText('Era de Rappi')).toBeVisible();
      await snap(page, `retenciones-${look.suffix}`);
    });
  });
}

test.describe('Retenciones', () => {
  test.use({ extraRoutes: RETENCIONES_ROUTES });

  test('retenciones, subir CFDI', async ({ page }) => {
    await open(page, PAGE);
    await page.getByRole('button', { name: 'Subir CFDI de retenciones' }).click();
    const uploading = await dialog(page, 'Subir CFDI de retenciones');
    await uploading.getByLabel('Archivos XML').setInputFiles(UPLOAD_FILES);
    await uploading.getByRole('button', { name: 'Subir' }).click();
    await expect(uploading.getByText('Se registraron 1 de 2. Revisa los que no entraron.')).toBeVisible();
    await snap(page, 'retenciones-subir-resultado', { fullPage: false });
  });

  test('retenciones, quitar CFDI', async ({ page }) => {
    await open(page, PAGE);
    await page.getByRole('button', { name: /^Quitar el CFDI de septiembre de 2026/ }).click();
    const removing = await dialog(page, 'Quitar CFDI');
    await removing.getByLabel('Motivo').fill('Lo subí dos veces');
    await snap(page, 'retenciones-quitar-dialogo', { fullPage: false });
  });
});
