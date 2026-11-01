import { expect, test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';
import {
  DRAFT_QUOTE_ID,
  PLUMBER_QUOTES,
  PUBLIC_QUOTE_ROUTES,
  QUOTES_ROUTES,
  QUOTE_LINK,
  REPLACED_QUOTE_LINK,
  SENT_QUOTE_ID,
} from './cotizaciones.mocks';

// The quotes (Fase 29C, ADR 0053): a plumber's list of open quotes, a draft with its lines, the quote of a visit sent with
// the visit taken off and its link to send by WhatsApp or by email, and the page the customer opens without an account:
// valid, replaced by a new version, or a link that no longer opens.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.describe('Cotizaciones', () => {
  test.use({ prepareData: PLUMBER_QUOTES, extraRoutes: QUOTES_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`cotizaciones abiertas ${look.suffix}`, async ({ page }) => {
        await open(page, '/quotes');
        await expect(page.getByRole('link', { name: 'COT-000011 · versión 2', exact: true })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Nueva cotización' })).toBeVisible();
        await snap(page, `cotizaciones-${look.suffix}`);
      });

      test(`cotización en borrador ${look.suffix}`, async ({ page }) => {
        await open(page, '/quotes/7a3f1c52-2d4b-4e6a-9c1f-0b8e5d2a7c41');
        await expect(page.getByRole('button', { name: 'Terminar y obtener enlace' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Agregar renglón' })).toBeVisible();
        await snap(page, `cotizacion-borrador-${look.suffix}`);
      });
    });
  }

  test('cotización enviada', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/0c9d2e71-5b3a-4f8e-a6d4-3e1b7c9f2a58');
    await expect(page.getByRole('button', { name: 'Registrar respuesta' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Enviar por WhatsApp' })).toBeVisible();
    await expect(page.getByText('Visita (se descuenta)')).toBeVisible();
    await snap(page, 'cotizacion-enviada-escritorio');
  });
});

test.describe('Cotización del enlace', () => {
  test.use({ signedIn: false, extraRoutes: PUBLIC_QUOTE_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`cotización del enlace ${look.suffix}`, async ({ page }) => {
        await open(page, `/cotizacion#${QUOTE_LINK}`);
        await expect(page.getByRole('heading', { name: 'Cotización COT-000011 · versión 2' })).toBeVisible();
        await expect(page.getByRole('link', { name: 'Responder al negocio' })).toBeVisible();
        await snap(page, `cotizacion-enlace-${look.suffix}`);
      });
    });
  }

  test('cotización del enlace reemplazada', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cotizacion#${REPLACED_QUOTE_LINK}`);
    await expect(page.getByRole('link', { name: 'Ver la versión nueva' })).toBeVisible();
    await snap(page, 'cotizacion-enlace-reemplazada-telefono');
  });

  test('cotización sin enlace', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cotizacion');
    await expect(page.getByRole('heading', { name: 'Abre el enlace de tu mensaje' })).toBeVisible();
    await snap(page, 'cotizacion-sin-enlace-telefono');
  });

  test('cotización con un enlace vencido', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cotizacion#AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE');
    await expect(page.getByText(/Esta cotización no existe o su enlace ya venció/)).toBeVisible();
    await snap(page, 'cotizacion-enlace-vencido-telefono');
  });
});

// The ids are written out so the route coverage (coverage.spec.ts) reads them; the mocks answer the same ones.
test('the scenes open the quotes the mocks answer', () => {
  expect([DRAFT_QUOTE_ID, SENT_QUOTE_ID]).toEqual(['7a3f1c52-2d4b-4e6a-9c1f-0b8e5d2a7c41', '0c9d2e71-5b3a-4f8e-a6d4-3e1b7c9f2a58']);
});
