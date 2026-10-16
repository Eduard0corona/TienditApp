import { expect, test } from '../fixtures';
import { SIZES, open, snap, snapElement } from '../helpers';
import { MENU_CON_PEDIDOS, SIN_NEGOCIOS } from './explorar.mocks';
import { CUSTOMER_PROFILE } from './vender-pedidos.mocks';

// Explorar (Fase 24A): the businesses of the city by category, as a visitor and a customer see them, and the card of
// the owner's settings that says how its business shows there. The directory of the scenes has seven businesses: two
// take orders now (one delivers), one has them paused, a kitchen that only delivers, a shop with its menu only and two
// closed.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.describe('Explorar', () => {
  test.use({ signedIn: false });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`explorar ${look.suffix}`, async ({ page }) => {
        await open(page, '/explorar');
        await expect(page.getByRole('link', { name: /Café La Esquina/ })).toBeVisible();
        await snap(page, `explorar-${look.suffix}`);
      });
    });
  }

  test('explorar una categoría en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/explorar/comida');
    await expect(page.getByRole('heading', { level: 1, name: 'Comida' })).toBeVisible();
    await snap(page, 'explorar-categoria-telefono');
  });

  test('explorar una búsqueda sin resultados', async ({ page }) => {
    await open(page, '/explorar?q=sushi');
    await expect(page.getByText('No encontramos «sushi»')).toBeVisible();
    await snap(page, 'explorar-sin-resultados');
  });

  test.describe('sin negocios todavía', () => {
    test.use({ prepareData: SIN_NEGOCIOS });

    test('explorar sin negocios', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/explorar');
      await expect(page.getByText('Pronto verás aquí los negocios de tu ciudad')).toBeVisible();
      await snap(page, 'explorar-sin-negocios-telefono');
    });
  });
});

test.describe('Explorar como cliente', () => {
  test.use({ profile: CUSTOMER_PROFILE });

  test('explorar como cliente en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/explorar');
    await expect(page.getByRole('link', { name: /Mis pedidos/ })).toBeVisible();
    await snap(page, 'explorar-cliente-telefono');
  });
});

test.describe('Cómo te ven en TienditApp', () => {
  test.use({ prepareData: MENU_CON_PEDIDOS });

  test('negocio, cómo te ven en TienditApp', async ({ page }) => {
    await open(page, '/settings/business');
    const card = page.getByRole('region', { name: 'Cómo te ven en TienditApp' });
    await expect(card.getByText(/Apareces en Explorar/)).toBeVisible();
    await snapElement(card, 'negocio-como-te-ven');
  });
});
