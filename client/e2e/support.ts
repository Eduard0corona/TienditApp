import { readFile } from 'node:fs/promises';
import { expect, type Page } from '@playwright/test';
import { apiLog } from './paths';

/** Not guessable: the API refuses a password with the person's name or a common one. */
export const PASSWORD = 'Mostrador.Sur.4821';

export interface Owner {
  firstName: string;
  email: string;
  businessName: string;
}

/**
 * A new owner and business on each run: the database outlives the runs, and an email or a slug signs up once. The
 * address is of example.com, reserved for examples (RFC 2606): no email of the tests can reach anyone.
 */
export function newOwner(): Owner {
  const stamp = `${Date.now().toString(36)}${Math.floor(Math.random() * 1296).toString(36).padStart(2, '0')}`;
  return { firstName: 'Lupita', email: `duena.${stamp}@example.com`, businessName: `Taquería Lupita ${stamp}` };
}

/**
 * The activation link the API would have emailed. Without an email provider, Development writes each email it could not
 * send to its log, after «Email not sent to <email>»: the link is the first after that line, so another owner's never
 * counts. The link ends at the quote of its href: a read in the middle of a write never takes half a token. Waits for it,
 * since the email goes out through the outbox.
 */
export async function activationLink(email: string): Promise<string> {
  let link: string | null = null;
  await expect.poll(async () => {
    const text = await readFile(apiLog, 'utf8').catch(() => '');
    const at = text.indexOf(`Email not sent to ${email}`);
    const found = at < 0 ? null : /confirm-email\?userId=([0-9a-fA-F-]+)&(?:amp;)?token=([A-Za-z0-9%._~-]+)['"]/.exec(text.slice(at));
    link = found ? `/confirm-email?userId=${found[1]}&token=${found[2]}` : null;
    return link;
  }, { message: `the activation email to ${email} in ${apiLog}`, timeout: 30_000 }).not.toBeNull();
  return link!;
}

/** Signs up a business from /register, activates it with the emailed link and signs in: it lands on Primeros pasos. */
export async function signUpBusiness(page: Page, owner: Owner): Promise<void> {
  await page.goto('/register');
  await page.getByRole('button', { name: /Tengo un negocio/ }).click();
  // The required fields are named with their asterisk: «Nombre *», never «Nombre del negocio *».
  await page.getByLabel(/^Nombre \*$/).fill(owner.firstName);
  await page.getByLabel(/^Apellido \*$/).fill('Ramírez');
  await page.getByLabel(/^Correo \*$/).fill(owner.email);
  await page.getByLabel(/^Teléfono \*$/).fill('5512345678');
  await page.getByRole('textbox', { name: /^Contraseña/ }).fill(PASSWORD);
  await page.getByLabel('Nombre del negocio').fill(owner.businessName);
  await page.getByLabel('Tipo de negocio').selectOption({ label: 'Alimentos' });
  await page.getByRole('button', { name: 'Registrar negocio' }).click();
  await expect(page).toHaveURL(/\/check-email/);

  await page.goto(await activationLink(owner.email));
  await expect(page).toHaveURL(/\/login/);
  await page.getByLabel('Correo').fill(owner.email);
  await page.getByRole('textbox', { name: /^Contraseña/ }).fill(PASSWORD);
  await page.getByRole('button', { name: /Iniciar sesión|Entrar/ }).click();
  await expect(page).toHaveURL(/\/setup/);
  await expect(page.getByRole('heading', { name: `Prepara ${owner.businessName}` })).toBeVisible();
}

/** Primeros pasos with what it proposes and the Taquería template, up to Vender. */
export async function setUpWithTaqueria(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { name: '¿Qué vas a usar?' })).toBeVisible();
  await page.getByRole('button', { name: 'Continuar' }).click();
  await expect(page.getByRole('heading', { name: 'Precios e IVA' })).toBeVisible();
  await page.getByRole('button', { name: 'Continuar' }).click();
  await expect(page.getByRole('heading', { name: 'Empieza con un catálogo de ejemplo' })).toBeVisible();
  // The innermost block with the template's name and its button: its card, whatever classes it has.
  await page.locator('div')
    .filter({ has: page.getByRole('heading', { name: 'Taquería', exact: true }) })
    .filter({ has: page.getByRole('button', { name: 'Usar esta plantilla' }) })
    .last()
    .getByRole('button', { name: 'Usar esta plantilla' })
    .click();
  await expect(page.getByRole('heading', { name: '¡Tu negocio está listo para vender!' })).toBeVisible();
  await page.getByRole('button', { name: 'Hacer mi primera venta' }).click();
  await expect(page).toHaveURL(/\/sell/);
}

/** Opens the cashier's drawer from Vender with that float: every charge, and every online order, needs one open. */
export async function openDrawerFromSell(page: Page, openingAmount: string): Promise<void> {
  await expect(page.getByText('Tu caja está cerrada: ábrela para cobrar.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Abrir caja' }).click();
  const drawer = page.getByRole('dialog', { name: 'Abrir mi caja' });
  await drawer.getByLabel('Fondo inicial').fill(openingAmount);
  await drawer.getByRole('button', { name: 'Abrir caja' }).click();
  await expect(page.getByText('Tu caja quedó abierta.')).toBeVisible();
  await expect(drawer).toBeHidden();
}
