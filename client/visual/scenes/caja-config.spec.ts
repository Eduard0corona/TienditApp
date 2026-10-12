import type { Locator, Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap, snapElement } from '../helpers';
import {
  CAJA_DATA,
  CAJA_ROUTES,
  CASHIER_PROFILE,
  CORTE_CON_PLATAFORMA_DATA,
  MENU_EN_LINEA_DATA,
  NEGOCIO_DATA,
  PLATAFORMAS_ROUTES,
  SIN_CAJA_DATA,
  SUPER_ADMIN_PROFILE,
  VENTAS_POR_CANAL_ROUTES,
} from './caja-config.mocks';
import { PRINCIPAL_SOLO_A_DOMICILIO } from './publico-cuenta-marco.mocks';

// Caja, Reportes, Auditoría and Configuración (Mi negocio, WhatsApp, Sucursales, Equipo, Perfil, Plataforma, Planes and
// the guided start): every screen at the three base looks, then the dialogs, tabs and printed cuts each one has. A
// dialog is taken at the size of the screen (not the whole page): its backdrop covers the screen, not the page.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ extraRoutes: CAJA_ROUTES, prepareData: CAJA_DATA });

/** A dialog or side panel as the screen shows it. */
async function snapDialog(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/**
 * Prints a cut the way the cashier does: the print dialog itself is not part of the scene (window.print does nothing),
 * the page is printed as a 72 mm ticket with only the cut visible.
 */
async function printCut(page: Page, button: Locator) {
  await page.evaluate(() => {
    window.print = () => undefined;
  });
  await button.click();
  await page.emulateMedia({ media: 'print' });
}

/** Opens a tab of Caja and waits until it shows its data, not its empty state. */
async function openCashTab(page: Page, name: string, loaded: Locator) {
  await page.getByRole('tab', { name }).click();
  await expect(page.getByRole('tab', { name })).toHaveAttribute('aria-selected', 'true');
  await expect(loaded).toBeVisible();
}

/** Scrolls a dialog taller than the screen to its end, for a shot of what is below. */
async function scrollToEnd(panel: Locator) {
  await panel.evaluate((root) => {
    for (const element of [root, ...root.querySelectorAll('*')]) element.scrollTop = element.scrollHeight;
  });
}

/** The row of a shift in the history, by what sets it apart (its difference, its branch). */
function shiftRow(page: Page, text: string) {
  return page.getByRole('row').filter({ hasText: text });
}

for (const look of LOOKS) {
  test.describe(`Caja y configuración ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`caja ${look.suffix}`, async ({ page }) => {
      await open(page, '/cash');
      await expect(page.getByText('Retiro a caja fuerte')).toBeVisible();
      await snap(page, `caja-${look.suffix}`);
    });

    test(`reportes ${look.suffix}`, async ({ page }) => {
      await open(page, '/reports');
      await expect(page.getByText('Ingresos por método de pago')).toBeVisible();
      await expect(page.getByRole('cell', { name: 'Sucursal Coyoacán' })).toBeVisible();
      await snap(page, `reportes-${look.suffix}`);
    });

    test(`auditoría ${look.suffix}`, async ({ page }) => {
      await open(page, '/audit');
      await expect(page.getByRole('cell', { name: 'whatsapp-agent' })).toBeVisible();
      await snap(page, `auditoria-${look.suffix}`);
    });

    test(`whatsapp ${look.suffix}`, async ({ page }) => {
      await open(page, '/settings/whatsapp');
      await expect(page.getByText('WhatsApp Business').first()).toBeVisible();
      // The callback URL carries the address the scenes are served from (its port changes with VISUAL_PORT).
      await snap(page, `whatsapp-${look.suffix}`, { mask: [page.getByLabel('URL de devolución de llamada (Callback URL)')] });
    });

    test(`sucursales ${look.suffix}`, async ({ page }) => {
      await open(page, '/branches');
      await expect(page.getByRole('button', { name: 'Editar Kiosko Parque México' })).toBeVisible();
      await snap(page, `sucursales-${look.suffix}`);
    });

    test(`equipo ${look.suffix}`, async ({ page }) => {
      await open(page, '/employees');
      await expect(page.getByRole('button', { name: 'Editar a Iván Rosales' })).toBeVisible();
      await snap(page, `equipo-${look.suffix}`);
    });

    test(`perfil ${look.suffix}`, async ({ page }) => {
      await open(page, '/profile');
      await expect(page.getByText('Cambiar contraseña').first()).toBeVisible();
      await snap(page, `perfil-${look.suffix}`);
    });

    test(`arranque guiado ${look.suffix}`, async ({ page }) => {
      await open(page, '/setup');
      await expect(page.getByRole('heading', { name: '¿Qué vas a usar?' })).toBeVisible();
      await snap(page, `arranque-${look.suffix}`);
    });

    test.describe('Mi negocio', () => {
      test.use({ prepareData: NEGOCIO_DATA });

      test(`negocio ${look.suffix}`, async ({ page }) => {
        await open(page, '/settings/business');
        await expect(page.getByRole('button', { name: 'Quitar horario' })).toBeVisible();
        await expect(page.getByText('Dirección fiscal')).toBeVisible();
        await snap(page, `negocio-${look.suffix}`);
      });
    });

    test.describe('Plataforma', () => {
      test.use({ profile: SUPER_ADMIN_PROFILE });

      test(`plataforma ${look.suffix}`, async ({ page }) => {
        await open(page, '/platform');
        await expect(page.getByText('Ferretería Hidalgo')).toBeVisible();
        await snap(page, `plataforma-${look.suffix}`);
      });

      test(`planes ${look.suffix}`, async ({ page }) => {
        await open(page, '/platform/plans');
        await expect(page.getByRole('button', { name: 'Editar Cadena' })).toBeVisible();
        await snap(page, `planes-${look.suffix}`);
      });
    });
  });
}

test.describe('Caja', () => {
  test('caja, registrar movimiento', async ({ page }) => {
    await open(page, '/cash');
    await page.getByRole('button', { name: 'Gasto, retiro o depósito' }).click();
    await dialog(page, 'Registrar movimiento');
    await snapDialog(page, 'caja-movimiento-dialogo');
  });

  test('caja, cerrar caja', async ({ page }) => {
    await open(page, '/cash');
    await page.getByRole('button', { name: 'Cerrar caja' }).click();
    await expect(await dialog(page, 'Cerrar caja')).toContainText('Efectivo esperado');
    await snapDialog(page, 'caja-cerrar-dialogo');
  });

  test('caja, cerrar caja en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cash');
    await page.getByRole('button', { name: 'Cerrar caja' }).click();
    await expect(await dialog(page, 'Cerrar caja')).toContainText('Efectivo esperado');
    await snapDialog(page, 'caja-cerrar-dialogo-telefono');
  });

  test('caja, cerrar caja con diferencia', async ({ page }) => {
    // A count that does not match: the difference shows and the note becomes required before closing.
    await open(page, '/cash');
    await page.getByRole('button', { name: 'Cerrar caja' }).click();
    const closing = await dialog(page, 'Cerrar caja');
    await closing.getByLabel('$500.00').fill('3');
    await closing.getByLabel('$200.00').fill('2');
    await expect(closing.getByText('Para cerrar con diferencia escribe qué pasó.')).toBeVisible();
    await snapDialog(page, 'caja-cerrar-con-diferencia');
  });

  test('caja cerrada con su corte', async ({ page }) => {
    await open(page, '/cash');
    await page.getByRole('button', { name: 'Cerrar caja' }).click();
    const closing = await dialog(page, 'Cerrar caja');
    await closing.getByRole('button', { name: 'Escribir el total' }).click();
    await closing.getByLabel('Efectivo contado').fill('1800');
    await closing.getByLabel('Notas (explica la diferencia)').fill('Faltaron monedas de cambio');
    await closing.getByRole('button', { name: 'Cerrar caja' }).click();
    await expect(page.getByRole('heading', { name: 'Corte de tu caja' })).toBeVisible();
    // The notice of the close leaves on a timer: closed by hand, the shot does not depend on when it is taken.
    await page.getByRole('button', { name: 'Cerrar aviso' }).click();
    await expect(page.getByRole('button', { name: 'Cerrar aviso' })).toHaveCount(0);
    await snap(page, 'caja-cerrada-con-corte');

    await printCut(page, page.getByRole('button', { name: 'Imprimir corte' }));
    await snap(page, 'caja-corte-impreso');
  });

  test.describe('sin abrir', () => {
    test.use({ prepareData: SIN_CAJA_DATA });

    test('caja sin abrir', async ({ page }) => {
      await open(page, '/cash');
      await expect(page.getByRole('heading', { name: 'Abre tu caja' })).toBeVisible();
      await snap(page, 'caja-sin-abrir');
    });

    test('caja sin abrir en el teléfono', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/cash');
      await expect(page.getByRole('heading', { name: 'Abre tu caja' })).toBeVisible();
      await snap(page, 'caja-sin-abrir-telefono');
    });
  });

  test('caja, turnos', async ({ page }) => {
    await open(page, '/cash');
    await openCashTab(page, 'Turnos', page.getByText('Faltan $50.00'));
    await snap(page, 'caja-turnos');
  });

  test('caja, turnos en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cash');
    await openCashTab(page, 'Turnos', page.getByText('Faltan $50.00'));
    await snap(page, 'caja-turnos-telefono');
  });

  test('caja, detalle de un turno cerrado', async ({ page }) => {
    await open(page, '/cash');
    await openCashTab(page, 'Turnos', page.getByText('Faltan $50.00'));
    await shiftRow(page, 'Faltan $50.00').getByRole('button').click();
    const detail = await dialog(page, 'Turno de carlos.mendez@cafelaesquina.mx');
    // Its movements load after the dialog opens.
    await expect(detail.getByText('Hielo (6 bolsas)')).toBeVisible();
    await snapDialog(page, 'caja-turno-detalle-dialogo');
  });

  test('caja, corte impreso de un turno', async ({ page }) => {
    await open(page, '/cash');
    await openCashTab(page, 'Turnos', page.getByText('Faltan $50.00'));
    await shiftRow(page, 'Faltan $50.00').getByRole('button').click();
    const detail = await dialog(page, 'Turno de carlos.mendez@cafelaesquina.mx');
    await expect(detail.getByText('Hielo (6 bolsas)')).toBeVisible();
    await printCut(page, detail.getByRole('button', { name: 'Imprimir corte' }));
    await snap(page, 'caja-turno-corte-impreso');
  });

  test('caja, caja abierta de otro cajero', async ({ page }) => {
    // An owner can close the drawer another cashier left open, from its detail.
    await open(page, '/cash');
    await openCashTab(page, 'Turnos', page.getByText('Faltan $50.00'));
    await shiftRow(page, 'Sucursal Coyoacán').filter({ hasText: 'En curso' }).getByRole('button').click();
    const detail = await dialog(page, 'Turno de carlos.mendez@cafelaesquina.mx');
    await expect(detail.getByText('Pago de pan')).toBeVisible();
    await expect(detail.getByRole('button', { name: 'Cerrar esta caja' })).toBeVisible();
    await snapDialog(page, 'caja-turno-abierto-otro-dialogo');
  });

  test('caja, corte del día', async ({ page }) => {
    await open(page, '/cash');
    await openCashTab(page, 'Corte del día', page.getByRole('heading', { name: 'Por usuario' }));
    await snap(page, 'caja-corte-del-dia');
  });

  test('caja, corte del día en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/cash');
    await openCashTab(page, 'Corte del día', page.getByRole('heading', { name: 'Por usuario' }));
    await snap(page, 'caja-corte-del-dia-telefono');
  });

  test('caja, corte del día impreso', async ({ page }) => {
    await open(page, '/cash');
    await openCashTab(page, 'Corte del día', page.getByRole('heading', { name: 'Por usuario' }));
    await printCut(page, page.getByRole('button', { name: 'Imprimir corte del día' }));
    await snap(page, 'caja-corte-del-dia-impreso');
  });

  test.describe('con plataforma', () => {
    // Rappi charged three orders in its app and gave one back: in "Cobrado", apart from the drawers, and in the printed cut.
    test.use({ prepareData: CORTE_CON_PLATAFORMA_DATA });

    test('caja, corte del día con plataforma', async ({ page }) => {
      await open(page, '/cash');
      await openCashTab(page, 'Corte del día', page.getByText('Lo cobró la plataforma: no entra a la caja'));
      await expect(page.getByText('Devuelto: $350.00')).toBeVisible();
      await snap(page, 'caja-corte-del-dia-plataforma');

      await printCut(page, page.getByRole('button', { name: 'Imprimir corte del día' }));
      await snap(page, 'caja-corte-del-dia-plataforma-impreso');
    });
  });

  test.describe('oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('caja, cerrar caja en oscuro', async ({ page }) => {
      await open(page, '/cash');
      await page.getByRole('button', { name: 'Cerrar caja' }).click();
      await expect(await dialog(page, 'Cerrar caja')).toContainText('Efectivo esperado');
      await snapDialog(page, 'caja-cerrar-dialogo-oscuro');
    });

    test('caja, turnos en oscuro', async ({ page }) => {
      await open(page, '/cash');
      await openCashTab(page, 'Turnos', page.getByText('Faltan $50.00'));
      await snap(page, 'caja-turnos-oscuro');
    });
  });
});

test.describe('Mi negocio', () => {
  test.use({ prepareData: NEGOCIO_DATA });

  test('negocio, editar', async ({ page }) => {
    // The edit mode: the business's details, its fiscal data and its fiscal address become a form.
    await open(page, '/settings/business');
    await page.getByRole('button', { name: 'Editar negocio' }).click();
    await expect(page.getByLabel('Calle')).toHaveValue('Av. Álvaro Obregón');
    await snap(page, 'negocio-editar');
  });

  test('negocio, editar en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/settings/business');
    await page.getByRole('button', { name: 'Editar negocio' }).click();
    await expect(page.getByLabel('Calle')).toHaveValue('Av. Álvaro Obregón');
    await snap(page, 'negocio-editar-telefono');
  });

  test('negocio, días especiales', async ({ page }) => {
    // The special days ahead, every suggested holiday and the past ones, open.
    await open(page, '/settings/business');
    await expect(page.getByRole('list', { name: 'Próximos días especiales' })).toContainText('Vie 25 dic');
    await page.getByRole('button', { name: 'Ver 1 más' }).click();
    await expect(page.getByRole('button', { name: 'Agregar Año Nuevo, viernes 1 de enero' })).toBeVisible();
    await page.getByText('Pasados (últimos 90 días)').click();
    await expect(page.getByRole('list', { name: 'Días especiales pasados' })).toBeVisible();
    await snap(page, 'negocio-dias-especiales');
  });

  test('negocio, día especial (diálogo)', async ({ page }) => {
    // A suggested holiday opens the dialog filled in: Fin de año closes at 18:00, from the Thursday hours.
    await page.setViewportSize(SIZES.phone);
    await open(page, '/settings/business');
    await page.getByRole('button', { name: 'Agregar Fin de año, jueves 31 de diciembre' }).click();
    const form = await dialog(page, 'Nuevo día especial');
    await expect(form.getByLabel('Motivo (opcional)')).toHaveValue('Fin de año');
    await expect(form.getByText('Tus clientes verán: 7:00 a 18:00 · Fin de año')).toBeVisible();
    await snapDialog(page, 'negocio-dia-especial-dialogo-telefono');
  });

  test('negocio, vista previa de un tema', async ({ page }) => {
    // Picking a theme shows it on the whole app before it is saved.
    await open(page, '/settings/business');
    await page.getByText('Jacaranda', { exact: true }).click();
    await expect(page.getByRole('button', { name: 'Guardar tema Jacaranda' })).toBeVisible();
    await snap(page, 'negocio-tema-vista-previa');
  });

  test.describe('menú en línea', () => {
    test.use({ prepareData: MENU_EN_LINEA_DATA });

    test('negocio con el menú en línea', async ({ page }) => {
      await open(page, '/settings/business');
      await expect(page.getByRole('button', { name: 'Terraza 3' })).toBeVisible();
      // The link and its QR carry the address the scenes are served from (its port changes with VISUAL_PORT).
      await snap(page, 'negocio-menu-en-linea', {
        mask: [page.getByRole('img', { name: /^QR del menú/ }), page.getByLabel('Enlace')],
      });
    });
  });

  test.describe('oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('negocio, editar en oscuro', async ({ page }) => {
      await open(page, '/settings/business');
      await page.getByRole('button', { name: 'Editar negocio' }).click();
      await expect(page.getByLabel('Calle')).toHaveValue('Av. Álvaro Obregón');
      await snap(page, 'negocio-editar-oscuro');
    });
  });
});

test.describe('Plataformas de reparto', () => {
  // Rappi takes orders, Uber Eats is turned off and DiDi Food was never set up.
  test.use({ extraRoutes: PLATAFORMAS_ROUTES, prepareData: NEGOCIO_DATA });

  test('negocio, plataformas de reparto', async ({ page }) => {
    await open(page, '/settings/business');
    const card = page.getByRole('region', { name: 'Plataformas de reparto' });
    await expect(card.getByText(
      'Con un pedido de $1,000.00 con IVA al 16 % incluido cobrado por Rappi: comisión e IVA $348.00, retenciones $90.52, te deposita $561.48.',
    )).toBeVisible();
    await snapElement(card, 'negocio-plataformas');

    await card.getByRole('button', { name: 'Editar Rappi' }).click();
    const form = await dialog(page, 'Rappi');
    await expect(form.getByLabel('Comisión (%)')).toHaveValue('30');
    await snapDialog(page, 'negocio-plataforma-dialogo');

    // Below, the fiscal notice and the one about the prices in the app, with its link to Catálogo › Precios en apps.
    await scrollToEnd(form);
    await expect(form.getByRole('link', { name: 'Catálogo › Precios en apps' })).toBeVisible();
    await snapDialog(page, 'negocio-plataforma-dialogo-final');
  });

  test('negocio, plataforma de reparto en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/settings/business');
    await page.getByRole('button', { name: 'Configurar DiDi Food' }).click();
    const form = await dialog(page, 'DiDi Food');
    await expect(form.getByRole('switch', { name: 'Recibo pedidos de DiDi Food' })).toBeChecked();
    await snapDialog(page, 'negocio-plataforma-dialogo-telefono');
  });
});

test.describe('Ventas por canal', () => {
  test.use({ extraRoutes: VENTAS_POR_CANAL_ROUTES });

  test('reportes, ventas por canal', async ({ page }) => {
    await open(page, '/reports');
    const card = page.getByRole('region', { name: 'Ventas por canal' });
    await expect(card.getByText('De Rappi cobraste tú $350.00: su comisión e IVA se descuentan de tu depósito.')).toBeVisible();
    await snapElement(card, 'reportes-ventas-por-canal');

    await card.getByRole('button', { name: 'Ver pedidos de Rappi' }).click();
    const orders = await dialog(page, 'Pedidos de Rappi · 1 al 4 oct.');
    await expect(orders.getByRole('cell', { name: '152412' })).toBeVisible();
    await snapDialog(page, 'reportes-pedidos-de-rappi');
  });
});

test.describe('Sin acceso a Mi negocio', () => {
  // Configuring the business is for owners and admins: a cashier who opens it lands on their own screen (Vender).
  test.use({ profile: CASHIER_PROFILE });

  test('negocio sin acceso, cajero', async ({ page }) => {
    await open(page, '/settings/business');
    await expect(page).toHaveURL(/\/sell$/);
    await snap(page, 'negocio-sin-acceso-cajero');
  });

  test('negocio sin acceso, cajero en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/settings/business');
    await expect(page).toHaveURL(/\/sell$/);
    await snap(page, 'negocio-sin-acceso-cajero-telefono');
  });
});

test.describe('Sucursales', () => {
  test('sucursales, editar la principal', async ({ page }) => {
    // The main branch's address is the one the online menu shows: its form says so.
    await open(page, '/branches');
    await page.getByRole('button', { name: 'Editar Sucursal Roma Norte' }).click();
    const editing = await dialog(page, 'Editar Sucursal Roma Norte');
    await expect(editing.getByText('La dirección de la sucursal principal se muestra en tu menú en línea')).toBeVisible();
    await snapDialog(page, 'sucursales-editar-principal-dialogo');
  });

  test('sucursales, editar la principal en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/branches');
    await page.getByRole('button', { name: 'Editar Sucursal Roma Norte' }).click();
    const editing = await dialog(page, 'Editar Sucursal Roma Norte');
    await expect(editing.getByText('La dirección de la sucursal principal se muestra en tu menú en línea')).toBeVisible();
    await snapDialog(page, 'sucursales-editar-principal-dialogo-telefono');
  });

  test('sucursales, la principal solo a domicilio', async ({ page }) => {
    // Chosen in the form: the notice says the menu keeps its address private.
    await open(page, '/branches');
    await page.getByRole('button', { name: 'Editar Sucursal Roma Norte' }).click();
    const editing = await dialog(page, 'Editar Sucursal Roma Norte');
    await editing.getByRole('radio', { name: 'Solo a domicilio, sin local' }).check();
    await expect(editing.getByText('Tu menú en línea dice «Solo a domicilio» y no muestra esta dirección.')).toBeVisible();
    await snapDialog(page, 'sucursales-principal-solo-a-domicilio-dialogo');
  });

  test.describe('con la principal solo a domicilio', () => {
    test.use({ prepareData: PRINCIPAL_SOLO_A_DOMICILIO });

    test('sucursales, lista con la principal solo a domicilio', async ({ page }) => {
      await open(page, '/branches');
      await expect(page.getByText('Solo a domicilio', { exact: true })).toBeVisible();
      await snap(page, 'sucursales-solo-a-domicilio-escritorio');
    });
  });

  test('sucursales, nueva sucursal', async ({ page }) => {
    await open(page, '/branches');
    await page.getByRole('button', { name: 'Nueva sucursal' }).click();
    const creating = await dialog(page, 'Nueva sucursal');
    await expect(creating.getByRole('button', { name: 'Crear sucursal' })).toBeVisible();
    await snapDialog(page, 'sucursales-nueva-dialogo');
  });

  test.describe('oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('sucursales, editar la principal en oscuro', async ({ page }) => {
      await open(page, '/branches');
      await page.getByRole('button', { name: 'Editar Sucursal Roma Norte' }).click();
      const editing = await dialog(page, 'Editar Sucursal Roma Norte');
      await expect(editing.getByText('La dirección de la sucursal principal se muestra en tu menú en línea')).toBeVisible();
      await snapDialog(page, 'sucursales-editar-principal-dialogo-oscuro');
    });
  });
});

test.describe('Equipo', () => {
  test('equipo, invitar persona', async ({ page }) => {
    await open(page, '/employees');
    await page.getByRole('button', { name: 'Invitar persona' }).click();
    const inviting = await dialog(page, 'Invitar persona');
    await expect(inviting.getByRole('button', { name: 'Invitar', exact: true })).toBeVisible();
    await snapDialog(page, 'equipo-invitar-dialogo');
  });

  test('equipo, invitar persona en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/employees');
    await page.getByRole('button', { name: 'Invitar persona' }).click();
    const inviting = await dialog(page, 'Invitar persona');
    await expect(inviting.getByRole('button', { name: 'Invitar', exact: true })).toBeVisible();
    await snapDialog(page, 'equipo-invitar-dialogo-telefono');
  });

  test('equipo, editar a una persona', async ({ page }) => {
    // A cashier: their role preset and the permissions it brings.
    await open(page, '/employees');
    await page.getByRole('button', { name: 'Editar a Carlos Méndez' }).click();
    const editing = await dialog(page, 'Carlos Méndez');
    await expect(editing.getByRole('button', { name: 'Guardar cambios' })).toBeVisible();
    await snapDialog(page, 'equipo-editar-dialogo');
  });

  test.describe('oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('equipo, invitar persona en oscuro', async ({ page }) => {
      await open(page, '/employees');
      await page.getByRole('button', { name: 'Invitar persona' }).click();
      const inviting = await dialog(page, 'Invitar persona');
      await expect(inviting.getByRole('button', { name: 'Invitar', exact: true })).toBeVisible();
      await snapDialog(page, 'equipo-invitar-dialogo-oscuro');
    });
  });
});
