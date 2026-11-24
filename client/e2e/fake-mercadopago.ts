import { createHash, randomBytes, randomInt } from 'node:crypto';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { BrowserContext, Route } from '@playwright/test';

/**
 * Mercado Pago, the one service outside TienditApp that the end-to-end tests stand in for (README.md): the real one needs
 * TienditApp's application in Mercado Pago, accounts that receive money and the internet. Everything on TienditApp's
 * side is real: the API calls this server as it calls Mercado Pago (start-api.mjs points `MercadoPago:ApiBaseUrl` here),
 * and the browser goes to Mercado Pago's pages, which `routeMercadoPagoPages` answers as Mercado Pago would: the owner
 * authorizes and the customer pays with an approved card, at once.
 *
 * It checks what Mercado Pago checks of TienditApp: the application's id, the PKCE verifier of the code, the account's
 * key on every call, and that a payment is read only with the key of the account that collected it.
 */

/** Where the API finds this server: start-api.mjs gives the API the same port. */
export const fakeMercadoPagoPort = Number(process.env.E2E_MP_PORT ?? 5298);
/** The application's id start-api.mjs configures: any other is refused, as Mercado Pago would. */
export const fakeMercadoPagoClientId = 'tienditapp-e2e';
/** Mercado Pago's pages for the browser (authorize, pay): https, as the app only leaves for an https page. */
export const mercadoPagoPages = 'https://mercadopago.e2e.test';

interface Account {
  userId: number;
  accessToken: string;
  refreshToken: string;
}

interface Preference {
  id: string;
  amount: number;
  externalReference: string;
  successUrl: string;
  collectorId: number;
}

export interface FakePayment {
  id: number;
  status: 'approved';
  status_detail: 'accredited';
  external_reference: string;
  transaction_amount: number;
  transaction_amount_refunded: number;
  currency_id: 'MXN';
  date_approved: string;
  date_last_updated: string;
  collector_id: number;
  live_mode: true;
}

export interface FakeMercadoPago {
  /** The preferences the API created: what it asked the customer to pay. */
  readonly preferences: readonly Preference[];
  /** The payments the customers made, all approved. */
  readonly payments: readonly FakePayment[];
  /** Each call of the API, as «METHOD /path»: what TienditApp asked Mercado Pago. */
  readonly calls: readonly string[];
  close(): Promise<void>;
}

/** Starts the stand-in on its port. Close it at the end of the test. */
export async function startFakeMercadoPago(port = fakeMercadoPagoPort): Promise<FakeMercadoPago & { server: Server }> {
  const codes = new Map<string, { challenge: string; redirectUri: string }>();
  const accounts: Account[] = [];
  const preferences: Preference[] = [];
  const payments: FakePayment[] = [];
  const calls: string[] = [];

  const server = createServer((request, response) => {
    void handle(request, response).catch((error: unknown) => {
      json(response, 500, { message: error instanceof Error ? error.message : String(error) });
    });
  });

  async function handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const url = new URL(request.url ?? '/', `http://127.0.0.1:${port}`);
    calls.push(`${request.method} ${url.pathname}`);
    const account = accountOf(request);

    if (request.method === 'POST' && url.pathname === '/oauth/token') {
      const body = await readJson(request);
      if (body.client_id !== fakeMercadoPagoClientId) return json(response, 401, { error: 'invalid_client' });
      if (body.grant_type === 'authorization_code') {
        const code = codes.get(String(body.code));
        codes.delete(String(body.code));
        const verifier = String(body.code_verifier ?? '');
        if (!code || code.redirectUri !== body.redirect_uri
          || createHash('sha256').update(verifier).digest('base64url') !== code.challenge) {
          return json(response, 400, { error: 'invalid_grant' });
        }
        const created: Account = { userId: randomInt(100_000_000, 999_999_999), accessToken: key('APP_USR'), refreshToken: key('TG') };
        accounts.push(created);
        return json(response, 200, tokenResponse(created));
      }
      if (body.grant_type === 'refresh_token') {
        const renewed = accounts.find((a) => a.refreshToken === body.refresh_token);
        if (!renewed) return json(response, 400, { error: 'invalid_grant' });
        renewed.accessToken = key('APP_USR');
        renewed.refreshToken = key('TG');
        return json(response, 200, tokenResponse(renewed));
      }
      return json(response, 400, { error: 'unsupported_grant_type' });
    }

    if (!account) return json(response, 401, { message: 'invalid access token' });

    if (request.method === 'POST' && url.pathname === '/checkout/preferences') {
      const body = await readJson(request);
      const item = (body.items as { unit_price: number }[] | undefined)?.[0];
      const successUrl = (body.back_urls as { success?: string } | undefined)?.success;
      if (!item || typeof body.external_reference !== 'string' || !successUrl) return json(response, 400, { message: 'bad request' });
      const preference: Preference = {
        id: `${account.userId}-${randomBytes(6).toString('hex')}`,
        amount: Number(item.unit_price),
        externalReference: body.external_reference,
        successUrl,
        collectorId: account.userId,
      };
      preferences.push(preference);
      const page = `${mercadoPagoPages}/checkout/v1/redirect?pref_id=${preference.id}`;
      return json(response, 201, { id: preference.id, init_point: page, sandbox_init_point: page });
    }

    const own = payments.filter((p) => p.collector_id === account.userId);
    if (request.method === 'GET' && url.pathname === '/v1/payments/search') {
      const reference = url.searchParams.get('external_reference');
      const results = reference ? own.filter((p) => p.external_reference === reference) : own;
      return json(response, 200, { results, paging: { total: results.length, offset: 0, limit: results.length } });
    }
    const refunds = /^\/v1\/payments\/(\d+)\/refunds$/.exec(url.pathname);
    if (request.method === 'GET' && refunds) {
      return own.some((p) => String(p.id) === refunds[1]) ? json(response, 200, []) : json(response, 404, { message: 'not found' });
    }
    const payment = /^\/v1\/payments\/(\d+)$/.exec(url.pathname);
    if (request.method === 'GET' && payment) {
      const found = own.find((p) => String(p.id) === payment[1]);
      return found ? json(response, 200, found) : json(response, 404, { message: 'Payment not found' });
    }
    return json(response, 404, { message: `${request.method} ${url.pathname} is not part of the stand-in` });
  }

  function accountOf(request: IncomingMessage): Account | undefined {
    const bearer = /^Bearer (.+)$/.exec(request.headers.authorization ?? '')?.[1];
    return bearer ? accounts.find((a) => a.accessToken === bearer) : undefined;
  }

  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', () => resolve());
  });

  const fake = {
    server,
    preferences,
    payments,
    calls,
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
    // The browser's side, kept with the server's state: see routeMercadoPagoPages.
    authorize(params: URLSearchParams): string {
      if (params.get('client_id') !== fakeMercadoPagoClientId || params.get('code_challenge_method') !== 'S256') {
        throw new Error(`Mercado Pago would not show its authorization for ${params.toString()}`);
      }
      const redirectUri = params.get('redirect_uri') ?? '';
      const code = key('TG-code');
      codes.set(code, { challenge: params.get('code_challenge') ?? '', redirectUri });
      return `${redirectUri}?code=${encodeURIComponent(code)}&state=${encodeURIComponent(params.get('state') ?? '')}`;
    },
    pay(preferenceId: string): string {
      const preference = preferences.find((p) => p.id === preferenceId);
      if (!preference) throw new Error(`Mercado Pago has no preference ${preferenceId}`);
      const now = new Date().toISOString();
      const paid: FakePayment = {
        id: randomInt(10_000_000_000, 99_999_999_999),
        status: 'approved',
        status_detail: 'accredited',
        external_reference: preference.externalReference,
        transaction_amount: preference.amount,
        transaction_amount_refunded: 0,
        currency_id: 'MXN',
        date_approved: now,
        date_last_updated: now,
        collector_id: preference.collectorId,
        live_mode: true,
      };
      payments.push(paid);
      // What Mercado Pago adds to the back URL of an approved payment.
      const back = new URL(preference.successUrl);
      for (const [name, value] of Object.entries({
        collection_id: String(paid.id), collection_status: 'approved', payment_id: String(paid.id), status: 'approved',
        external_reference: preference.externalReference, payment_type: 'credit_card', preference_id: preference.id,
        site_id: 'MLM', processing_mode: 'aggregator', merchant_account_id: 'null',
      })) back.searchParams.set(name, value);
      return back.toString();
    },
  };
  fakes.set(server, fake);
  return fake;
}

const fakes = new WeakMap<Server, { authorize(params: URLSearchParams): string; pay(preferenceId: string): string }>();

/**
 * Answers Mercado Pago's pages in this browser context as Mercado Pago would: the authorization sends the owner back with
 * a code at once (they said yes), and the checkout sends the customer back with an approved payment.
 */
export async function routeMercadoPagoPages(context: BrowserContext, fake: FakeMercadoPago & { server: Server }): Promise<void> {
  const pages = fakes.get(fake.server);
  if (!pages) throw new Error('Start the stand-in with startFakeMercadoPago first.');
  await context.route(`${mercadoPagoPages}/**`, async (route: Route) => {
    const url = new URL(route.request().url());
    const location = url.pathname === '/authorization'
      ? pages.authorize(url.searchParams)
      : url.pathname === '/checkout/v1/redirect'
        ? pages.pay(url.searchParams.get('pref_id') ?? '')
        : null;
    if (!location) return route.fulfill({ status: 404, body: `${url.pathname} is not part of the stand-in` });
    return route.fulfill({ status: 302, headers: { location } });
  });
}

function tokenResponse(account: Account) {
  return {
    access_token: account.accessToken,
    token_type: 'Bearer',
    expires_in: 15_552_000,
    scope: 'offline_access payments read write',
    user_id: account.userId,
    refresh_token: account.refreshToken,
    public_key: key('APP_USR-public'),
    live_mode: true,
  };
}

function key(prefix: string): string {
  return `${prefix}-${randomBytes(24).toString('hex')}`;
}

async function readJson(request: IncomingMessage): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) chunks.push(chunk as Buffer);
  const text = Buffer.concat(chunks).toString('utf8');
  return text ? (JSON.parse(text) as Record<string, unknown>) : {};
}

function json(response: ServerResponse, status: number, body: unknown): void {
  response.writeHead(status, { 'content-type': 'application/json' });
  response.end(JSON.stringify(body));
}
