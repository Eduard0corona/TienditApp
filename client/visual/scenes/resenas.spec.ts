import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { SUPER_ADMIN_PROFILE } from './caja-config.mocks';
import { ACCOUNT_PROFILE, TIENDA_CON_CUENTA_ROUTES } from './cuenta-cliente.mocks';
import { PUBLICO_ROUTES } from './publico-cuenta-marco.mocks';
import { EXPLORAR_CON_CALIFICACION, PUEDE_CALIFICAR, RESENA_OCULTA, TIENDA_CON_RESENAS } from './resenas.mocks';

// The reviews of the businesses (Fase 24H, ADR 0030): the store's rating and its reviews after the menu, the card of
// Explorar with its rating, rating a business from the store and from Mis pedidos (and one the platform hid), the
// owner's Clientes › Reseñas with its answer and its report, and the platform's console with its decisions.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapDialog(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

test.describe('La tienda con reseñas', () => {
  test.use({ signedIn: false, extraRoutes: PUBLICO_ROUTES, prepareData: TIENDA_CON_RESENAS });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`tienda con reseñas ${look.suffix}`, async ({ page }) => {
        await open(page, '/m/cafe-la-esquina');
        await expect(page.getByRole('button', { name: 'Calificación 3.9 de 5, 7 reseñas. Ver reseñas' })).toBeVisible();
        const reviews = page.getByRole('region', { name: 'Reseñas' });
        await expect(reviews.getByText('Lucía R.')).toBeVisible();
        await expect(reviews.getByText('Miguel A.')).toBeVisible();
        await snap(page, `tienda-con-resenas-${look.suffix}`);
      });
    });
  }
});

test.describe('Calificar', () => {
  test.use({ profile: ACCOUNT_PROFILE, extraRoutes: TIENDA_CON_CUENTA_ROUTES, prepareData: PUEDE_CALIFICAR });

  test('tienda calificar', async ({ page }) => {
    await open(page, '/m/cafe-la-esquina');
    await page.getByRole('region', { name: 'Reseñas' }).getByRole('button', { name: 'Calificar' }).click();
    const rate = await dialog(page, 'Califica a Café La Esquina');
    await rate.getByRole('radio', { name: '4 estrellas' }).check();
    await rate.getByLabel('Cuéntanos cómo te fue (opcional)').fill('El americano, muy bueno. La concha llegó un poco aplastada.');
    await expect(rate.getByRole('button', { name: 'Publicar' })).toBeEnabled();
    await snapDialog(page, 'tienda-calificar');
  });

  test.describe('reseña oculta', () => {
    test.use({ prepareData: RESENA_OCULTA });

    test('mis pedidos tu reseña oculta', async ({ page }) => {
      await open(page, '/mis-pedidos');
      await page.getByRole('button', { name: 'Tu reseña de Café La Esquina' }).click();
      const mine = await dialog(page, 'Tu reseña de Café La Esquina');
      await expect(mine.getByText(/La plataforma ocultó tu reseña/)).toBeVisible();
      await snapDialog(page, 'mis-pedidos-resena-oculta');
    });
  });
});

test.describe('Explorar con calificación', () => {
  test.use({ signedIn: false, prepareData: EXPLORAR_CON_CALIFICACION });

  test('explorar con calificación', async ({ page }) => {
    await open(page, '/explorar');
    await expect(page.getByRole('link', { name: /Calificación 3\.9 de 5, 7 reseñas/ })).toBeVisible();
    await snap(page, 'explorar-con-calificacion');
  });
});

test.describe('Reseñas del negocio', () => {
  test.use({ prepareData: TIENDA_CON_RESENAS });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`reseñas ${look.suffix}`, async ({ page }) => {
        await open(page, '/reviews');
        await expect(page.getByRole('button', { name: 'Responder a Jorge M.' })).toBeVisible();
        await snap(page, `resenas-${look.suffix}`);
      });
    });
  }

  test('reseñas responder', async ({ page }) => {
    await open(page, '/reviews');
    await page.getByRole('button', { name: 'Responder a Jorge M.' }).click();
    const answer = await dialog(page, 'Responder a Jorge M.');
    await answer.getByLabel('Tu respuesta').fill('¡Gracias, Jorge! Desde esta semana horneamos una segunda tanda a las 11:00.');
    await snapDialog(page, 'resenas-responder');
  });

  test('reseñas reportar', async ({ page }) => {
    await open(page, '/reviews');
    await page.getByRole('button', { name: 'Reportar la reseña de Miguel A.' }).click();
    const report = await dialog(page, 'Reportar la reseña de Miguel A.');
    await report.getByRole('radio', { name: 'Publicidad o nada que ver con el negocio' }).check();
    await snapDialog(page, 'resenas-reportar');
  });

  test('reseñas reportadas', async ({ page }) => {
    await open(page, '/reviews');
    await page.getByLabel('Mostrar').selectOption({ label: 'Reportadas' });
    await expect(page.getByText('En revisión')).toBeVisible();
    await expect(page.getByText('Lucía R.')).toBeHidden();
    await snap(page, 'resenas-reportadas');
  });
});

test.describe('Reseñas en la plataforma', () => {
  test.use({ profile: SUPER_ADMIN_PROFILE, prepareData: TIENDA_CON_RESENAS });

  test('plataforma reseñas', async ({ page }) => {
    await open(page, '/platform/reviews');
    await expect(page.getByRole('button', { name: 'Ocultar la reseña de Carla V. en Café La Esquina' })).toBeVisible();
    await snap(page, 'plataforma-resenas');
  });

  test('plataforma ocultar reseña', async ({ page }) => {
    await open(page, '/platform/reviews');
    await page.getByRole('button', { name: 'Ocultar la reseña de Carla V. en Café La Esquina' }).click();
    const hide = await dialog(page, 'Ocultar la reseña de Carla V.');
    await expect(hide.getByRole('radio', { name: 'No habla de un pedido o una cita' })).toBeChecked();
    await snapDialog(page, 'plataforma-ocultar-resena');
  });

  test('plataforma reseñas ocultas', async ({ page }) => {
    await open(page, '/platform/reviews');
    await page.getByLabel('Mostrar').selectOption({ label: 'Ocultas' });
    await expect(page.getByRole('button', { name: 'Restaurar la reseña de Pedro G. en Café La Esquina' })).toBeVisible();
    await snap(page, 'plataforma-resenas-ocultas');
  });
});
