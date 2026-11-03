import { readFileSync } from 'node:fs';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap, snapElement } from '../helpers';
import {
  ENLACE_CON_FOTOS, OFICIOS_EN_EXPLORAR, OFICIOS_QUE_RESPONDEN, PLOMERO_EN_CONFIGURACION, SERVICIOS_EN_EXPLORAR, TIENDA_DE_PLOMERIA, VISIT_LINK, VISITA_CON_FOTOS,
  VISITA_DE_PLOMERIA,
} from './oficios.mocks';

// Oficios a domicilio (Fase 29A, ADR 0053): the trades that go to their customers' homes to quote look like what they
// are. In Explorar, «Hogar y reparaciones» lists a plumber, a carpenter and a locksmith that receive visits: their cards
// speak of visits and what one costs, never of orders, and with the customer's postal code say whether they go there
// (the one that does not, last). The plumber's store shows its visits instead of an empty menu, and its owner sets them
// in Configuración, where «Cómo te ven» says that its store still publishes the address of a local. Its visits are asked
// for online (Fase 29B): the postal code says whether it goes there and what the visit costs there, then a day and a free
// time, what happens and where, and the receipt with the link of the visit.

const LOOKS = [
  { suffix: 'escritorio', viewport: SIZES.desktop },
  { suffix: 'telefono', viewport: SIZES.phone },
] as const;

/** Remembers the customer's postal code as Explorar does. */
async function rememberPostalCode(page: import('@playwright/test').Page, postalCode: string) {
  await page.addInitScript((code) => window.localStorage.setItem('tienditapp.postalCode', code), postalCode);
}

test.describe('Oficios en Explorar', () => {
  test.use({ signedIn: false, prepareData: OFICIOS_EN_EXPLORAR });

  for (const look of LOOKS) {
    test(`oficios en explorar ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await open(page, '/explorar/hogar-y-reparaciones');
      await expect(page.getByRole('heading', { level: 1, name: 'Hogar y reparaciones' })).toBeVisible();
      await expect(page.getByText('Visita desde $350.00, se descuenta si contratas')).toBeVisible();
      await snap(page, `oficios-explorar-${look.suffix}`);
    });
  }

  test('oficios en explorar con código postal', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await rememberPostalCode(page, '06700');
    await open(page, '/explorar/hogar-y-reparaciones');
    // The plumber and the carpenter go to Roma Norte; the locksmith does not, and comes last.
    await expect(page.getByText('Va a tu código postal · Visita $400.00, se descuenta si contratas')).toBeVisible();
    await expect(page.getByText('No va a tu código postal')).toBeVisible();
    await snap(page, 'oficios-explorar-codigo-postal-telefono');
  });
});

// A search finds services (Fase 29I, ADR 0063): «corte» finds two services of a salon that books online, at their price and
// length, and one of the carpenter, from what it starts at and with IVA on top; the salon is also found by its description.
// Fase 29J (ADR 0064): how soon each trade answers the requests made online, on its card.
test.describe('Oficios que responden pronto', () => {
  test.use({ signedIn: false, prepareData: OFICIOS_QUE_RESPONDEN });

  for (const look of LOOKS) {
    test(`oficios que responden ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await open(page, '/explorar/hogar-y-reparaciones');
      await expect(page.getByRole('link', { name: /^Plomería Hernández/ })).toContainText('Responde en menos de 1 hora');
      await expect(page.getByRole('link', { name: /^Carpintería Don José/ })).toContainText('Responde en menos de 12 horas');
      await expect(page.getByRole('link', { name: /^Cerrajería Pérez/ })).not.toContainText('Responde');
      await snap(page, `oficios-explorar-respuesta-${look.suffix}`);
    });
  }
});

test.describe('Servicios en Explorar', () => {
  test.use({ signedIn: false, prepareData: SERVICIOS_EN_EXPLORAR });

  for (const look of LOOKS) {
    test(`servicios en explorar ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await open(page, '/explorar?q=corte');
      const services = page.getByRole('region', { name: 'Servicios' });
      await expect(services.getByRole('link', { name: /^Corte de madera a la medida/ })).toBeVisible();
      await expect(services.getByText('desde $300.00')).toBeVisible();
      await snap(page, `oficios-explorar-servicios-${look.suffix}`);
    });
  }
});

test.describe('Tienda de un oficio', () => {
  test.use({ signedIn: false, extraRoutes: TIENDA_DE_PLOMERIA });

  for (const look of LOOKS) {
    test(`tienda de un oficio ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await open(page, '/m/plomeria-hernandez');
      const visits = page.getByRole('region', { name: 'Visitas a domicilio' });
      await expect(visits.getByRole('link', { name: 'Solicitar visita' })).toBeVisible();
      await expect(visits.getByRole('button', { name: 'Ver todos los códigos postales' })).toBeVisible();
      await snap(page, `oficios-tienda-${look.suffix}`);
    });
  }
});

test.describe('Visitas a domicilio en Configuración', () => {
  test.use({ prepareData: PLOMERO_EN_CONFIGURACION });

  for (const look of LOOKS) {
    test(`visitas a domicilio en configuración ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await open(page, '/settings/business');
      const card = page.getByRole('region', { name: 'Visitas a domicilio' });
      await expect(card.getByText(/Tus clientes ya ven tus visitas a domicilio/)).toBeVisible();
      await expect(card.getByLabel('Servicio de la visita')).toHaveValue('svc-visita');
      await card.scrollIntoViewIfNeeded();
      await snapElement(card, `oficios-configuracion-${look.suffix}`);
    });
  }

  test('aviso de la dirección en cómo te ven', async ({ page }) => {
    await open(page, '/settings/business');
    const card = page.getByRole('region', { name: 'Cómo te ven en TienditApp' });
    await expect(card.getByText(/Tu tienda muestra la dirección de tu sucursal principal/)).toBeVisible();
    await expect(card.getByRole('link', { name: 'Sucursales' })).toBeVisible();
    await card.scrollIntoViewIfNeeded();
    await snapElement(card, 'oficios-aviso-direccion');
  });
});

test.describe('Solicitar una visita', () => {
  test.use({ signedIn: false, extraRoutes: VISITA_DE_PLOMERIA });

  /** Roma Norte, where the plumber goes: its free times open on Monday. */
  async function openWithPostalCode(page: import('@playwright/test').Page) {
    await open(page, '/m/plomeria-hernandez/visita');
    await page.getByLabel('Código postal').fill('06700');
    await expect(page.getByText('Vamos al 06700 · Visita $400.00 más IVA, se descuenta si contratas')).toBeVisible();
    await expect(page.getByRole('button', { name: /^Lun 5.*lunes 5 de octubre$/ })).toHaveAttribute('aria-pressed', 'true');
  }

  for (const look of LOOKS) {
    test(`solicitar visita ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await openWithPostalCode(page);
      await page.getByRole('button', { name: '10:00 a. m.' }).click();
      await expect(page.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' })).toBeVisible();
      await snap(page, `oficios-solicitar-visita-${look.suffix}`);
    });
  }

  test('solicitar visita donde no va', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/plomeria-hernandez/visita');
    await page.getByLabel('Código postal').fill('72000');
    await expect(page.getByText('Por ahora no vamos al 72000.')).toBeVisible();
    await snap(page, 'oficios-solicitar-visita-no-va-telefono');
  });

  test('solicitar visita datos y recibo', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await openWithPostalCode(page);
    await page.getByRole('button', { name: '10:00 a. m.' }).click();
    await page.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }).click();
    const sheet = await dialog(page, 'Tu visita');
    await sheet.getByLabel('¿Qué pasa?').fill('Gotea el calentador desde ayer en la noche.');
    await sheet.getByLabel('Dirección').fill('Colima 120, Roma Norte');
    await sheet.getByLabel('Tu nombre').fill('Valeria Ruiz');
    await sheet.getByLabel('Teléfono (WhatsApp)').fill('55 2210 7765');
    await snapElement(sheet, 'oficios-solicitar-visita-datos-telefono');

    await sheet.getByRole('button', { name: 'Solicitar visita' }).click();
    await expect(page.getByRole('heading', { name: '¡Solicitud enviada!' })).toBeVisible();
    await expect(page.getByText('A domicilio · CP 06700')).toBeVisible();
    await snap(page, 'oficios-solicitar-visita-enviada-telefono');
  });
});

// Fase 29H: the photos of what happens, picked in the request and sent with its link; more from the visit's link.
const PHOTO = (name: string) => ({
  name,
  mimeType: 'image/png',
  buffer: readFileSync(new URL(`../mocks/img/${name}`, import.meta.url)),
});

test.describe('Fotos de lo que pasa', () => {
  test.use({ signedIn: false, extraRoutes: VISITA_CON_FOTOS });

  test('solicitar visita con fotos', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/plomeria-hernandez/visita');
    await page.getByLabel('Código postal').fill('06700');
    await page.getByRole('button', { name: '10:00 a. m.' }).click();
    await page.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }).click();
    const sheet = await dialog(page, 'Tu visita');
    await sheet.getByLabel('¿Qué pasa?').fill('Gotea el calentador desde ayer en la noche.');
    await sheet.getByTestId('problem-photos-input').setInputFiles([PHOTO('fuga.png'), PHOTO('pared.png')]);
    await expect(sheet.getByRole('img', { name: 'Foto 2 de lo que pasa' })).toBeVisible();
    await sheet.getByLabel('Dirección').fill('Colima 120, Roma Norte');
    await sheet.getByLabel('Tu nombre').fill('Valeria Ruiz');
    await sheet.getByLabel('Teléfono (WhatsApp)').fill('55 2210 7765');
    await snapElement(sheet, 'oficios-solicitar-visita-fotos-telefono');

    await sheet.getByRole('button', { name: 'Solicitar visita' }).click();
    await expect(page.getByText('Listo: se mandaron 2 fotos.')).toBeVisible();
    await snap(page, 'oficios-solicitar-visita-fotos-enviadas-telefono');
  });
});

test.describe('Fotos desde el enlace de la visita', () => {
  test.use({ signedIn: false, extraRoutes: ENLACE_CON_FOTOS });

  test('enlace de la visita con fotos', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cita#${VISIT_LINK}`);
    await expect(page.getByText('Mandaste 2 fotos de lo que pasa')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Agregar más fotos' })).toBeVisible();
    await snap(page, 'oficios-enlace-visita-fotos-telefono');
  });
});
