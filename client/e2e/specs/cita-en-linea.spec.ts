import { expect, test } from '@playwright/test';
import { newOwner, signUpBusiness } from '../support';

test('un cliente agenda una cita en línea, el negocio la acepta y su enlace lo dice', async ({ page, browser, baseURL }) => {
  const signedUp = newOwner();
  const owner = { ...signedUp, businessName: signedUp.businessName.replace('Taquería', 'Barbería') };
  await signUpBusiness(page, owner);

  // Primeros pasos with the Agenda and Servicios modules, and the Barbería template: four services that take an hour.
  await expect(page.getByRole('heading', { name: '¿Qué vas a usar?' })).toBeVisible();
  await page.getByRole('checkbox', { name: /^Agenda/ }).check();
  await page.getByRole('checkbox', { name: /^Servicios/ }).check();
  await page.getByRole('button', { name: 'Continuar' }).click();
  await expect(page.getByRole('heading', { name: 'Precios e IVA' })).toBeVisible();
  await page.getByRole('button', { name: 'Continuar' }).click();
  await expect(page.getByRole('heading', { name: 'Empieza con un catálogo de ejemplo' })).toBeVisible();
  await page.getByText('Plantillas de otros giros').click();
  await page.locator('div')
    .filter({ has: page.getByRole('heading', { name: 'Barbería', exact: true }) })
    .filter({ has: page.getByRole('button', { name: 'Usar esta plantilla' }) })
    .last()
    .getByRole('button', { name: 'Usar esta plantilla' })
    .click();
  await expect(page.getByRole('heading', { name: '¡Tu negocio está listo para vender!' })).toBeVisible();
  await page.getByRole('button', { name: 'Ir a Inicio' }).click();
  await expect(page).toHaveURL(/\/dashboard/);

  // What online appointments need, in this order (the card reads the menu and the hours): the menu published, the hours
  // the free times come from (every day, 9 a. m. to 8 p. m.), and the switch.
  await page.goto('/settings/business');
  await page.getByRole('radiogroup', { name: 'Menú en línea' }).getByRole('radio', { name: 'Solo menú' }).click();
  await expect(page.getByText('Menú en línea actualizado.')).toBeVisible();
  const menuPath = new URL(await page.getByLabel('Enlace', { exact: true }).inputValue()).pathname;
  for (const day of ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo']) {
    await page.getByRole('switch', { name: day, exact: true }).check();
  }
  await expect(page.getByLabel('Abre el lunes')).toHaveValue('09:00');
  await expect(page.getByLabel('Cierra el domingo')).toHaveValue('20:00');
  await page.getByRole('button', { name: 'Guardar horario' }).click();
  await expect(page.getByText('Horario guardado: tu menú en línea ya lo muestra.')).toBeVisible();
  const booking = page.getByRole('region', { name: 'Citas en línea' });
  await booking.getByRole('switch', { name: 'Recibir citas en línea' }).check();
  await booking.getByRole('button', { name: 'Guardar' }).click();
  await expect(page.getByText('Guardado: tus clientes ya pueden agendar en línea.')).toBeVisible();
  await expect(booking.getByText('Tus clientes ya agendan desde tu menú en línea.')).toBeVisible();

  // The customer, on their own device and without an account, books from the menu: tomorrow at 10 a. m. (today may have
  // no free time left, a start needs an hour's notice).
  const customerDevice = await browser.newContext({ baseURL, locale: 'es-MX', timezoneId: 'America/Mexico_City' });
  const customer = await customerDevice.newPage();
  await customer.goto(menuPath);
  await customer.getByRole('link', { name: 'Agenda tu cita' }).click();
  await expect(customer).toHaveURL(/\/m\/[a-z0-9-]+\/agendar/);
  await customer.getByRole('button', { name: /^Corte de cabello/ }).click();
  const tomorrow = customer.getByRole('group', { name: 'Días de la semana' }).getByRole('button').nth(1);
  await expect(tomorrow).toBeEnabled();
  await tomorrow.click();
  await customer.getByRole('group', { name: /^Horarios libres del / }).getByRole('button', { name: /^10:00\sa\.\sm\.$/ }).click();
  await customer.getByRole('button', { name: /^Continuar · .+ · 10:00\sa\.\sm\.$/ }).click();
  const request = customer.getByRole('dialog', { name: 'Tu cita' });
  await request.getByLabel('Tu nombre').fill('Andrés Pérez');
  await request.getByLabel('Teléfono (WhatsApp)').fill('5587654321');
  await request.getByRole('button', { name: 'Pedir cita' }).click();
  await expect(customer.getByRole('heading', { name: '¡Solicitud enviada!' })).toBeVisible();
  await expect(customer.getByText(`${owner.businessName} te confirma por WhatsApp. Mientras, tu cita queda por confirmar.`)).toBeVisible();

  // Its link: waiting for the business.
  await customer.getByRole('link', { name: 'Ver tu cita' }).click();
  await expect(customer).toHaveURL(/\/cita#.+/);
  await expect(customer.getByRole('heading', { name: 'Corte de cabello' })).toBeVisible();
  await expect(customer.getByText('Por confirmar', { exact: true })).toBeVisible();
  await expect(customer.getByText(`${owner.businessName} te confirma por WhatsApp.`)).toBeVisible();

  // The business finds the request in the Agenda and accepts it.
  await page.goto('/appointments');
  await expect(page.getByText('Tienes 1 solicitud de cita por confirmar.', { exact: false })).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: 'Andrés Pérez' });
  await expect(row).toContainText('En línea');
  await expect(row).toContainText('Corte de cabello');
  await expect(row).toContainText('Por confirmar');
  await row.getByRole('button', { name: 'Aceptar la cita de Andrés Pérez' }).click();
  await expect(page.getByText('Cita aceptada: el cliente recibe el aviso.')).toBeVisible();
  await expect(row).toContainText('Programada');

  // The customer's link says it is confirmed: the page asks when it opens, a reload asks now.
  await customer.reload();
  await expect(customer.getByText('Confirmada', { exact: true })).toBeVisible();
  await expect(customer.getByText(`${owner.businessName} te confirma por WhatsApp.`)).toBeHidden();
  await customerDevice.close();
});
