import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { dialog, open, snap } from '../helpers';
import { CON_AVISO } from './recordatorios.mocks';

// El aviso antes de vencer del fiado (Fase 26E): Por cobrar with the notice next to the reminders, the two sections of the
// reminders, the account with its last notice and, in Campañas, the notices sent and the template for them.

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

test.describe('Aviso antes de vencer', () => {
  test.use({ prepareData: CON_AVISO });

  test('por cobrar con el aviso', async ({ page }) => {
    await open(page, '/customers/receivables');
    await expect(page.getByText('Aviso 2 días antes de vencer · recordatorios cada 7 días')).toBeVisible();
    await snap(page, 'por-cobrar-aviso-escritorio');
  });

  test('recordatorios con el aviso', async ({ page }) => {
    await open(page, '/customers/receivables');
    await page.getByRole('button', { name: 'Cambiar los recordatorios' }).click();
    const reminders = await dialog(page, 'Recordatorios del fiado');
    const notice = reminders.getByRole('group', { name: 'Antes de que venza' });
    await expect(notice.getByLabel('Avisar por WhatsApp antes de que venza')).toBeChecked();
    await expect(notice.getByLabel('Anticipación')).toHaveValue('2');
    await expect(notice.getByLabel('Mensaje')).toHaveValue('t-por-vencer');
    await expect(notice.getByText('Hola Ana, el lunes 5 de octubre vence $500.00')).toBeVisible();
    await snapScreen(page, 'por-cobrar-recordatorios-con-aviso');
  });

  test('estado de cuenta con su último aviso', async ({ page }) => {
    await open(page, '/customers/receivables');
    await page.getByRole('button', { name: 'Ver cuenta de Oficinas Grupo Altavista' }).click();
    const account = await dialog(page, 'Estado de cuenta');
    await expect(account.getByText(/^Último aviso antes de vencer por WhatsApp/)).toBeVisible();
    await snapScreen(page, 'por-cobrar-estado-de-cuenta-aviso');
  });

  test('campañas con los avisos antes de vencer', async ({ page }) => {
    await open(page, '/campaigns?tab=creditReminders');
    await expect(page.getByRole('heading', { name: 'Avisos antes de vencer' })).toBeVisible();
    await expect(page.getByText(/^Hola Oficinas, el sábado 3 de octubre vence/)).toBeVisible();
    await snap(page, 'campanas-avisos-antes-de-vencer-escritorio');
  });

  test('plantilla de aviso antes de vencer', async ({ page }) => {
    await open(page, '/campaigns?tab=templates');
    await page.getByRole('button', { name: 'Nueva plantilla' }).click();
    const template = await dialog(page, 'Nueva plantilla');
    await template.getByLabel('Uso').selectOption({ label: 'Aviso antes de vencer' });
    await expect(template.getByRole('button', { name: '{porvencer}' })).toBeVisible();
    await snapScreen(page, 'campanas-plantilla-aviso-antes-de-vencer');
  });
});
