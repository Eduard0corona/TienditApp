import { expect, test } from '@playwright/test';
import { usesRunningApi } from '../paths';
import { routeMercadoPagoPages, startFakeMercadoPago } from '../fake-mercadopago';
import { newOwner, openDrawerFromSell, setUpWithTaqueria, signUpBusiness } from '../support';

// Mercado Pago is the stand-in of fake-mercadopago.ts, which only an API started by start-api.mjs calls.
test.skip(usesRunningApi, 'An API already running does not call the Mercado Pago stand-in (README.md).');

test('el negocio conecta Mercado Pago y un cliente paga su pedido en línea', async ({ page, browser, baseURL }) => {
  const mercadoPago = await startFakeMercadoPago();
  try {
    await routeMercadoPagoPages(page.context(), mercadoPago);
    const owner = newOwner();
    await signUpBusiness(page, owner);
    await setUpWithTaqueria(page);
    // Online orders come in while a drawer is open at the main branch, paid online ones too.
    await openDrawerFromSell(page, '300');
    await page.goto('/settings/business');
    await page.getByRole('radiogroup', { name: 'Menú en línea' }).getByRole('radio', { name: 'Menú con pedidos' }).click();
    await expect(page.getByText('Menú en línea actualizado.')).toBeVisible();
    const menuPath = new URL(await page.getByLabel('Enlace', { exact: true }).inputValue()).pathname;

    // The owner connects the business's account: Mercado Pago's authorization sends them back with a code, the page
    // finishes it with their session, and the API exchanges it with its PKCE verifier.
    await page.goto('/settings/online-payments');
    await page.getByRole('button', { name: 'Conectar Mercado Pago' }).click();
    await expect(page.getByText('Cuenta de Mercado Pago conectada.')).toBeVisible();
    await expect(page).toHaveURL(/\/settings\/online-payments$/);
    await expect(page.getByText('Conectada', { exact: true })).toBeVisible();
    // The switch shows what the server saved: it turns on when the answer comes, not on the click.
    const offer = page.getByRole('switch', { name: 'Ofrecer pagar en línea en mi menú' });
    await offer.click();
    await expect(page.getByText('Tu menú ya ofrece pagar en línea.')).toBeVisible();
    await expect(offer).toBeChecked();

    // The customer, on their own device and without an account, pays when ordering.
    const customerDevice = await browser.newContext({ baseURL, locale: 'es-MX', timezoneId: 'America/Mexico_City' });
    await routeMercadoPagoPages(customerDevice, mercadoPago);
    const customer = await customerDevice.newPage();
    await customer.goto(menuPath);
    await customer.getByRole('button', { name: /^Agregar Taco de pastor/ }).click();
    await customer.getByRole('button', { name: /^Ver pedido · 1 · \$22\.00/ }).click();
    const checkout = customer.getByRole('dialog', { name: 'Tu pedido' });
    await checkout.getByLabel('Tu nombre').fill('Andrés Pérez');
    await checkout.getByLabel('Teléfono (WhatsApp)').fill('5587654321');
    await checkout.getByRole('radio', { name: /^Pagar ahora en línea/ }).check();
    await checkout.getByRole('button', { name: 'Pagar · $22.00' }).click();

    // Back from Mercado Pago with the payment approved: the page asks the API, which reads the payment from Mercado Pago
    // with the business's key, and the order is placed.
    await expect(customer).toHaveURL(/\/m\/[a-z0-9-]+\/pago\/[0-9a-f-]+\?/);
    await expect(customer.getByRole('heading', { name: '¡Pago recibido!' })).toBeVisible();
    const returnUrl = customer.url();
    await expect(customer.getByText(`Tu pedido ya está con ${owner.businessName}; lo confirmará en unos minutos.`)).toBeVisible();
    const orderNumber = await customer.getByRole('definition').filter({ hasText: /^ORD-/ }).innerText();
    await expect(customer.getByRole('definition').filter({ hasText: '$22.00' })).toBeVisible();

    // What TienditApp asked Mercado Pago: one preference for the order's total, read back with its payment.
    expect(mercadoPago.preferences).toHaveLength(1);
    expect(mercadoPago.preferences[0].amount).toBe(22);
    expect(mercadoPago.payments).toHaveLength(1);
    expect(mercadoPago.calls).toContain(`GET /v1/payments/${mercadoPago.payments[0].id}`);

    // The tracking link the page gives.
    await customer.getByRole('link', { name: 'Seguir tu pedido' }).click();
    await expect(customer).toHaveURL(/\/seguimiento#.+/);
    await expect(customer.getByText(`Folio ${orderNumber}`, { exact: false })).toBeVisible();

    // The business has it in Pedidos, paid: nothing to charge.
    await page.goto('/orders');
    const row = page.getByRole('row').filter({ hasText: orderNumber });
    await expect(row.getByRole('cell', { name: `${orderNumber} En línea` })).toBeVisible();
    await expect(row.getByRole('cell', { name: 'Andrés Pérez +525587654321' })).toBeVisible();
    await expect(row.getByRole('cell', { name: '$22.00 Pagado' })).toBeVisible();

    // The same return again (a reload, or the notification and the return both reading the payment): the same order,
    // never a second one.
    await customer.goto(returnUrl);
    await expect(customer.getByRole('heading', { name: '¡Pago recibido!' })).toBeVisible();
    await expect(customer.getByRole('definition').filter({ hasText: /^ORD-/ })).toHaveText(orderNumber);
    await page.reload();
    await expect(page.getByRole('row').filter({ hasText: 'Andrés Pérez' })).toHaveCount(1);
    await customerDevice.close();
  } finally {
    await mercadoPago.close();
  }
});
