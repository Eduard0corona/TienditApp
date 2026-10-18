import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap, snapElement } from '../helpers';
import {
  AGENDA_SOLICITUDES,
  APPOINTMENT_LINK,
  CITAS_PRENDIDAS,
  CITAS_ROUTES,
  EXPLORAR_AGENDA,
  MENU_AGENDA,
} from './citas-en-linea.mocks';
import { ACCOUNT_PROFILE } from './cuenta-cliente.mocks';

// Citas en línea (Fase 24D): the booking page of Café La Esquina (a service, a day and a free time, the customer's data and
// the receipt), the appointment followed with its link, Mis citas of María Fernanda's account, the requests in the agenda
// of the business and the owner's card in Configuración.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

/** The booking page with the cata picked, its week open on Monday. */
async function openCata(page: Page) {
  await open(page, '/m/cafe-la-esquina/agendar');
  await page.getByRole('button', { name: /Cata de café de especialidad/ }).click();
  await expect(page.getByRole('button', { name: /Lunes 5 de octubre: \d+ horarios libres/ })).toHaveAttribute('aria-pressed', 'true');
}

/** Monday at 4:00 p. m., and the sheet with the customer's data. */
async function pickMondayAfternoon(page: Page) {
  await page.getByRole('button', { name: '4:00 p. m.' }).click();
  await page.getByRole('button', { name: 'Continuar · Lun 5 · 4:00 p. m.' }).click();
  return dialog(page, 'Tu cita');
}

// ------------------------------------------------------------------ The booking page, as a guest

test.describe('Agendar en línea', () => {
  test.use({ signedIn: false, extraRoutes: CITAS_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`agendar servicios ${look.suffix}`, async ({ page }) => {
        await open(page, '/m/cafe-la-esquina/agendar');
        await expect(page.getByRole('button', { name: /Taller de barismo básico.*3 h/ })).toBeVisible();
        await snap(page, `agendar-servicios-${look.suffix}`);
      });

      test(`agendar horarios ${look.suffix}`, async ({ page }) => {
        await openCata(page);
        await page.getByRole('button', { name: '4:00 p. m.' }).click();
        await expect(page.getByRole('button', { name: 'Continuar · Lun 5 · 4:00 p. m.' })).toBeVisible();
        await snap(page, `agendar-horarios-${look.suffix}`);
      });
    });
  }

  test('agendar tus datos', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await openCata(page);
    const sheet = await pickMondayAfternoon(page);
    await sheet.getByLabel('Tu nombre').fill('Valeria Ruiz');
    await sheet.getByLabel('Teléfono (WhatsApp)').fill('55 2210 7765');
    await snap(page, 'agendar-tus-datos-telefono', { fullPage: false });
  });

  test('agendar solicitud enviada', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await openCata(page);
    const sheet = await pickMondayAfternoon(page);
    await sheet.getByLabel('Tu nombre').fill('Valeria Ruiz');
    await sheet.getByLabel('Teléfono (WhatsApp)').fill('55 2210 7765');
    await sheet.getByRole('button', { name: 'Pedir cita' }).click();
    await expect(page.getByRole('heading', { name: '¡Solicitud enviada!' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Copiar enlace de tu cita' })).toBeVisible();
    await snap(page, 'agendar-solicitud-enviada-telefono');
  });

  test('agendar la semana siguiente', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await openCata(page);
    await page.getByRole('button', { name: 'Semana siguiente' }).click();
    await expect(page.getByRole('button', { name: /Domingo 11 de octubre/ })).toHaveAttribute('aria-pressed', 'true');
    await snap(page, 'agendar-semana-siguiente-telefono');
  });

  test('agendar en un negocio que no agenda', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/m/otro-negocio/agendar');
    await expect(page.getByRole('heading', { name: 'Este negocio no agenda citas en línea' })).toBeVisible();
    await snap(page, 'agendar-no-agenda-telefono');
  });
});

// ------------------------------------------------------------------ Where customers find it

test.describe('Agenda tu cita en el menú y en Explorar', () => {
  test.use({ signedIn: false, extraRoutes: CITAS_ROUTES });

  test.describe('menú', () => {
    test.use({ prepareData: MENU_AGENDA });

    test('menu con agenda tu cita', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/m/cafe-la-esquina');
      await expect(page.getByRole('link', { name: 'Agenda tu cita' })).toBeVisible();
      await snap(page, 'menu-agenda-tu-cita-telefono');
    });
  });

  test.describe('explorar', () => {
    test.use({ prepareData: EXPLORAR_AGENDA });

    test('explorar agenda en linea', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/explorar');
      const card = page.getByRole('link', { name: /Estética Bella/ });
      await expect(card.getByText('Agenda en línea')).toBeVisible();
      await snapElement(card, 'explorar-agenda-en-linea-telefono');
    });
  });
});

// ------------------------------------------------------------------ The appointment's link

test.describe('Tu cita con enlace', () => {
  test.use({ signedIn: false, extraRoutes: CITAS_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`tu cita ${look.suffix}`, async ({ page }) => {
        await open(page, `/cita#${APPOINTMENT_LINK}`);
        await expect(page.getByText('Por confirmar')).toBeVisible();
        await expect(page.getByRole('button', { name: /Cancelar la cita de Cata de café de especialidad/ })).toBeVisible();
        await snap(page, `tu-cita-${look.suffix}`);
      });
    });
  }

  test('tu cita cancelar', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, `/cita#${APPOINTMENT_LINK}`);
    await page.getByRole('button', { name: /Cancelar la cita de Cata de café de especialidad/ }).click();
    await dialog(page, 'Cancelar cita');
    await snap(page, 'tu-cita-cancelar-telefono', { fullPage: false });
  });

  test('tu cita sin enlace', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cita');
    await expect(page.getByRole('heading', { name: 'Abre el enlace de tu cita' })).toBeVisible();
    await snap(page, 'tu-cita-sin-enlace-telefono');
  });
});

// ------------------------------------------------------------------ Mis citas of the account

test.describe('Mis citas', () => {
  test.use({ profile: ACCOUNT_PROFILE, extraRoutes: CITAS_ROUTES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`mis citas ${look.suffix}`, async ({ page }) => {
        await open(page, '/mis-citas');
        await expect(page.getByRole('region', { name: 'Próximas' })).toContainText('Por confirmar');
        await expect(page.getByRole('region', { name: 'Anteriores' })).toContainText('La cancelaste');
        await snap(page, `mis-citas-${look.suffix}`);
      });
    });
  }

  test('agendar con la cuenta', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await openCata(page);
    const sheet = await pickMondayAfternoon(page);
    await expect(sheet.getByLabel('Tu nombre')).toHaveValue('María Fernanda López');
    await sheet.getByRole('button', { name: 'Pedir cita' }).click();
    await expect(page.getByText('La encuentras en tus citas.')).toBeVisible();
    await snap(page, 'agendar-con-la-cuenta-telefono');
  });
});

// ------------------------------------------------------------------ The business: its agenda and its settings

test.describe('Solicitudes en la agenda', () => {
  test.use({ prepareData: AGENDA_SOLICITUDES });

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`agenda solicitudes ${look.suffix}`, async ({ page }) => {
        await open(page, '/appointments');
        await expect(page.getByText(/Tienes 2 solicitudes de cita por confirmar/)).toBeVisible();
        await page.getByRole('button', { name: 'Ver solicitudes' }).click();
        await expect(page.getByRole('button', { name: 'Aceptar la cita de María Fernanda López' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Aceptar la cita de Jorge Ibarra' })).toBeVisible();
        await snap(page, `agenda-solicitudes-${look.suffix}`);
      });
    });
  }

  test('agenda aviso de solicitudes', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/appointments?estado=por-confirmar');
    await expect(page.getByLabel('Estado')).toHaveValue('5');
    await page.getByLabel('Estado').selectOption({ label: 'Todos' });
    await expect(page.getByText(/Tienes 2 solicitudes de cita por confirmar/)).toBeVisible();
    await expect(page.getByText('La canceló el cliente')).toBeVisible();
    await snap(page, 'agenda-aviso-solicitudes-escritorio');
  });

  test('agenda rechazar solicitud', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/appointments');
    await page.getByRole('button', { name: 'Ver solicitudes' }).click();
    await page.getByRole('button', { name: 'Rechazar la cita de Jorge Ibarra' }).click();
    await dialog(page, 'Rechazar la solicitud');
    await snap(page, 'agenda-rechazar-solicitud-escritorio', { fullPage: false });
  });
});

test.describe('Citas en línea en Configuración', () => {
  test('configuracion citas en linea apagadas', async ({ page }) => {
    await page.setViewportSize(SIZES.desktop);
    await open(page, '/settings/business');
    const card = page.getByRole('region', { name: 'Citas en línea' });
    await expect(card.getByText(/Las citas en línea están apagadas/)).toBeVisible();
    await snapElement(card, 'configuracion-citas-apagadas-escritorio');
  });

  test.describe('prendidas', () => {
    test.use({ prepareData: CITAS_PRENDIDAS });

    test('configuracion citas en linea prendidas', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/settings/business');
      const card = page.getByRole('region', { name: 'Citas en línea' });
      await expect(card.getByRole('link', { name: 'Ver tu página de citas' })).toBeVisible();
      await snapElement(card, 'configuracion-citas-prendidas-telefono');
    });
  });
});
