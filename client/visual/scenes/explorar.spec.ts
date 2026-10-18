import { expect, test } from '../fixtures';
import { SIZES, open, snap, snapElement } from '../helpers';
import { CON_FAVORITOS, MENU_CON_PEDIDOS, MUCHOS_FAVORITOS, SIN_NEGOCIOS } from './explorar.mocks';
import { CUSTOMER_PROFILE } from './vender-pedidos.mocks';

// Explorar (Fase 24A): the businesses of the city by category, as a visitor and a customer see them, and the card of
// the owner's settings that says how its business shows there. The directory of the scenes has seven businesses: two
// take orders now (one delivers), one has them paused, a kitchen that only delivers, a shop with its menu only and two
// closed. Since the 24F each card has its cover (Café La Esquina's photo, the colors of the others), a search shows the
// products it found first, and a customer keeps favorites with a heart. Since the 24G «Cerca de mí» orders them by
// distance from the customer, near the Zócalo of the demo city, each card saying how far it is.

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
        await expect(page.getByRole('link', { name: /^Café La Esquina/ })).toBeVisible();
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

  // «chilaquiles»: two products of two businesses (one with its photo and discount), and the business whose line says it.
  for (const look of [LOOKS[0], LOOKS[1]]) {
    test(`explorar productos ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await open(page, '/explorar?q=chilaquiles');
      await expect(page.getByRole('link', { name: /^Chilaquiles rojos con huevo/ })).toBeVisible();
      await snap(page, `explorar-productos-${look.suffix}`);
    });
  }

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

test.describe('Cerca de mí', () => {
  test.use({ signedIn: false });

  test.describe('con permiso', () => {
    test.use({ permissions: ['geolocation'], geolocation: { latitude: 19.4195, longitude: -99.1622 } });

    for (const look of [LOOKS[0], LOOKS[1]]) {
      test(`explorar cerca de mí ${look.suffix}`, async ({ page }) => {
        await page.setViewportSize(look.viewport);
        await open(page, '/explorar?cerca=1');
        await expect(page.getByText('Primero lo más cercano a ti.')).toBeVisible();
        await expect(page.getByRole('link', { name: /^Abarrotes Doña Mary/ })).toContainText('a 200 m');
        await snap(page, `explorar-cerca-${look.suffix}`);
      });
    }

    // The products of a search come from the nearest businesses first, each with how far its business is.
    test('explorar cerca de mí productos teléfono', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/explorar?q=chilaquiles&cerca=1');
      await expect(page.getByRole('link', { name: /^Chilaquiles verdes/ })).toContainText('a 400 m');
      await snap(page, 'explorar-cerca-productos-telefono');
    });
  });

  test('explorar cerca de mí sin permiso en el teléfono', async ({ page, context }) => {
    await context.clearPermissions();
    await page.setViewportSize(SIZES.phone);
    await open(page, '/explorar?cerca=1');
    await expect(page.getByText('Permite tu ubicación en el navegador para ver primero lo más cercano.')).toBeVisible();
    await snap(page, 'explorar-cerca-sin-permiso-telefono');
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

  test.describe('con favoritos', () => {
    test.use({ prepareData: CON_FAVORITOS });

    // In dark too: the red of a kept heart reads on each surface.
    for (const look of LOOKS) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`explorar favoritos ${look.suffix}`, async ({ page }) => {
          await open(page, '/explorar');
          await expect(page.getByRole('region', { name: 'Tus favoritos' }).getByText('Panadería San Juan')).toBeVisible();
          await snap(page, `explorar-favoritos-${look.suffix}`);
        });
      });
    }
  });

  test.describe('con muchos favoritos', () => {
    test.use({ prepareData: MUCHOS_FAVORITOS });

    test('explorar muchos favoritos escritorio', async ({ page }) => {
      await page.setViewportSize(SIZES.desktop);
      await open(page, '/explorar');
      const favorites = page.getByRole('region', { name: 'Tus favoritos' });
      await expect(favorites.getByRole('button', { name: 'Ver todos tus favoritos (8)' })).toBeVisible();
      await snapElement(favorites, 'explorar-muchos-favoritos-escritorio');
    });
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
