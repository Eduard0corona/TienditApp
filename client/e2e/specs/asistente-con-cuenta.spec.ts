import { createHash, randomBytes } from 'node:crypto';
import { expect, test, type APIRequestContext } from '@playwright/test';
import { PASSWORD, activationLink, newOwner, openDrawerFromSell, setUpWithTaqueria, signUpBusiness } from '../support';

const CALLBACK = 'http://127.0.0.1:43210/callback';

/** One tool call to /mcp/cuenta with the assistant's access token (Fase 32E): its HTTP status and the text it answers. */
async function accountTool(
  request: APIRequestContext, token: string, name: string, args: Record<string, unknown>,
): Promise<{ status: number; text: string }> {
  const response = await request.post('/mcp/cuenta', {
    headers: { Accept: 'application/json, text/event-stream', 'MCP-Protocol-Version': '2025-06-18', Authorization: `Bearer ${token}` },
    data: { jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name, arguments: args } },
  });
  const raw = await response.text();
  if (!response.ok()) return { status: response.status(), text: raw };
  const json = (response.headers()['content-type'] ?? '').startsWith('text/event-stream')
    ? raw.split('\n').filter((line) => line.startsWith('data:')).map((line) => line.slice(5).trim()).join('\n')
    : raw;
  const { result } = JSON.parse(json) as { result: { isError?: boolean; content: { text: string }[] } };
  expect(result.isError ?? false, JSON.stringify(result)).toBe(false);
  return { status: response.status(), text: result.content[0].text };
}

test('un cliente conecta su asistente con su cuenta: le da permiso, el asistente le prepara su pedido de siempre y lo desconecta', async ({
  page, browser, baseURL, request,
}) => {
  // A taquería that takes orders online.
  const owner = newOwner();
  await signUpBusiness(page, owner);
  await setUpWithTaqueria(page);
  await openDrawerFromSell(page, '300');
  await page.goto('/settings/business');
  await page.getByRole('radiogroup', { name: 'Menú en línea' }).getByRole('radio', { name: 'Menú con pedidos' }).click();
  await expect(page.getByText('Menú en línea actualizado.')).toBeVisible();
  const menuPath = new URL(await page.getByLabel('Enlace', { exact: true }).inputValue()).pathname;

  // A customer with an account, who ordered there before.
  const email = owner.email.replace('duena.', 'cliente.');
  const registered = await request.post('/api/v1/auth/register/customer', {
    data: { email, password: PASSWORD, firstName: 'Andrés', lastName: 'Pérez', phoneNumber: `+52155${Date.now().toString().slice(-8)}` },
  });
  expect(registered.ok(), await registered.text()).toBe(true);
  const customerDevice = await browser.newContext({ baseURL, locale: 'es-MX', timezoneId: 'America/Mexico_City' });
  const customer = await customerDevice.newPage();
  await customer.goto(await activationLink(email));
  await expect(customer).toHaveURL(/\/login/);
  await customer.getByLabel('Correo').fill(email);
  await customer.getByRole('textbox', { name: /^Contraseña/ }).fill(PASSWORD);
  await customer.getByRole('button', { name: /Iniciar sesión|Entrar/ }).click();
  await expect(customer).toHaveURL(/\/explorar/);
  await customer.goto(menuPath);
  await customer.getByRole('button', { name: /^Agregar Taco de pastor/ }).click();
  await customer.getByRole('button', { name: /^Agregar Taco de pastor/ }).click();
  await customer.getByRole('button', { name: /^Ver pedido · 2 · \$44\.00/ }).click();
  const checkout = customer.getByRole('dialog', { name: 'Tu pedido' });
  await checkout.getByRole('button', { name: 'Hacer pedido · $44.00' }).click();
  await expect(customer.getByRole('heading', { name: '¡Pedido enviado!' })).toBeVisible();
  const orderNumber = await customer.getByRole('definition').filter({ hasText: /^ORD-/ }).innerText();

  // The assistant registers, as Claude does when the customer adds the connector with their account.
  const client = await request.post('/oauth/register', {
    data: { client_name: 'Claude', redirect_uris: [CALLBACK], token_endpoint_auth_method: 'none' },
  });
  expect(client.status(), await client.text()).toBe(201);
  const { client_id: clientId } = await client.json() as { client_id: string };
  const verifier = randomBytes(32).toString('base64url');
  const challenge = createHash('sha256').update(verifier).digest('base64url');
  const resource = `${baseURL}/mcp/cuenta`;

  // The browser goes to authorize: TienditApp asks the customer, who allows it; it returns to the assistant with a code.
  await customer.route(`${CALLBACK}**`, (route) => route.fulfill({ status: 200, contentType: 'text/plain', body: 'Listo' }));
  await customer.goto(`/oauth/authorize?${new URLSearchParams({
    response_type: 'code', client_id: clientId, redirect_uri: CALLBACK, code_challenge: challenge, code_challenge_method: 'S256',
    resource, scope: 'cuenta', state: 'e2e',
  })}`);
  await expect(customer).toHaveURL(/\/autorizar\?/);
  await expect(customer.getByRole('heading', { name: 'Claude quiere usar tu cuenta de TienditApp' })).toBeVisible();
  await expect(customer.getByRole('note')).toContainText('Regresa a una aplicación de esta computadora');
  await customer.screenshot({ path: test.info().outputPath('permiso-del-asistente.png') });
  await customer.getByRole('button', { name: 'Permitir', exact: true }).click();
  await expect(customer).toHaveURL(new RegExp(`^${CALLBACK.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')}\\?code=[A-Za-z0-9_-]{43}&state=e2e$`));
  const code = new URL(customer.url()).searchParams.get('code')!;

  // The code becomes its tokens, with the verifier only the assistant has.
  const exchanged = await request.post('/oauth/token', {
    form: { grant_type: 'authorization_code', code, redirect_uri: CALLBACK, client_id: clientId, code_verifier: verifier, resource },
  });
  expect(exchanged.ok(), await exchanged.text()).toBe(true);
  const { access_token: accessToken } = await exchanged.json() as { access_token: string };

  // With the account, it knows what the customer ordered and prepares it again; it still sends nothing.
  const orders = await accountTool(request, accessToken, 'mis_pedidos', {});
  expect(orders.text).toContain(orderNumber);
  expect(orders.text).toContain('2 × Taco de pastor');
  const orderId = orders.text.match(/- id: `([0-9a-f-]{36})`/)?.[1];
  expect(orderId, orders.text).toBeDefined();
  const again = await accountTool(request, accessToken, 'pedir_otra_vez', { pedido: orderId });
  expect(again.text).toContain('Todavía no se envía.');
  expect(again.text).toContain('- 2 × Taco de pastor · $44.00');
  const link = again.text.match(new RegExp(`Enlace: (\\S+${menuPath}#pedido=[A-Za-z0-9_-]{43})`))?.[1];
  expect(link, again.text).toBeDefined();
  await customer.goto(link!);
  await expect(customer.getByRole('dialog', { name: 'Tu pedido' }).getByRole('status'))
    .toHaveText('Tu asistente preparó este pedido. Revísalo, escribe tus datos y envíalo: todavía no le llega al negocio.');

  // Mi cuenta lists the assistant; disconnected, its next request is refused.
  await customer.goto('/cuenta');
  const assistants = customer.getByRole('region', { name: 'Asistentes conectados' });
  await expect(assistants.getByText('Claude')).toBeVisible();
  await expect(assistants).toContainText('127.0.0.1:43210');
  await assistants.getByRole('button', { name: 'Desconectar Claude' }).click();
  await customer.getByRole('dialog', { name: 'Desconectar asistente' }).getByRole('button', { name: 'Desconectar' }).click();
  await expect(customer.getByText('Claude: desconectado.')).toBeVisible();
  await expect(assistants.getByText('No tienes asistentes conectados.')).toBeVisible();
  expect((await accountTool(request, accessToken, 'mis_pedidos', {})).status).toBe(401);
  await customerDevice.close();
});
