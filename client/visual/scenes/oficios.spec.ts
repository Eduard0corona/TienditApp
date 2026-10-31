import { expect, test } from '../fixtures';
import { SIZES, open, snap, snapElement } from '../helpers';
import { OFICIOS_EN_EXPLORAR, PLOMERO_EN_CONFIGURACION, TIENDA_DE_PLOMERIA } from './oficios.mocks';

// Oficios a domicilio (Fase 29A, ADR 0053): the trades that go to their customers' homes to quote look like what they
// are. In Explorar, «Hogar y reparaciones» lists a plumber, a carpenter and a locksmith that receive visits: their cards
// speak of visits and what one costs, never of orders, and with the customer's postal code say whether they go there
// (the one that does not, last). The plumber's store shows its visits instead of an empty menu, and its owner sets them
// in Configuración, where «Cómo te ven» says that its store still publishes the address of a local.

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
