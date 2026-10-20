import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import { CON_RECORDATORIOS, SIN_WHATSAPP } from './recordatorios.mocks';

// Plazo por cliente y recordatorios del fiado (Fase 26C): Por cobrar with the terms of each customer and the reminders,
// the term in «Fiado de…» and in Clientes, the account with its last reminder, the reminders (with and without WhatsApp)
// and, in Campañas, the reminders sent and the templates for them.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/** Opens the reminders from Por cobrar, as the owner does, and waits for them to load. */
async function openReminders(page: Page, button: string): Promise<Locator> {
  await open(page, '/customers/receivables');
  await page.getByRole('button', { name: button }).click();
  const reminders = await dialog(page, 'Recordatorios del fiado');
  await expect(reminders.getByLabel('Recordar lo vencido por WhatsApp')).toBeVisible();
  return reminders;
}

test.describe('Recordatorios del fiado', () => {
  test.use({ prepareData: CON_RECORDATORIOS });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`por cobrar con plazos y recordatorios ${look.suffix}`, async ({ page }) => {
        await open(page, '/customers/receivables');
        await expect(page.getByText('Recordatorios cada 7 días')).toBeVisible();
        await expect(page.getByRole('row').filter({ hasText: 'Roberto Sánchez Vega' }).getByText('Paga en 7 días')).toBeVisible();
        await snap(page, `por-cobrar-recordatorios-${look.suffix}`);
      });
    });
  }

  test('recordatorios del fiado', async ({ page }) => {
    const reminders = await openReminders(page, 'Cambiar los recordatorios');
    await expect(reminders.getByLabel('Mensaje')).toHaveValue('t-fiado');
    await expect(reminders.getByText('Así le llega:')).toBeVisible();
    await snapScreen(page, 'por-cobrar-recordatorios-del-fiado');
  });

  test('estado de cuenta con su plazo y su recordatorio', async ({ page }) => {
    await open(page, '/customers/receivables');
    await page.getByRole('button', { name: 'Ver cuenta de Roberto Sánchez Vega' }).click();
    const account = await dialog(page, 'Estado de cuenta');
    await expect(account.getByText(/\(solo para este cliente\)/)).toBeVisible();
    await expect(account.getByText(/^Último recordatorio por WhatsApp/)).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-recordatorio');
  });

  test('clientes con su plazo', async ({ page }) => {
    await open(page, '/customers');
    await expect(page.getByText('Paga en 30 días')).toBeVisible();
    await snap(page, 'clientes-plazo-escritorio');
  });

  test('fiado de un cliente con su plazo', async ({ page }) => {
    await open(page, '/customers');
    await page.getByRole('button', { name: 'Fiado de Roberto Sánchez Vega' }).click();
    const credit = await dialog(page, 'Fiado de Roberto Sánchez Vega');
    await expect(credit.getByText('· 15 días')).toBeVisible();
    await expect(credit.getByLabel('Días para pagar')).toHaveValue('7');
    await snapScreen(page, 'clientes-fiado-plazo-propio');
  });

  test('plazo para un cliente sin fiado en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers');
    await page.getByRole('button', { name: 'Fiado de Juan Carlos Hernández' }).click();
    const credit = await dialog(page, 'Fiado de Juan Carlos Hernández');
    await credit.getByLabel('Un plazo para este cliente').check();
    await credit.getByLabel('Días para pagar').fill('10');
    await expect(credit.getByRole('button', { name: 'Guardar' })).toBeEnabled();
    await snapScreen(page, 'clientes-fiado-plazo-telefono');
  });

  test('campañas con los recordatorios del fiado', async ({ page }) => {
    await open(page, '/campaigns?tab=creditReminders');
    await expect(page.getByRole('tab', { name: 'Recordatorios del fiado' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByText('Ya no tiene nada vencido.')).toBeVisible();
    await snap(page, 'campanas-recordatorios-del-fiado-escritorio');
  });

  test('plantilla de recordatorio del fiado', async ({ page }) => {
    await open(page, '/campaigns?tab=templates');
    await page.getByRole('button', { name: 'Nueva plantilla' }).click();
    const template = await dialog(page, 'Nueva plantilla');
    await template.getByLabel('Uso').selectOption({ label: 'Recordatorio del fiado' });
    await expect(template.getByRole('button', { name: '{vencido}' })).toBeVisible();
    await snapScreen(page, 'campanas-plantilla-recordatorio-del-fiado');
  });
});

test.describe('Recordatorios sin WhatsApp', () => {
  test.use({ prepareData: SIN_WHATSAPP });

  test('por cobrar sin plazo del negocio', async ({ page }) => {
    await open(page, '/customers/receivables');
    await expect(page.getByText('Sin plazo del negocio')).toBeVisible();
    await expect(page.getByText('Sin recordatorios')).toBeVisible();
    await snap(page, 'por-cobrar-plazo-propio-escritorio');
  });

  test('recordatorios sin whatsapp en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    const reminders = await openReminders(page, 'Activar los recordatorios');
    await expect(reminders.getByRole('link', { name: 'Ir a WhatsApp' })).toBeVisible();
    await reminders.getByLabel('Recordar lo vencido por WhatsApp').check();
    await expect(reminders.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    await snapScreen(page, 'por-cobrar-recordatorios-sin-whatsapp-telefono');
  });
});
