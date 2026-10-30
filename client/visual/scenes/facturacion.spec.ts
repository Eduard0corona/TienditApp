import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap } from '../helpers';
import {
  CLIENTE_CON_DATOS_FISCALES,
  CUSTOMER_NAME,
  FACTURACION_AUTOFACTURA,
  FACTURACION_CON_LOGO,
  FACTURACION_LISTA,
  FACTURACION_NO_DISPONIBLE,
  FACTURACION_PRUEBAS_POR_VENCER,
  FACTURACION_ROUTES,
  FACTURACION_SIN_CSD,
  ORDER_NUMBER,
  PEDIDO_AUTOFACTURADO,
  PEDIDO_CANCELANDOSE,
  PEDIDO_TIMBRANDO_SIN_TIMBRE,
  PEDIDO_CON_NOTAS_DE_CREDITO,
  PEDIDO_CORREO_RECHAZADO,
  PEDIDO_EN_FACTURA_GLOBAL,
  PEDIDO_EN_PARCIALIDADES,
  PEDIDO_FACTURA_POR_CORREO,
  PEDIDO_FACTURADO,
  PEDIDO_POR_FACTURAR,
  PEDIDO_SUSTITUIBLE,
  PEDIDO_SUSTITUIDO,
  PEDIDO_SUSTITUIDO_CANCELADO,
} from './facturacion.mocks';

// The invoice of an order (Fase 28A, ADR 0045): Configuración › Facturación (ready, without the CSD, in the PAC's test
// environment with the CSD about to expire, and not available), the invoice of ORD-000343 in Pedidos (to stamp for the
// customer's fiscal data, stamped after a refused and a cancelled one, waiting for the customer to accept the cancellation,
// and the dialog to cancel it) and the fiscal data of a customer in Clientes, from their edit dialog. Since the Fase 28C
// (ADR 0047), the invoice in installments of the same order with the payment complements of its payments, and the dialog
// to cancel one; since the Fase 28D (ADR 0048), its refunds with their credit notes, and the dialog to cancel one; since
// the Fase 28E (ADR 0049), the invoice sent by email or refused by the customer's mail server, and «Enviar por correo»;
// since the Fase 28F (ADR 0050), self-invoicing turned on, and the invoice the customer asked for online; since the Fase
// 28G (ADR 0051), the form to replace an invoice with errors, the replaced invoice waiting for its cancellation with
// relation, the dialog to cancel it without relation, and the replaced one once cancelled; since the Fase 28I (ADR 0054),
// the refunds of a sale of a global invoice with their credit notes of that global invoice, and since the Fase 28J (ADR
// 0055) its discount with its own. Since the Fase 28L (ADR 0057), the logo that goes on the PDF, or where to upload it.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ extraRoutes: FACTURACION_ROUTES });

/** A dialog as the screen shows it: its backdrop covers the screen, not the page. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

test.describe('Configuración › Facturación', () => {
  test.describe('lista', () => {
    test.use({ prepareData: FACTURACION_LISTA });

    for (const look of LOOKS) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`facturación lista ${look.suffix}`, async ({ page }) => {
          await open(page, '/settings/invoicing');
          await expect(page.getByText('Listo para facturar')).toBeVisible();
          await snap(page, `facturacion-lista-${look.suffix}`);
        });
      });
    }
  });

  test.describe('sin certificado', () => {
    test.use({ prepareData: FACTURACION_SIN_CSD });

    test('facturación sin certificado', async ({ page }) => {
      await open(page, '/settings/invoicing');
      await expect(page.getByRole('button', { name: 'Subir certificado' })).toBeDisabled();
      await expect(page.getByText('El régimen fiscal del negocio')).toBeVisible();
      await snap(page, 'facturacion-sin-certificado');
    });
  });

  test.describe('en pruebas y por vencer', () => {
    test.use({ prepareData: FACTURACION_PRUEBAS_POR_VENCER });

    test('facturación en pruebas con el certificado por vencer', async ({ page }) => {
      await open(page, '/settings/invoicing');
      await expect(page.getByText('Vence pronto: tramita uno nuevo en el SAT y súbelo antes de que venza.')).toBeVisible();
      await snap(page, 'facturacion-pruebas-por-vencer');
    });
  });

  test.describe('autofactura', () => {
    test.use({ prepareData: FACTURACION_AUTOFACTURA });

    for (const look of LOOKS) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`facturación con autofactura ${look.suffix}`, async ({ page }) => {
          await open(page, '/settings/invoicing');
          await expect(page.getByRole('switch', { name: 'Autofactura activa' })).toBeChecked();
          // The page's address carries the one the scenes are served from (its port changes with VISUAL_PORT).
          await snap(page, `facturacion-autofactura-${look.suffix}`, {
            mask: [page.getByRole('link', { name: /\/m\/cafe-la-esquina\/facturar$/ })],
          });
        });
      });
    }
  });

  test.describe('con logo en el PDF', () => {
    test.use({ prepareData: FACTURACION_CON_LOGO });

    for (const look of LOOKS.slice(0, 2)) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`facturación con el logo en el pdf ${look.suffix}`, async ({ page }) => {
          await open(page, '/settings/invoicing');
          await expect(page.getByText('El de tu negocio')).toBeVisible();
          await snap(page, `facturacion-con-logo-${look.suffix}`);
        });
      });
    }
  });

  test.describe('no disponible', () => {
    test.use({ prepareData: FACTURACION_NO_DISPONIBLE });

    test('facturación no disponible', async ({ page }) => {
      await open(page, '/settings/invoicing');
      await expect(page.getByRole('heading', { name: 'La facturación no está disponible por ahora' })).toBeVisible();
      await snap(page, 'facturacion-no-disponible');
    });
  });
});

test.describe('Pedidos › Factura', () => {
  /** The order's detail, and its invoice from there. */
  async function openInvoice(page: Page) {
    await open(page, '/orders');
    await page.getByRole('button', { name: ORDER_NUMBER, exact: true }).click();
    const detail = await dialog(page, `Pedido ${ORDER_NUMBER}`);
    await detail.getByRole('button', { name: 'Factura' }).click();
    return dialog(page, `Facturar ${ORDER_NUMBER}`);
  }

  test.describe('por facturar', () => {
    test.use({ prepareData: PEDIDO_POR_FACTURAR });

    test('factura lista para timbrar', async ({ page }) => {
      const invoice = await openInvoice(page);
      await expect(invoice.getByRole('button', { name: 'Timbrar factura' })).toBeEnabled();
      await snapScreen(page, 'pedidos-factura-por-timbrar');
    });

    test('factura lista para timbrar en el teléfono', async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      const invoice = await openInvoice(page);
      await expect(invoice.getByRole('button', { name: 'Timbrar factura' })).toBeEnabled();
      await snapScreen(page, 'pedidos-factura-por-timbrar-telefono');
    });
  });

  test.describe('facturado', () => {
    test.use({ prepareData: PEDIDO_FACTURADO });

    test('factura timbrada', async ({ page }) => {
      const invoice = await openInvoice(page);
      await expect(invoice.getByText('5FB2822E-396D-4725-8521-CDC4BDD20CCF')).toBeVisible();
      await expect(invoice.getByRole('button', { name: 'Timbrar factura' })).toBeHidden();
      await snapScreen(page, 'pedidos-factura-timbrada');
    });

    test('factura timbrada oscuro', async ({ page }) => {
      await page.emulateMedia({ colorScheme: 'dark' });
      test.info().annotations.push({ type: 'look', description: 'oscuro' });
      const invoice = await openInvoice(page);
      await expect(invoice.getByText('5FB2822E-396D-4725-8521-CDC4BDD20CCF')).toBeVisible();
      await snapScreen(page, 'pedidos-factura-timbrada-oscuro');
    });

    test('cancelar la factura', async ({ page }) => {
      const invoice = await openInvoice(page);
      await invoice.getByRole('button', { name: 'Cancelar factura F-3' }).click();
      const cancelling = await dialog(page, 'Cancelar la factura F-3');
      await cancelling.getByLabel('03 · La venta no se llevó a cabo').check();
      await snapScreen(page, 'pedidos-factura-cancelar');
    });
  });

  test.describe('cancelándose', () => {
    test.use({ prepareData: PEDIDO_CANCELANDOSE });

    test('factura cancelándose', async ({ page }) => {
      const invoice = await openInvoice(page);
      await expect(invoice.getByText(/El SAT pidió al cliente que acepte la cancelación/)).toBeVisible();
      await snapScreen(page, 'pedidos-factura-cancelandose');
    });
  });

  test.describe('se quedó timbrando', () => {
    test.use({ prepareData: PEDIDO_TIMBRANDO_SIN_TIMBRE });

    test('factura que se quedó timbrando', async ({ page }) => {
      const invoice = await openInvoice(page);
      await expect(invoice.getByText(/Se pidió hace más de 71 horas/)).toBeVisible();
      await expect(invoice.getByRole('link', { name: 'CFDI emitidos' })).toBeVisible();
      await expect(invoice.getByRole('button', { name: /^Volver a pedir el timbre/ })).toBeHidden();
      await snapScreen(page, 'pedidos-factura-timbrando-sin-timbre');
    });
  });
});

test.describe('Pedidos › Factura por correo', () => {
  /** The order's invoice, with how its latest email went. */
  async function openInvoice(page: Page) {
    await open(page, '/orders');
    await page.getByRole('button', { name: ORDER_NUMBER, exact: true }).click();
    const detail = await dialog(page, `Pedido ${ORDER_NUMBER}`);
    await detail.getByRole('button', { name: 'Factura' }).click();
    return dialog(page, `Facturar ${ORDER_NUMBER}`);
  }

  test.describe('enviada', () => {
    test.use({ prepareData: PEDIDO_FACTURA_POR_CORREO });

    for (const look of LOOKS) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`factura enviada por correo ${look.suffix}`, async ({ page }) => {
          const invoice = await openInvoice(page);
          await expect(invoice.getByText(/Se envió por correo a facturas@altavista\.mx/)).toBeVisible();
          await snapScreen(page, `pedidos-factura-enviada-por-correo-${look.suffix}`);
        });
      });
    }

    test('enviar la factura por correo', async ({ page }) => {
      const invoice = await openInvoice(page);
      await invoice.getByRole('button', { name: 'Enviar por correo la factura F-3' }).click();
      const sending = await dialog(page, 'Enviar por correo la factura F-3');
      await sending.getByLabel('Correo').fill('contabilidad@altavista.mx');
      await expect(sending.getByRole('checkbox', { name: /Usar este correo para lo que siga de esta factura/ })).not.toBeChecked();
      await snapScreen(page, 'pedidos-factura-enviar-por-correo');
    });
  });

  test.describe('pedida por el cliente', () => {
    test.use({ prepareData: PEDIDO_AUTOFACTURADO });

    test('factura que pidió el cliente en línea', async ({ page }) => {
      const invoice = await openInvoice(page);
      await expect(invoice.getByText('La pidió el cliente en línea con su ticket.')).toBeVisible();
      await expect(invoice.getByText(/Lo pidió el cliente en línea\./)).toBeVisible();
      await snapScreen(page, 'pedidos-factura-pedida-por-el-cliente');
    });
  });

  test.describe('rechazada', () => {
    test.use({ prepareData: PEDIDO_CORREO_RECHAZADO });

    test('correo de la factura rechazado', async ({ page }) => {
      const invoice = await openInvoice(page);
      await expect(invoice.getByText(/No se envió a facturas@altavsita\.mx: el servidor de correo lo rechazó/)).toBeVisible();
      await snapScreen(page, 'pedidos-factura-correo-rechazado');
    });
  });
});

test.describe('Pedidos › Sustituir factura', () => {
  /** The order's invoice, from its detail. */
  async function openInvoice(page: Page) {
    await open(page, '/orders');
    await page.getByRole('button', { name: ORDER_NUMBER, exact: true }).click();
    const detail = await dialog(page, `Pedido ${ORDER_NUMBER}`);
    await detail.getByRole('button', { name: 'Factura' }).click();
    return dialog(page, `Facturar ${ORDER_NUMBER}`);
  }

  test.describe('sustituible', () => {
    test.use({ prepareData: PEDIDO_SUSTITUIBLE });

    for (const look of LOOKS.slice(0, 2)) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`sustituir la factura ${look.suffix}`, async ({ page }) => {
          const invoice = await openInvoice(page);
          await invoice.getByRole('button', { name: 'Sustituir factura F-3' }).click();
          await expect(invoice.getByRole('group', { name: 'Sustituir la factura F-3' })).toBeFocused();
          await expect(invoice.getByRole('button', { name: 'Timbrar y sustituir la F-3' })).toBeEnabled();
          await snapScreen(page, `pedidos-factura-sustituir-${look.suffix}`);
        });
      });
    }
  });

  test.describe('sustituida', () => {
    test.use({ prepareData: PEDIDO_SUSTITUIDO });

    for (const look of LOOKS) {
      test.describe(look.suffix, () => {
        test.use({ colorMode: look.colorMode, viewport: look.viewport });

        test(`factura sustituida ${look.suffix}`, async ({ page }) => {
          const invoice = await openInvoice(page);
          await expect(invoice.getByText('Sustituida por la factura F-4.')).toBeVisible();
          await expect(invoice.getByRole('button', { name: 'Cancelar con relación la factura F-3' })).toBeVisible();
          await snapScreen(page, `pedidos-factura-sustituida-${look.suffix}`);
        });
      });
    }

    test('cancelar la factura sustituida', async ({ page }) => {
      const invoice = await openInvoice(page);
      await invoice.getByRole('button', { name: 'Cancelar factura F-3' }).click();
      const cancelling = await dialog(page, 'Cancelar la factura F-3');
      await expect(cancelling.getByLabel('02 · Tiene errores, sin relacionarla con la F-4')).toBeChecked();
      await snapScreen(page, 'pedidos-factura-cancelar-sustituida');
    });
  });

  test.describe('sustituida y cancelada', () => {
    test.use({ prepareData: PEDIDO_SUSTITUIDO_CANCELADO });

    test('factura sustituida y cancelada', async ({ page }) => {
      const invoice = await openInvoice(page);
      await expect(invoice.getByText('Sustituye a la factura F-3.')).toBeVisible();
      await expect(invoice.getByRole('button', { name: 'Sustituir factura F-4' })).toBeVisible();
      await snapScreen(page, 'pedidos-factura-sustituida-cancelada');
    });
  });
});

test.describe('Pedidos › Factura en parcialidades', () => {
  test.use({ prepareData: PEDIDO_EN_PARCIALIDADES });

  /** The order's invoice, scrolled to the complements of its payments. */
  async function openComplements(page: Page) {
    await open(page, '/orders');
    await page.getByRole('button', { name: ORDER_NUMBER, exact: true }).click();
    const detail = await dialog(page, `Pedido ${ORDER_NUMBER}`);
    await detail.getByRole('button', { name: 'Factura' }).click();
    const invoice = await dialog(page, `Facturar ${ORDER_NUMBER}`);
    const heading = invoice.getByRole('heading', { name: 'Complementos de pago' });
    await heading.scrollIntoViewIfNeeded();
    await expect(invoice.getByText(/Sin complemento todavía: TienditApp lo emite en unos minutos/)).toBeVisible();
    return invoice;
  }

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`complementos de pago ${look.suffix}`, async ({ page }) => {
        const invoice = await openComplements(page);
        await expect(invoice.getByText('P-1', { exact: true })).toBeVisible();
        await snapScreen(page, `pedidos-factura-complementos-${look.suffix}`);
      });
    });
  }

  test('cancelar un complemento', async ({ page }) => {
    const invoice = await openComplements(page);
    await invoice.getByRole('button', { name: 'Cancelar complemento P-1' }).click();
    const cancelling = await dialog(page, 'Cancelar el complemento P-1');
    await cancelling.getByLabel('03 · El cobro no se llevó a cabo').check();
    await snapScreen(page, 'pedidos-factura-cancelar-complemento');
  });
});

test.describe('Pedidos › Notas de crédito', () => {
  test.use({ prepareData: PEDIDO_CON_NOTAS_DE_CREDITO });

  /** The order's invoice, scrolled to the credit notes of its refunds. */
  async function openCreditNotes(page: Page) {
    await open(page, '/orders');
    await page.getByRole('button', { name: ORDER_NUMBER, exact: true }).click();
    const detail = await dialog(page, `Pedido ${ORDER_NUMBER}`);
    await detail.getByRole('button', { name: 'Factura' }).click();
    const invoice = await dialog(page, `Facturar ${ORDER_NUMBER}`);
    const heading = invoice.getByRole('heading', { name: 'Notas de crédito' });
    await heading.scrollIntoViewIfNeeded();
    await expect(invoice.getByText('Sin nota de crédito todavía: TienditApp la emite en unos minutos.')).toBeVisible();
    return invoice;
  }

  for (const look of LOOKS) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`notas de crédito ${look.suffix}`, async ({ page }) => {
        const invoice = await openCreditNotes(page);
        await expect(invoice.getByText('NC-1', { exact: true })).toBeVisible();
        await snapScreen(page, `pedidos-factura-notas-de-credito-${look.suffix}`);
      });
    });
  }

  test('cancelar una nota de crédito', async ({ page }) => {
    const invoice = await openCreditNotes(page);
    await invoice.getByRole('button', { name: 'Cancelar nota de crédito NC-1' }).click();
    const cancelling = await dialog(page, 'Cancelar la nota de crédito NC-1');
    await expect(cancelling.getByLabel('02 · Tiene errores (después emites otra para el reembolso)')).toBeChecked();
    await snapScreen(page, 'pedidos-factura-cancelar-nota-de-credito');
  });
});

test.describe('Pedidos › Notas de crédito de una venta de la factura global', () => {
  test.use({ prepareData: PEDIDO_EN_FACTURA_GLOBAL });

  for (const look of LOOKS.slice(0, 2)) {
    test.describe(look.suffix, () => {
      test.use({ colorMode: look.colorMode, viewport: look.viewport });

      test(`notas de crédito de la factura global ${look.suffix}`, async ({ page }) => {
        await open(page, '/orders');
        await page.getByRole('button', { name: ORDER_NUMBER, exact: true }).click();
        const detail = await dialog(page, `Pedido ${ORDER_NUMBER}`);
        await detail.getByRole('button', { name: 'Factura' }).click();
        const invoice = await dialog(page, `Facturar ${ORDER_NUMBER}`);
        const heading = invoice.getByRole('heading', { name: 'Notas de crédito' });
        await heading.scrollIntoViewIfNeeded();
        await expect(invoice.getByText(/al público en general, de la factura global F-58/)).toHaveCount(2);
        await expect(invoice.getByText(/^Descuento o bonificación · \$/)).toBeVisible();
        await expect(invoice.getByText('Sin nota de crédito todavía: TienditApp la emite en unos minutos.')).toBeVisible();
        await snapScreen(page, `pedidos-factura-notas-de-la-global-${look.suffix}`);
      });
    });
  }
});

test.describe('Clientes › Datos fiscales', () => {
  test.use({ prepareData: CLIENTE_CON_DATOS_FISCALES });

  /** The customer's edit dialog, where their fiscal data is. */
  async function editCustomer(page: Page) {
    await open(page, '/customers');
    await expect(page.getByText('RFC GAL1806043P5')).toBeVisible();
    await page.getByRole('button', { name: `Editar ${CUSTOMER_NAME}` }).click();
    return dialog(page, 'Editar cliente');
  }

  test('editar un cliente con sus datos fiscales', async ({ page }) => {
    const editing = await editCustomer(page);
    await expect(editing.getByText('RFC GAL1806043P5 · GRUPO ALTAVISTA')).toBeVisible();
    await snapScreen(page, 'clientes-editar-con-datos-fiscales');
  });

  test('datos fiscales del cliente', async ({ page }) => {
    const editing = await editCustomer(page);
    await editing.getByRole('button', { name: 'Cambiar datos fiscales' }).click();
    const fiscal = await dialog(page, `Datos fiscales de ${CUSTOMER_NAME}`);
    await expect(fiscal.getByLabel('RFC')).toHaveValue('GAL1806043P5');
    await expect(fiscal.getByLabel('Correo para facturas (opcional)')).toHaveValue('facturas@altavista.mx');
    await snapScreen(page, 'clientes-datos-fiscales');
  });
});
