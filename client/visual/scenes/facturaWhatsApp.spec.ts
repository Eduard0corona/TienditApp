import { expect, test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';
import { CANCELLED_CFDI_LINK, CFDI_LINK, FACTURA_WHATSAPP_ROUTES } from './facturaWhatsApp.mocks';

// The CFDI a business sent by WhatsApp (Fase 28M, ADR 0058), opened with the link of its message without an account: F-3
// with its XML and PDF, one whose cancellation was asked for, a link unknown or expired, and the page without a link.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.describe('Factura por WhatsApp', () => {
  test.use({ signedIn: false, extraRoutes: FACTURA_WHATSAPP_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`factura del enlace ${look.suffix}`, async ({ page }) => {
        await open(page, `/factura#${CFDI_LINK}`);
        await expect(page.getByRole('heading', { name: 'Factura F-3' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Descargar PDF de la factura F-3' })).toBeEnabled();
        await snap(page, `factura-enlace-${look.suffix}`);
      });
    });
  }

  test('factura del enlace cancelada', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/factura#${CANCELLED_CFDI_LINK}`);
    await expect(page.getByText(/Este CFDI ya no está vigente: el negocio pidió cancelarlo\./)).toBeVisible();
    await snap(page, 'factura-enlace-cancelada-telefono');
  });

  test('factura con un enlace vencido', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/factura#AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE');
    await expect(page.getByText(/No encontramos esa factura\. El enlace pudo haber vencido/)).toBeVisible();
    await snap(page, 'factura-enlace-vencido-telefono');
  });

  test('factura sin enlace', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/factura');
    await expect(page.getByRole('heading', { name: 'Abre el enlace de tu mensaje' })).toBeVisible();
    await snap(page, 'factura-sin-enlace-telefono');
  });
});
