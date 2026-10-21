import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { CANCELLED_COUNT, CON_CONTEOS, COUNT_ROUTES, OPEN_COUNT, POSTED_COUNT } from './conteos.mocks';

// Inventario › Conteos (Fase 27A): the list of counts, the sheet of an open count, its review before posting it, a posted
// count with its adjustment and a cancelled one, and the dialog that starts a count. Café La Esquina has an open count of
// Roma Norte, a posted one of Bodega Central and a cancelled one of Coyoacán (conteos.mocks).

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

async function openCounts(page: Page) {
  await open(page, '/inventory');
  await page.getByRole('tab', { name: 'Conteos' }).click();
  await expect(page.getByRole('button', { name: `Contar el conteo ${OPEN_COUNT}` })).toBeVisible();
}

async function openCount(page: Page, action: 'Contar' | 'Ver', number: string) {
  await openCounts(page);
  await page.getByRole('button', { name: `${action} el conteo ${number}` }).click();
  await expect(page.getByRole('heading', { name: new RegExp(number) })).toBeVisible();
}

for (const look of LOOKS) {
  test.describe(`Conteos ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode, prepareData: CON_CONTEOS, extraRoutes: COUNT_ROUTES });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`conteos ${look.suffix}`, async ({ page }) => {
      await openCounts(page);
      await expect(page.getByRole('button', { name: `Ver el conteo ${CANCELLED_COUNT}` })).toBeVisible();
      await snap(page, `conteos-${look.suffix}`);
    });

    test(`conteo hoja ${look.suffix}`, async ({ page }) => {
      await openCount(page, 'Contar', OPEN_COUNT);
      await expect(page.getByLabel('Café en grano Chiapas')).toHaveValue('8.2');
      await snap(page, `conteo-hoja-${look.suffix}`);
    });

    test(`conteo revisar ${look.suffix}`, async ({ page }) => {
      await openCount(page, 'Contar', OPEN_COUNT);
      await page.getByRole('button', { name: 'Revisar diferencias' }).click();
      await expect(page.getByText('Faltan por contar 5 artículos: se quedan como están.')).toBeVisible();
      await expect(page.getByRole('cell', { name: /^Hielo en bolsa 5 kg/ })).toBeVisible();
      await snap(page, `conteo-revisar-${look.suffix}`);
    });

    test(`conteo aplicado ${look.suffix}`, async ({ page }) => {
      await openCount(page, 'Ver', POSTED_COUNT);
      await expect(page.getByRole('button', { name: 'Ver salida INV-20261002-0001' })).toBeVisible();
      await expect(page.getByRole('cell', { name: /Azúcar mascabado/ })).toBeVisible();
      await snap(page, `conteo-aplicado-${look.suffix}`);
    });

    if (look.suffix !== 'escritorio-oscuro') {
      test(`nuevo conteo ${look.suffix}`, async ({ page }) => {
        await openCounts(page);
        await page.getByRole('button', { name: 'Nuevo conteo' }).click();
        const modal = await dialog(page, 'Nuevo conteo');
        await modal.getByLabel('Solo los artículos que elijas').check();
        await modal.getByLabel(/Totopos para chilaquiles/).check();
        await expect(modal.getByText('1 artículo elegido')).toBeVisible();
        await snap(page, `nuevo-conteo-${look.suffix}`, { fullPage: false });
      });

      test(`conteo cancelado ${look.suffix}`, async ({ page }) => {
        await openCount(page, 'Ver', CANCELLED_COUNT);
        await expect(page.getByText('Cancelado sin mover existencias: Se contó antes de que llegara la leche')).toBeVisible();
        await snap(page, `conteo-cancelado-${look.suffix}`);
      });
    }

    if (look.suffix === 'escritorio') {
      test(`conteo guardado ${look.suffix}`, async ({ page }) => {
        await openCount(page, 'Contar', OPEN_COUNT);
        const panela = page.getByLabel('Queso panela');
        await panela.fill('4.5');
        await panela.press('Enter');
        await expect(page.getByText('Guardado')).toBeVisible();
        await expect(page.getByLabel('Tapas para vaso 12 oz (50 pzas)')).toBeFocused();
        await page.getByLabel('Tapas para vaso 12 oz (50 pzas)').fill('2.5');
        await page.getByLabel('Tapas para vaso 12 oz (50 pzas)').blur();
        await expect(page.getByText('La cantidad contada va de 0 a 9,999,999, en piezas enteras.')).toBeVisible();
        await snap(page, `conteo-guardado-${look.suffix}`);
      });
    }
  });
}
