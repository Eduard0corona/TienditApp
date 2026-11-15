import { expect, test, type APIRequestContext } from '@playwright/test';
import { newOwner, openDrawerFromSell, setUpWithTaqueria, signUpBusiness } from '../support';

/**
 * One tool call to the MCP server (Fase 32B, ADR 0096), as an assistant's client sends it over Streamable HTTP: the text
 * the tool answers. The server answers JSON or one server-sent event with it.
 */
async function tool(request: APIRequestContext, name: string, args: Record<string, unknown>): Promise<string> {
  const response = await request.post('/mcp', {
    headers: { Accept: 'application/json, text/event-stream', 'MCP-Protocol-Version': '2025-06-18' },
    data: { jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name, arguments: args } },
  });
  const raw = await response.text();
  expect(response.ok(), raw).toBe(true);
  const json = (response.headers()['content-type'] ?? '').startsWith('text/event-stream')
    ? raw.split('\n').filter((line) => line.startsWith('data:')).map((line) => line.slice(5).trim()).join('\n')
    : raw;
  const { result } = JSON.parse(json) as { result: { isError?: boolean; content: { text: string }[] } };
  expect(result.isError ?? false, JSON.stringify(result)).toBe(false);
  return result.content[0].text;
}

const escaped = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

test('un cliente pide desde su asistente de IA: lo busca, recibe el enlace, envía el pedido y el asistente lo ve', async ({
  page, browser, baseURL, request,
}) => {
  const owner = newOwner();
  await signUpBusiness(page, owner);
  await setUpWithTaqueria(page);
  await openDrawerFromSell(page, '300');
  await page.goto('/settings/business');
  await page.getByRole('radiogroup', { name: 'Menú en línea' }).getByRole('radio', { name: 'Menú con pedidos' }).click();
  await expect(page.getByText('Menú en línea actualizado.')).toBeVisible();
  const menuPath = new URL(await page.getByLabel('Enlace', { exact: true }).inputValue()).pathname;
  const slug = menuPath.split('/').pop()!;

  // The assistant looks for the dish and finds who sells it, from the same index as Explorar (rebuilt every 30 seconds).
  await expect(async () => {
    const found = await tool(request, 'buscar_negocios', { texto: 'taco de pastor', categoria: 'comida' });
    expect(found).toContain('## Productos que coinciden');
    expect(found).toMatch(/- Taco de pastor · \$22\.00 · en .+ \(slug `[a-z0-9-]+`\) · id `[0-9a-f-]{36}`/);
  }).toPass({ timeout: 75_000, intervals: [2_000, 5_000] });
  // And this business by its name, with its menu and the ids to prepare the order.
  await expect(async () => {
    expect(await tool(request, 'buscar_negocios', { texto: owner.businessName })).toContain(`- slug: \`${slug}\``);
  }).toPass({ timeout: 75_000, intervals: [2_000, 5_000] });
  const menu = await tool(request, 'ver_menu', { slug });
  const pastor = menu.match(/#### Taco de pastor · \$22\.00\n- id: `([0-9a-f-]{36})`/)?.[1];
  expect(pastor, menu).toBeDefined();

  // It prepares the order: nothing reaches the business, the customer gets a link.
  const prepared = await tool(request, 'preparar_pedido', {
    slug, productos: [{ id: pastor, cantidad: 2, nota: 'con todo' }],
  });
  expect(prepared).toContain('Todavía no se envía.');
  expect(prepared).toContain('- 2 × Taco de pastor · $44.00 · nota: con todo');
  expect(prepared).toMatch(/Vence el \d{1,2} de \p{L}+ a las \d{2}:\d{2} \(hora del negocio\)\./u);
  const link = prepared.match(new RegExp(`Enlace: (${escaped(baseURL!)}/m/${slug}#pedido=[A-Za-z0-9_-]{43})`))?.[1];
  expect(link, prepared).toBeDefined();
  expect(await tool(request, 'ver_pedido', { enlace: link })).toContain('Todavía no lo envía.');

  // The customer opens it on their phone: the cart, open with what the assistant chose, says it is still to send.
  const customerDevice = await browser.newContext({ baseURL, locale: 'es-MX', timezoneId: 'America/Mexico_City' });
  const customer = await customerDevice.newPage();
  await customer.goto(link!);
  const checkout = customer.getByRole('dialog', { name: 'Tu pedido' });
  await expect(checkout.getByRole('status')).toHaveText(
    'Tu asistente preparó este pedido. Revísalo, escribe tus datos y envíalo: todavía no le llega al negocio.',
  );
  await expect(checkout.getByText('Taco de pastor')).toBeVisible();
  await expect(checkout.getByText('«con todo»')).toBeVisible();
  await checkout.screenshot({ path: test.info().outputPath('carrito-del-asistente.png') });
  await checkout.getByLabel('Tu nombre').fill('Andrés Pérez');
  await checkout.getByLabel('Teléfono (WhatsApp)').fill('5587654321');
  await checkout.getByRole('button', { name: 'Hacer pedido · $44.00' }).click();
  await expect(customer.getByRole('heading', { name: '¡Pedido enviado!' })).toBeVisible();
  const orderNumber = await customer.getByRole('definition').filter({ hasText: /^ORD-/ }).innerText();

  // The assistant, asked whether it arrived, sees it sent with its folio; the link opened again says so too.
  expect(await tool(request, 'ver_pedido', { enlace: link })).toContain(`El cliente lo envió. Folio: ${orderNumber}.`);
  await customer.goto('about:blank');
  await customer.goto(link!);
  await expect(customer.getByText('Este pedido ya se envió. Síguelo con el enlace que te dio la página al enviarlo.')).toBeVisible();
  await customerDevice.close();

  // The business finds it in Pedidos like any order from its menu.
  await page.goto('/orders');
  const row = page.getByRole('row').filter({ hasText: orderNumber });
  await expect(row.getByRole('cell', { name: `${orderNumber} En línea` })).toBeVisible();
  await expect(row.getByRole('cell', { name: 'Andrés Pérez +525587654321' })).toBeVisible();
  await expect(row.getByRole('cell', { name: '$44.00 Por cobrar $44.00' })).toBeVisible();
});
