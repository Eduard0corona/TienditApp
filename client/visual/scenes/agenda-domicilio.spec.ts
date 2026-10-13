import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { HOME_VISITS_DATA, RICARDO_VISIT_ID } from './agenda-domicilio.mocks';
import { SELL_ROUTES } from './vender-pedidos.mocks';

// Services at the customer's address (Fase 22E): the agenda's visits, the new appointment with the charge of its zone,
// the route of the day, charging an appointment in Vender and the travel time in Configuración › Zonas de entrega.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ prepareData: HOME_VISITS_DATA, extraRoutes: SELL_ROUTES });

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapDialog(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/** The agenda with only the appointments at the customers' addresses. */
async function openHomeVisits(page: Page) {
  await open(page, '/appointments');
  await page.getByLabel('Dónde').selectOption({ label: 'A domicilio' });
  await expect(page.getByText('A domicilio · CP 03100 · Del Valle')).toBeVisible();
}

/** Agenda: the new appointment form once the services and the team have loaded. */
async function openNewAppointment(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Nueva cita' }).click();
  const form = await dialog(page, 'Nueva cita');
  await expect(form.getByRole('option', { name: 'Mantenimiento de cafetera espresso' })).toBeAttached();
  await expect(form.getByRole('option', { name: 'Carlos Méndez' })).toBeAttached();
  return form;
}

/** Karla's maintenance on Tuesday at 10:00 at her address: the end follows the service's two hours. */
async function fillHomeVisit(form: Locator, postalCode: string) {
  await form.getByLabel('Cliente *').fill('Karla Mendoza');
  await form.getByLabel('Teléfono *').fill('55 3021 7788');
  await form.getByLabel('Servicio *').selectOption({ label: 'Mantenimiento de cafetera espresso' });
  await form.getByLabel('Atiende').selectOption({ label: 'Carlos Méndez' });
  await form.getByLabel('A domicilio').check();
  await form.getByLabel('Código postal *').fill(postalCode);
  await form.getByLabel('Dirección *').fill('Ámsterdam 120, depto. 5, Condesa');
  await form.getByLabel('Referencias').fill('Frente al parque, timbre 5');
  await form.getByLabel('Inicio *').fill('2026-10-06T10:00');
  await expect(form.getByLabel('Fin *')).toHaveValue('2026-10-06T12:00');
}

/** Vender with Ricardo's descaling: the service in the ticket and the charge for the visit to Condesa. */
async function openAppointmentSale(page: Page) {
  await open(page, `/sell?appointment=${RICARDO_VISIT_ID}`);
  await expect(page.getByText('Cobro de la cita de')).toBeVisible();
  await expect(page.getByText('Cargo por visita (Condesa)')).toBeVisible();
}

for (const look of LOOKS) {
  test.describe(`Agenda a domicilio ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`citas a domicilio ${look.suffix}`, async ({ page }) => {
      await openHomeVisits(page);
      await snap(page, `agenda-domicilio-${look.suffix}`);
    });

    test(`ruta del día ${look.suffix}`, async ({ page }) => {
      await open(page, '/appointments');
      await page.getByRole('button', { name: 'Ruta del día' }).click();
      await expect(page.getByRole('heading', { name: 'Diego Herrera' })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Sin asignar' })).toBeVisible();
      await snap(page, `agenda-ruta-${look.suffix}`);
    });

    test(`cita nueva a domicilio ${look.suffix}`, async ({ page }) => {
      await open(page, '/appointments');
      const form = await openNewAppointment(page);
      await fillHomeVisit(form, '06140');
      await expect(form.getByText('Zona Condesa · Cargo por visita $45.00')).toBeVisible();
      await snapDialog(page, `agenda-nueva-domicilio-${look.suffix}`);
    });
  });
}

test.describe('Agenda a domicilio', () => {
  test('cita nueva fuera de las zonas', async ({ page }) => {
    await open(page, '/appointments');
    const form = await openNewAppointment(page);
    await fillHomeVisit(form, '72000');
    await expect(form.getByText('Por ahora no hacemos visitas en el 72000.')).toBeVisible();
    await snapDialog(page, 'agenda-nueva-domicilio-sin-zona-escritorio');
  });

  test('editar una visita: lo acordado con el cliente', async ({ page }) => {
    await openHomeVisits(page);
    await page.getByRole('button', { name: 'Editar o reprogramar la cita de Sofía Ramírez Cruz' }).click();
    const form = await dialog(page, 'Editar o reprogramar la cita');
    await expect(form.getByText('(lo acordado con el cliente)')).toBeVisible();
    await snapDialog(page, 'agenda-editar-domicilio-escritorio');
  });

  test('cobrar una cita a domicilio en Vender', async ({ page }) => {
    await openAppointmentSale(page);
    await snap(page, 'vender-cita-domicilio-escritorio');
  });

  test('la venta de la cita, con el cargo por visita', async ({ page }) => {
    await openAppointmentSale(page);
    await page.getByRole('button', { name: /^Cobrar( \$|$)/ }).click();
    const panel = await dialog(page, 'Cobrar');
    await panel.getByRole('button', { name: /^Cobrar \$/ }).click();
    const sale = await dialog(page, /^Venta ORD-\d+ cobrada$/);
    // The charge dialog closes as the sale opens: only the sale stays.
    await expect(page.getByRole('dialog')).toHaveCount(1);
    await expect(sale.getByText('Cargo por visita')).toBeVisible();
    await snapDialog(page, 'vender-cita-domicilio-cobrada');
  });

  test('zonas de entrega: tiempo de traslado entre visitas', async ({ page }) => {
    await open(page, '/settings/delivery-zones');
    await page.getByLabel('Traslado entre citas (minutos)').fill('45');
    await expect(page.getByRole('button', { name: 'Guardar', exact: true })).toBeEnabled();
    await snap(page, 'zonas-entrega-visitas-escritorio');
  });
});
