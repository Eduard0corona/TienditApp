import { expect, test } from '@playwright/test';
import { newOwner, openDrawerFromSell, setUpWithTaqueria, signUpBusiness } from '../support';

test('un cliente pide en el menú en línea y el negocio confirma su pedido', async ({ page, browser, baseURL }) => {
  const owner = newOwner();
  await signUpBusiness(page, owner);
  await setUpWithTaqueria(page);
  // Online orders come in while a drawer is open at the main branch.
  await openDrawerFromSell(page, '300');

  await page.goto('/settings/business');
  await page.getByRole('radiogroup', { name: 'Menú en línea' }).getByRole('radio', { name: 'Menú con pedidos' }).click();
  await expect(page.getByText('Menú en línea actualizado.')).toBeVisible();
  const menuPath = new URL(await page.getByLabel('Enlace', { exact: true }).inputValue()).pathname;
  expect(menuPath).toMatch(/^\/m\/[a-z0-9-]+$/);

  // The customer, on their own device and without an account.
  const customerDevice = await browser.newContext({ baseURL, locale: 'es-MX', timezoneId: 'America/Mexico_City' });
  const customer = await customerDevice.newPage();
  await customer.goto(menuPath);
  await expect(customer.getByRole('heading', { name: owner.businessName })).toBeVisible();
  await customer.getByRole('button', { name: /^Agregar Taco de pastor/ }).click();
  await customer.getByRole('button', { name: /^Ver pedido · 1 · \$22\.00/ }).click();
  const checkout = customer.getByRole('dialog', { name: 'Tu pedido' });
  await checkout.getByLabel('Tu nombre').fill('Andrés Pérez');
  await checkout.getByLabel('Teléfono (WhatsApp)').fill('5587654321');
  await checkout.getByRole('button', { name: 'Hacer pedido · $22.00' }).click();
  await expect(customer.getByRole('heading', { name: '¡Pedido enviado!' })).toBeVisible();
  await expect(customer.getByText(`${owner.businessName} lo confirmará en unos minutos.`)).toBeVisible();
  const orderNumber = await customer.getByRole('definition').filter({ hasText: /^ORD-/ }).innerText();
  await customer.getByRole('link', { name: 'Seguir tu pedido' }).click();
  await expect(customer).toHaveURL(/\/seguimiento#.+/);
  await expect(customer.getByText(`Folio ${orderNumber}`, { exact: false })).toBeVisible();
  const currentStep = customer.getByRole('list', { name: 'Seguimiento del pedido' }).locator('[aria-current="step"]');
  await expect(currentStep).toContainText('Recibido, ahora');

  // The business finds it in Pedidos, waiting, and confirms it.
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ hasText: orderNumber });
  await expect(row.getByRole('cell', { name: `${orderNumber} En línea` })).toBeVisible();
  await expect(row.getByRole('cell', { name: 'Andrés Pérez +525587654321' })).toBeVisible();
  await expect(row.getByRole('cell', { name: 'Pendiente' })).toBeVisible();
  await expect(row.getByRole('cell', { name: '$22.00 Por cobrar $22.00' })).toBeVisible();
  await row.getByRole('button', { name: orderNumber }).click();
  const detail = page.getByRole('dialog', { name: `Pedido ${orderNumber}` });
  await detail.getByRole('button', { name: 'Confirmada', exact: true }).click();
  await expect(row.getByRole('cell', { name: 'Confirmada' })).toBeVisible();

  // The customer's link shows it accepted: the page asks again every 20 seconds, a reload asks now.
  await customer.reload();
  await expect(currentStep).toContainText('Confirmado, ahora');
  await expect(customer.getByText('Recibido, hecho')).toBeVisible();
  await customerDevice.close();
});
