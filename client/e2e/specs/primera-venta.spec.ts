import { expect, test, type Page } from '@playwright/test';
import { newOwner, openDrawerFromSell, setUpWithTaqueria, signUpBusiness } from '../support';

test('una dueña registra su taquería y cobra su primera venta en efectivo', async ({ page }) => {
  const owner = newOwner();
  await signUpBusiness(page, owner);
  await setUpWithTaqueria(page);

  // Every charge goes into a drawer: Vender asks to open it first.
  await openDrawerFromSell(page, '500');

  // Two tacos al pastor of the template, at $22.00 each.
  const pastor = page.getByRole('button', { name: /^Agregar Taco de pastor/ });
  await pastor.click();
  await pastor.click();
  const ticket = page.getByRole('complementary', { name: 'Ticket de la venta' });
  await expect(ticket.getByText('Taco de pastor')).toBeVisible();
  await ticket.getByRole('button', { name: /^Cobrar \$44\.00/ }).click();

  const charge = page.getByRole('dialog', { name: 'Cobrar' });
  await expect(charge.getByRole('radio', { name: /Efectivo/ })).toHaveAttribute('aria-checked', 'true');
  await charge.getByLabel('Efectivo recibido').fill('100');
  await expect(charge.getByText('$56.00')).toBeVisible();
  await charge.getByRole('button', { name: 'Cobrar $44.00' }).click();

  const done = page.getByRole('dialog', { name: /^Venta .+ cobrada$/ });
  await expect(done).toBeVisible();
  const orderNumber = (await done.getByRole('heading').first().innerText()).replace(/^Venta (.+) cobrada$/, '$1');
  await done.getByRole('button', { name: 'Nueva venta' }).click();
  await expect(done).toBeHidden();

  // Pedidos has it paid, on its way to the kitchen of a food business, and the drawer counts the cash it took.
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ hasText: orderNumber });
  await expect(row.getByRole('cell', { name: 'Confirmada' })).toBeVisible();
  await expect(row.getByRole('cell', { name: '$44.00 Pagado' })).toBeVisible();

  await page.goto('/cash');
  await expect(kpi(page, 'Fondo inicial')).toHaveText(/\$500\.00$/);
  await expect(kpi(page, 'Entró en efectivo')).toHaveText(/\$44\.00$/);
  await expect(kpi(page, 'Debe haber en caja')).toHaveText(/\$544\.00$/);
});

/** A figure of Mi caja: the innermost block with its label and an amount. */
function kpi(page: Page, label: string) {
  return page.locator('div')
    .filter({ has: page.getByText(label, { exact: true }) })
    .filter({ hasText: /\$/ })
    .last();
}
