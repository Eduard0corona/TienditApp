import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import {
  ACCEPTED_QUOTE_ID, ANSWERED_QUOTE_LINK, CONNECTED_QUOTE_ID, CONVERTED_QUOTE_ID, DEPOSIT_CHECKOUT_ID, DEPOSIT_QUOTE_LINK, DRAFT_QUOTE_ID,
  PAID_QUOTE_LINK, PLUMBER_QUOTES, PUBLIC_QUOTE_ROUTES, QUOTES_ROUTES, QUOTES_WITH_DEPOSIT_ROUTES, QUOTES_WITH_WHATSAPP_ROUTES, QUOTE_LINK,
  QUOTE_WITH_PROBLEM_ROUTES, REPLACED_QUOTE_LINK, SCHEDULED_QUOTE_ID, SENT_QUOTE_ID,
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
    const form = await dialog(page, 'Convertir en orden de servicio');
    await expect(form.getByLabel('Para cuándo es el trabajo')).toBeVisible();
    // A dialog fits the screen: its dark backdrop covers the screen, not the page.
    await snap(page, 'cotizacion-convertir-escritorio', { fullPage: false });
  });

  test('cotización convertida', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/8b4e1d73-9a2c-4f6e-b5d8-2c7a0e9f4b16');
    await expect(page.getByRole('link', { name: 'Ver la orden' })).toBeVisible();
    await snap(page, 'cotizacion-convertida-escritorio');
  });
});

// Fase 29F: the job of the order is agendado at the place of the visit, in one appointment or more, with whom and when.
test.describe('Trabajo de la cotización', () => {
  test.use({ prepareData: PLUMBER_QUOTES, extraRoutes: QUOTES_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`cotización con su trabajo agendado ${look.suffix}`, async ({ page }) => {
        await open(page, '/quotes/2f6c8a14-7d3b-4e9a-a5c2-9b1e0d4f7a83');
        await expect(page.getByRole('heading', { name: 'Citas del trabajo' })).toBeVisible();
        await snap(page, `cotizacion-trabajo-${look.suffix}`);
      });
    });
  }

  test('agendar el trabajo', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/2f6c8a14-7d3b-4e9a-a5c2-9b1e0d4f7a83');
    await page.getByRole('button', { name: 'Agendar el trabajo' }).click();
    const form = await dialog(page, 'Agendar el trabajo');
    await expect(form.getByLabel('Atiende')).toBeVisible();
    await snap(page, 'cotizacion-agendar-trabajo-escritorio', { fullPage: false });
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
        await expect(page.getByRole('button', { name: 'Aceptar la cotización' })).toBeVisible();
        await expect(page.getByRole('link', { name: 'Preguntar algo al negocio' })).toBeVisible();
        await snap(page, `cotizacion-enlace-${look.suffix}`);
      });
    });
  }

  // Fase 29G: the customer accepts it here, saying what they want, and reads it accepted.
  test('aceptar la cotización desde el enlace', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cotizacion#${QUOTE_LINK}`);
    await page.getByRole('button', { name: 'Aceptar la cotización' }).click();
    await page.getByLabel('Mensaje para Plomería Hernández (opcional)').fill('Los espero el jueves por la mañana');
    await expect(page.getByRole('button', { name: 'Sí, la acepto' })).toBeVisible();
    await snap(page, 'cotizacion-enlace-aceptar-telefono');
  });

  test('cotización aceptada desde el enlace', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cotizacion#${ANSWERED_QUOTE_LINK}`);
    await expect(page.getByText(/La aceptaste aquí el/)).toBeVisible();
    await snap(page, 'cotizacion-enlace-aceptada-telefono');
  });

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
// Fase 29H: the quote of a visit says what the customer said happens, with the photos they sent.
test.describe('Lo que pasa en la cotización', () => {
  test.use({ prepareData: PLUMBER_QUOTES, extraRoutes: QUOTE_WITH_PROBLEM_ROUTES });

  test('cotización con lo que pasa y sus fotos', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/0c9d2e71-5b3a-4f8e-a6d4-3e1b7c9f2a58');
    const problem = page.getByRole('region', { name: 'Lo que pasa' });
    await expect(problem.getByRole('link', { name: 'Abrir foto 2 de lo que pasa' }).locator('img')).toBeVisible();
    await snap(page, 'cotizacion-lo-que-pasa-escritorio');
  });
});

test('the scenes open the quotes the mocks answer', () => {
  expect([DRAFT_QUOTE_ID, SENT_QUOTE_ID, CONNECTED_QUOTE_ID, ACCEPTED_QUOTE_ID, CONVERTED_QUOTE_ID, SCHEDULED_QUOTE_ID]).toEqual([
    '7a3f1c52-2d4b-4e6a-9c1f-0b8e5d2a7c41', '0c9d2e71-5b3a-4f8e-a6d4-3e1b7c9f2a58', '3e7b9c14-8f2a-4d6b-b1e5-7a0c4f9d2e63',
    '5d2a8f61-3c7e-4b9a-8e14-6f0b2c9d7a35', '8b4e1d73-9a2c-4f6e-b5d8-2c7a0e9f4b16', '2f6c8a14-7d3b-4e9a-a5c2-9b1e0d4f7a83',
  ]);
});

// Fase 31N: the business asks a deposit of 30 % to accept from the link, with its Mercado Pago account; its customer accepts by
// paying it, and the quote is already its order.
test.describe('Anticipo de la cotización', () => {
  test.use({ prepareData: PLUMBER_QUOTES, extraRoutes: QUOTES_WITH_DEPOSIT_ROUTES });

  test('cotizaciones con anticipo', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes');
    await expect(page.getByLabel('Anticipo para aceptar')).toHaveValue('30');
    await snap(page, 'cotizaciones-anticipo-escritorio');
  });

  test('cotización con su anticipo pagado', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/quotes/9e2b7c41-6d3a-4f8e-b1c5-0a7d4e9f2b63');
    await expect(page.getByText('Anticipo pagado en línea')).toBeVisible();
    await snap(page, 'cotizacion-anticipo-pagado-escritorio');
  });
});

test.describe('Anticipo desde el enlace', () => {
  test.use({ signedIn: false, extraRoutes: PUBLIC_QUOTE_ROUTES });

  test('cotización del enlace con anticipo', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cotizacion#${DEPOSIT_QUOTE_LINK}`);
    await expect(page.getByRole('button', { name: 'Aceptar y pagar el anticipo de $1,266.72' })).toBeVisible();
    await snap(page, 'cotizacion-enlace-anticipo-telefono');
  });

  test('pagar el anticipo desde el enlace', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cotizacion#${DEPOSIT_QUOTE_LINK}`);
    await page.getByRole('button', { name: 'Aceptar y pagar el anticipo de $1,266.72' }).click();
    await expect(page.getByRole('button', { name: 'Ir a pagar $1,266.72' })).toBeVisible();
    await snap(page, 'cotizacion-enlace-pagar-anticipo-telefono');
  });

  test('cotización aceptada con su anticipo', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cotizacion#${PAID_QUOTE_LINK}`);
    await expect(page.getByText(/y pagaste su anticipo de/)).toBeVisible();
    await snap(page, 'cotizacion-enlace-anticipo-pagado-telefono');
  });

  test('regreso del pago del anticipo', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    // This device went to pay it from the link: the return page reads it with the link it kept.
    await page.addInitScript(({ checkoutId, token }) => {
      window.localStorage.setItem('tienditapp.quoteDeposits', JSON.stringify({ [checkoutId]: { token, savedAt: new Date().toISOString() } }));
    }, { checkoutId: DEPOSIT_CHECKOUT_ID, token: PAID_QUOTE_LINK });
    await open(page, '/cotizacion/pago/6c1e9a47-2b8d-4f3a-9e5c-7d0b2a8f4c19?payment_id=1234567890&status=approved');
    await expect(page.getByRole('heading', { name: '¡Anticipo pagado!' })).toBeVisible();
    await snap(page, 'cotizacion-anticipo-regreso-telefono');
  });
});
