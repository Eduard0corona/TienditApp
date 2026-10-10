import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap, snapElement } from '../helpers';

// Customers and messages: Clientes, Mensajes (WhatsApp chat), Campañas, Agenda, Taller and Respuestas automáticas.
// Every screen at the three base looks, then the dialogs and panels each one opens. A dialog is taken at the size of
// the screen (not the whole page): its backdrop covers the screen, and the shot shows it where the person sees it.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

// These dialogs do not link their title to the dialog (no accessible name): each one is found as the dialog that
// opened, then checked by its heading.

/** A dialog or side panel as the screen shows it. */
async function snapDialog(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/** The panel of the open conversation, the part of the chat a phone shows once the person picks one. */
function conversationPanel(page: Page): Locator {
  // No accessible handle holds the thread, its header and the reply box together: the panel's app class does.
  return page.locator('.whatsapp-messages-panel');
}

/** Clientes: the card of María Fernanda López, with her orders, notes and delivery address. */
async function openCustomerCard(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Ficha de María Fernanda López' }).click();
  const card = await dialog(page);
  await expect(card.getByRole('heading', { name: 'Ficha del cliente' })).toBeVisible();
  // The card loads after the dialog opens ("Cargando ficha…" is not the generic loading text settle waits for).
  await expect(card.getByText('Últimos pedidos')).toBeVisible();
  await expect(card.getByText(/^Av\. Insurgentes Sur 1235/)).toBeVisible();
  return card;
}

/**
 * Mensajes: selects a conversation and waits for its messages. The thread scrolls smoothly to its last message, and the
 * page with it. A smooth scroll runs apart from the page, and a full-page shot taken after it can still draw the menu a
 * few pixels down, so here it jumps straight to the end: the same place, without the ride. The thread has no
 * accessible handle, so its app class is the one way to read its scroll.
 */
async function openConversation(page: Page, name: string, lastMessage: string) {
  await page.evaluate(() => {
    const scrollIntoView = Element.prototype.scrollIntoView;
    Element.prototype.scrollIntoView = function (options?: boolean | ScrollIntoViewOptions) {
      scrollIntoView.call(this, typeof options === 'object' ? { ...options, behavior: 'instant' } : options);
    };
  });
  await page.getByRole('button', { name: new RegExp(name) }).click();
  // The header of a selected conversation offers its customer card.
  await expect(page.getByRole('button', { name: /^Ficha/ })).toBeVisible();
  await expect(page.getByText(lastMessage, { exact: true }).last()).toBeVisible();
  await page.waitForFunction(() => {
    const thread = document.querySelector('.wa-messages');
    return !!thread && Math.abs(thread.scrollHeight - thread.clientHeight - thread.scrollTop) < 1;
  });
}

/** The conversation of María Fernanda: her order for pickup, read and delivered ticks and her question still open. */
async function openMaferConversation(page: Page) {
  await openConversation(page, 'María Fernanda López', '¿Ya está lista mi orden para llevar?');
}

/** Mensajes: the customer card beside the conversation. */
async function openChatCard(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: /^Ficha/ }).click();
  const card = await dialog(page);
  await expect(card.getByRole('heading', { name: 'Ficha del cliente' })).toBeVisible();
  await expect(card.getByText('Lo que más pide')).toBeVisible();
  return card;
}

/** Campañas: the detail of the campaign being sent, with each message and its state. */
async function openCampaignDetail(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: '2x1 lattes sábado' }).click();
  const detail = await dialog(page);
  await expect(detail.getByRole('heading', { name: '2x1 lattes sábado' })).toBeVisible();
  await expect(detail.getByText('Luis Ángel Martínez')).toBeVisible();
  return detail;
}

/** Campañas: a new campaign with its name and the preview of who receives it. */
async function fillNewCampaign(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Nueva campaña' }).click();
  const form = await dialog(page);
  await expect(form.getByRole('heading', { name: 'Nueva campaña' })).toBeVisible();
  await form.getByLabel('Nombre').fill('Pan de muerto en octubre');
  await form.getByRole('button', { name: '¿A cuántos llega?' }).click();
  await expect(form.getByText(/^Llega a/)).toBeVisible();
  return form;
}

/** Agenda: the new appointment form once the services and the team have loaded. */
async function openNewAppointment(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: 'Nueva cita' }).click();
  const form = await dialog(page);
  await expect(form.getByRole('heading', { name: 'Nueva cita' })).toBeVisible();
  await expect(form.getByRole('option', { name: 'Cata de café de especialidad' })).toBeAttached();
  await expect(form.getByRole('option', { name: 'Carlos Méndez' })).toBeAttached();
  return form;
}

/** A tasting for Karla on Tuesday at 5 p.m.: the end follows the service's 90 minutes. */
async function fillNewAppointment(form: Locator) {
  await form.getByLabel('Cliente *').fill('Karla Mendoza');
  await form.getByLabel('Teléfono *').fill('55 3021 7788');
  await form.getByLabel('Servicio *').selectOption({ label: 'Cata de café de especialidad' });
  await form.getByLabel('Atiende').selectOption({ label: 'Carlos Méndez' });
  await form.getByLabel('Inicio *').fill('2026-10-06T17:00');
  await expect(form.getByLabel('Fin *')).toHaveValue('2026-10-06T18:30');
  await form.getByLabel('Notas').fill('Viene con dos amigas; prefieren orígenes de Chiapas.');
}

/** Taller: the De'Longhi in progress, with its checklist and the other piece of the same order. */
async function openPieceDetail(page: Page): Promise<Locator> {
  await page.getByRole('button', { name: "Cafetera De'Longhi Dedica EC685" }).click();
  const panel = await dialog(page);
  await expect(panel.getByRole('heading', { name: /^Cafetera De'Longhi Dedica EC685/ })).toBeVisible();
  await expect(panel.getByRole('heading', { name: 'Otras piezas del pedido' })).toBeVisible();
  return panel;
}

for (const look of LOOKS) {
  test.describe(`Clientes y mensajes ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`clientes ${look.suffix}`, async ({ page }) => {
      await open(page, '/customers');
      await expect(page.getByRole('button', { name: 'Ficha de María Fernanda López' })).toBeVisible();
      await snap(page, `clientes-${look.suffix}`);
    });

    test(`mensajes ${look.suffix}`, async ({ page }) => {
      await open(page, '/whatsapp/chat');
      await expect(page.getByRole('button', { name: /María Fernanda López/ })).toBeVisible();
      await snap(page, `mensajes-${look.suffix}`);
    });

    test(`campañas ${look.suffix}`, async ({ page }) => {
      await open(page, '/campaigns');
      await expect(page.getByRole('button', { name: '2x1 lattes sábado' })).toBeVisible();
      await snap(page, `campanas-${look.suffix}`);
    });

    test(`agenda ${look.suffix}`, async ({ page }) => {
      await open(page, '/appointments');
      await expect(page.getByRole('button', { name: 'Editar o reprogramar la cita de Daniela Torres' })).toBeVisible();
      await snap(page, `agenda-${look.suffix}`);
    });

    test(`taller ${look.suffix}`, async ({ page }) => {
      await open(page, '/services/board');
      await expect(page.getByRole('button', { name: "Cafetera De'Longhi Dedica EC685" })).toBeVisible();
      await snap(page, `taller-${look.suffix}`);
    });

    test(`respuestas automáticas ${look.suffix}`, async ({ page }) => {
      await open(page, '/ai-agent');
      await expect(page.getByLabel('Nombre', { exact: true })).toHaveValue('Esquinita');
      await snap(page, `respuestas-automaticas-${look.suffix}`);
    });
  });
}

test.describe('Clientes', () => {
  test('ficha del cliente', async ({ page }) => {
    await open(page, '/customers');
    await openCustomerCard(page);
    await snapDialog(page, 'clientes-ficha-escritorio');
  });

  test('ficha del cliente en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/customers');
    await openCustomerCard(page);
    await snapDialog(page, 'clientes-ficha-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('ficha del cliente oscuro', async ({ page }) => {
      await open(page, '/customers');
      await openCustomerCard(page);
      await snapDialog(page, 'clientes-ficha-escritorio-oscuro');
    });
  });
});

test.describe('Mensajes', () => {
  test('conversación abierta', async ({ page }) => {
    await open(page, '/whatsapp/chat');
    await openMaferConversation(page);
    await snap(page, 'mensajes-conversacion-escritorio');
  });

  test('conversación con las respuestas automáticas en pausa', async ({ page }) => {
    await open(page, '/whatsapp/chat');
    // Valeria: someone of the team took over (the bot is paused), a photo came in and the last reply is only sent.
    await openConversation(page, 'Valeria Morales', 'Ya está lista, te esperamos en caja 🙂');
    await snap(page, 'mensajes-conversacion-en-pausa-escritorio');
  });

  test('ficha del cliente en el chat', async ({ page }) => {
    await open(page, '/whatsapp/chat');
    await openMaferConversation(page);
    await openChatCard(page);
    await snapDialog(page, 'mensajes-ficha-escritorio');
  });

  test('conversación en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/whatsapp/chat');
    await openMaferConversation(page);
    await snapElement(conversationPanel(page), 'mensajes-conversacion-telefono');
  });

  test('ficha del cliente en el chat en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/whatsapp/chat');
    await openMaferConversation(page);
    await openChatCard(page);
    await snapDialog(page, 'mensajes-ficha-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    // The thread has its own dark colors (WhatsAppChat.css) instead of light bubbles on a dark page.
    test('conversación abierta oscuro', async ({ page }) => {
      await open(page, '/whatsapp/chat');
      await openMaferConversation(page);
      await snap(page, 'mensajes-conversacion-escritorio-oscuro');
    });
  });
});

test.describe('Campañas', () => {
  test('detalle de una campaña', async ({ page }) => {
    await open(page, '/campaigns');
    await openCampaignDetail(page);
    await snapDialog(page, 'campanas-detalle-escritorio');
  });

  test('detalle de una campaña en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/campaigns');
    await openCampaignDetail(page);
    await snapDialog(page, 'campanas-detalle-telefono');
  });

  test('campaña nueva', async ({ page }) => {
    await open(page, '/campaigns');
    await page.getByRole('button', { name: 'Nueva campaña' }).click();
    const form = await dialog(page);
    await expect(form.getByRole('heading', { name: 'Nueva campaña' })).toBeVisible();
    await snapDialog(page, 'campanas-nueva-escritorio');
  });

  test('campaña nueva con su vista previa', async ({ page }) => {
    await open(page, '/campaigns');
    await fillNewCampaign(page);
    await snapDialog(page, 'campanas-nueva-vista-previa-escritorio');
  });

  test('campaña nueva en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/campaigns');
    await fillNewCampaign(page);
    await snapDialog(page, 'campanas-nueva-vista-previa-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('detalle de una campaña oscuro', async ({ page }) => {
      await open(page, '/campaigns');
      await openCampaignDetail(page);
      await snapDialog(page, 'campanas-detalle-escritorio-oscuro');
    });
  });
});

test.describe('Agenda', () => {
  test('cita nueva', async ({ page }) => {
    await open(page, '/appointments');
    await openNewAppointment(page);
    await snapDialog(page, 'agenda-nueva-escritorio');
  });

  test('cita nueva llena', async ({ page }) => {
    await open(page, '/appointments');
    const form = await openNewAppointment(page);
    await fillNewAppointment(form);
    await snapDialog(page, 'agenda-nueva-llena-escritorio');
  });

  test('cita nueva en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/appointments');
    const form = await openNewAppointment(page);
    await fillNewAppointment(form);
    await snapDialog(page, 'agenda-nueva-llena-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('cita nueva oscuro', async ({ page }) => {
      await open(page, '/appointments');
      const form = await openNewAppointment(page);
      await fillNewAppointment(form);
      await snapDialog(page, 'agenda-nueva-llena-escritorio-oscuro');
    });
  });
});

test.describe('Taller', () => {
  test('detalle de una pieza', async ({ page }) => {
    await open(page, '/services/board');
    await openPieceDetail(page);
    await snapDialog(page, 'taller-pieza-escritorio');
  });

  test('detalle de una pieza en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/services/board');
    await openPieceDetail(page);
    await snapDialog(page, 'taller-pieza-telefono');
  });

  test.describe('Oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('detalle de una pieza oscuro', async ({ page }) => {
      await open(page, '/services/board');
      await openPieceDetail(page);
      await snapDialog(page, 'taller-pieza-escritorio-oscuro');
    });
  });
});

test.describe('Respuestas automáticas', () => {
  test('mensajes de las respuestas', async ({ page }) => {
    await open(page, '/ai-agent');
    await page.getByRole('button', { name: 'Mensajes', exact: true }).click();
    await expect(page.getByLabel('Mensaje de bienvenida')).toHaveValue(/^¡Hola! Soy Esquinita/);
    await snap(page, 'respuestas-automaticas-mensajes-escritorio');
  });

  test('respuestas inactivas', async ({ page }) => {
    await open(page, '/ai-agent');
    await page.getByRole('button', { name: /^Agente de eventos/ }).click();
    await expect(page.getByLabel('Nombre', { exact: true })).toHaveValue('Agente de eventos');
    await expect(page.getByRole('button', { name: 'Activar', exact: true })).toBeVisible();
    await snap(page, 'respuestas-automaticas-inactivas-escritorio');
  });
});
