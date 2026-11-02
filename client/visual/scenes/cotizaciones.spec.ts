import { expect, test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';
import {
  ACCEPTED_QUOTE_ID,
  CONNECTED_QUOTE_ID,
  CONVERTED_QUOTE_ID,
  DRAFT_QUOTE_ID,
  PLUMBER_QUOTES,
  PUBLIC_QUOTE_ROUTES,
  QUOTES_ROUTES,
  QUOTES_WITH_WHATSAPP_ROUTES,
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

// Fase 29E: the accepted quote becomes the job, a service order with what was paid for the visit as its payment; then it
// says which order it became.
test.describe('Cotización aceptada', () => {
  test.use({ prepareData: PLUMBER_QUOTES, extraRoutes: QUOTES_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`cotización aceptada ${look.suffix}`, async ({ page }) => {
        await open(page, '/quotes/5d2a8f61-3c7e-4b9a-8e14-6f0b2c9d7a35');
        await expect(page.getByRole('button', { name: 'Convertir en orden de servicio' })).toBeVisible();
        await snap(page, `cotizacion-aceptada-${look.suffix}`);
      });
    });
  }

  test('convertir en orden de servicio', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/5d2a8f61-3c7e-4b9a-8e14-6f0b2c9d7a35');
    await page.getByRole('button', { name: 'Convertir en orden de servicio' }).click();
    await expect(page.getByRole('dialog', { name: 'Convertir en orden de servicio' })).toBeVisible();
    await expect(page.getByLabel('Para cuándo es el trabajo')).toBeVisible();
    await snap(page, 'cotizacion-convertir-escritorio');
  });

  test('cotización convertida', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/8b4e1d73-9a2c-4f6e-b5d8-2c7a0e9f4b16');
    await expect(page.getByRole('link', { name: 'Ver la orden' })).toBeVisible();
    await snap(page, 'cotizacion-convertida-escritorio');
  });
});

// Fase 29D: the business connected its WhatsApp, so the quote goes from TienditApp by WhatsApp and by email, each send with
// where it is; and the owner picks the template its quotes go with.
test.describe('Cotizaciones desde TienditApp', () => {
  test.use({ prepareData: PLUMBER_QUOTES, extraRoutes: QUOTES_WITH_WHATSAPP_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`cotización con sus envíos ${look.suffix}`, async ({ page }) => {
        await open(page, '/quotes/3e7b9c14-8f2a-4d6b-b1e5-7a0c4f9d2e63');
        await expect(page.getByRole('button', { name: 'Enviar por WhatsApp' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Envíos de esta versión' })).toBeVisible();
        await snap(page, `cotizacion-envios-${look.suffix}`);
      });
    });
  }

  test('enviar cotización por correo', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/3e7b9c14-8f2a-4d6b-b1e5-7a0c4f9d2e63');
    await page.getByRole('button', { name: 'Enviar por correo' }).click();
    await expect(page.getByRole('dialog', { name: 'Enviar por correo' })).toBeVisible();
    await expect(page.getByLabel('Correo del cliente')).toHaveValue('administracion@amsterdam210.mx');
    await snap(page, 'cotizacion-enviar-correo-escritorio');
  });

  test('plantilla de WhatsApp de las cotizaciones', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes');
    await expect(page.getByRole('heading', { name: 'Mensaje de WhatsApp' })).toBeVisible();
    await expect(page.getByLabel('Plantilla')).toHaveValue('tpl-cotizacion');
    await snap(page, 'cotizaciones-plantilla-whatsapp-escritorio');
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
  expect([DRAFT_QUOTE_ID, SENT_QUOTE_ID, CONNECTED_QUOTE_ID, ACCEPTED_QUOTE_ID, CONVERTED_QUOTE_ID]).toEqual([
    '7a3f1c52-2d4b-4e6a-9c1f-0b8e5d2a7c41', '0c9d2e71-5b3a-4f8e-a6d4-3e1b7c9f2a58', '3e7b9c14-8f2a-4d6b-b1e5-7a0c4f9d2e63',
    '5d2a8f61-3c7e-4b9a-8e14-6f0b2c9d7a35', '8b4e1d73-9a2c-4f6e-b5d8-2c7a0e9f4b16',
  ]);
});
