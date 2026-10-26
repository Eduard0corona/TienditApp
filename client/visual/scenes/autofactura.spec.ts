import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, open, snap } from '../helpers';
import {
  AUTOFACTURA_ROUTES,
  CODE,
  FOLIO,
  FROM_QR,
  FUERA_DE_PLAZO,
  NO_DISPONIBLE,
  POR_FACTURAR,
  RECHAZADA,
  SLUG,
  YA_FACTURADO,
} from './autofactura.mocks';

// Autofactura (Fase 28F, ADR 0050): a customer of Café La Esquina invoices the ticket on its page, without an account. The
// ticket's QR brings its folio and code; the customer writes their fiscal data and their email, and the XML and the PDF
// arrive there. Also: the PAC refusing the name, a ticket invoiced already (its invoice sent again to whoever writes its
// RFC), one paid last month, a code typed wrong, and the business with self-invoicing off.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ signedIn: false, extraRoutes: AUTOFACTURA_ROUTES });

/** Karla's data as her constancia says it, and her email. */
async function fillReceiver(page: Page) {
  await page.getByLabel('RFC').fill('XIQB891116QE4');
  await page.getByLabel('Nombre o razón social').fill('Karla Fuente Nolasco');
  await page.getByLabel('Código postal fiscal').fill('26015');
  await page.getByLabel('Correo para recibir la factura').fill('karla.fuente@gmail.com');
}

test.describe('con el QR del ticket', () => {
  test.use({ prepareData: POR_FACTURAR });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`autofactura con el ticket ${look.suffix}`, async ({ page }) => {
        await open(page, FROM_QR);
        await expect(page.getByRole('heading', { name: `Ticket ${FOLIO}` })).toBeVisible();
        await expect(page.getByText(/Puedes facturar esta compra hasta el 31 de octubre de 2026/)).toBeVisible();
        await snap(page, `autofactura-ticket-${look.suffix}`);
      });
    });
  }

  test('autofactura con los datos escritos', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, FROM_QR);
    await fillReceiver(page);
    await expect(page.getByRole('button', { name: 'Facturar' })).toBeEnabled();
    await snap(page, 'autofactura-datos-telefono');
  });

  test('autofactura timbrada', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, FROM_QR);
    await fillReceiver(page);
    await page.getByRole('button', { name: 'Facturar' }).click();
    await expect(page.getByText('Listo: tu factura F-27 está timbrada.')).toBeVisible();
    await snap(page, 'autofactura-timbrada-telefono');
  });
});

test.describe('rechazada por el PAC', () => {
  test.use({ prepareData: RECHAZADA });

  test('autofactura rechazada', async ({ page }) => {
    await open(page, FROM_QR);
    await fillReceiver(page);
    await page.getByRole('button', { name: 'Facturar' }).click();
    await expect(page.getByRole('alert')).toContainText('El SAT no aceptó la factura F-27');
    await snap(page, 'autofactura-rechazada');
  });
});

test.describe('ya facturado', () => {
  test.use({ prepareData: YA_FACTURADO });

  test('autofactura de un ticket ya facturado', async ({ page }) => {
    await open(page, FROM_QR);
    await expect(page.getByText(/Este ticket ya tiene la factura F-27/)).toBeVisible();
    await page.getByLabel('RFC de la factura').fill('XIQB891116QE4');
    await page.getByLabel('Correo', { exact: true }).fill('karla.fuente@gmail.com');
    await snap(page, 'autofactura-ya-facturado');
  });

  test('autofactura reenviada', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, FROM_QR);
    await page.getByLabel('RFC de la factura').fill('XIQB891116QE4');
    await page.getByLabel('Correo', { exact: true }).fill('karla.fuente@gmail.com');
    await page.getByRole('button', { name: 'Reenviar factura' }).click();
    await expect(page.getByText('Enviamos la factura F-27 a karla.fuente@gmail.com.')).toBeVisible();
    await snap(page, 'autofactura-reenviada-telefono');
  });
});

test.describe('fuera de plazo', () => {
  test.use({ prepareData: FUERA_DE_PLAZO });

  test('autofactura de una compra del mes pasado', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, FROM_QR);
    await expect(page.getByText(/se podía facturar en línea hasta el miércoles 30 de septiembre de 2026/)).toBeVisible();
    await snap(page, 'autofactura-fuera-de-plazo-telefono');
  });
});

test.describe('sin el QR', () => {
  test.use({ prepareData: POR_FACTURAR });

  test('autofactura sin ticket', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/cafe-la-esquina/facturar');
    await expect(page.getByRole('button', { name: 'Buscar ticket' })).toBeDisabled();
    await snap(page, 'autofactura-sin-ticket-telefono');
  });

  test('autofactura con el código mal escrito', async ({ page }) => {
    await open(page, `/m/${SLUG}/facturar`);
    await page.getByLabel('Folio', { exact: true }).fill(FOLIO);
    await page.getByLabel('Código', { exact: true }).fill(CODE.replace('K', 'X'));
    await page.getByRole('button', { name: 'Buscar ticket' }).click();
    await expect(page.getByRole('alert')).toContainText('No encontramos ese ticket');
    await snap(page, 'autofactura-no-encontrado');
  });
});

test.describe('apagada', () => {
  test.use({ prepareData: NO_DISPONIBLE });

  test('autofactura apagada', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, FROM_QR);
    await expect(page.getByText('Este negocio no factura en línea por ahora: pídele tu factura directamente.')).toBeVisible();
    await snap(page, 'autofactura-apagada-telefono');
  });
});
