import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO } from '../mocks/data.mjs';
import { publicRoutes } from './publico-cuenta-marco.mocks';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;
type PrepareData = NonNullable<SceneOptions['prepareData']>;

// Self-invoicing (Fase 28F, ADR 0050): the page of Café La Esquina where its customers invoice their tickets, without an
// account. The ticket is ORD-000418, today's two lattes and a croissant ($231.00) paid in cash; the code printed on it is
// K7M2Q-9XH4C. What the ticket says (`data.selfInvoiceTicket`) and how its invoice goes (`data.selfInvoiceOutcome`) change
// with each scene.

// SelfInvoiceTicketStatus and SelfInvoiceOutcome as the contract numbers them.
const CAN_INVOICE = 0;
const INVOICED = 1;
const NOT_ONLINE = 3;
const STAMPED = 0;
const REFUSED = 2;

export const SLUG = 'cafe-la-esquina';
export const FOLIO = 'ORD-000418';
export const CODE = 'K7M2Q-9XH4C';
/** The ticket's QR: the page with its folio and code in the fragment, which the browser never sends. */
export const FROM_QR = `/m/${SLUG}/facturar#${FOLIO}.${CODE}`;

const NOT_FOUND = 'No encontramos ese ticket: revisa el folio y el código impresos en él.';

/** An error as the API answers it (ApiProblem). */
function problem(status: number, detail: string) {
  return { status, json: { status, title: 'Not Found', detail, code: 'not_found', error: detail, traceId: '00-visual-00' } };
}

const minutesAgo = (minutes: number) => new Date(Date.parse(NOW_ISO) - minutes * 60_000).toISOString();

/** The ticket as the page looks it up: paid an hour ago, and invoiced online until the end of October. */
function ticket(data: DemoData) {
  return {
    orderNumber: FOLIO, date: minutesAgo(62), total: 231, status: CAN_INVOICE, invoiceNumber: null, message: null,
    invoiceBy: '2026-10-31', ...data.selfInvoiceTicket,
  };
}

const prepare = (change: (data: DemoData) => void): PrepareData => change;

/** The ticket can be invoiced, and the PAC stamps it. */
export const POR_FACTURAR = asOption(prepare((data) => {
  data.selfInvoiceOutcome = { outcome: STAMPED, message: null };
}));
/** The PAC refuses the name: it is not the one the SAT has for that RFC. */
export const RECHAZADA = asOption(prepare((data) => {
  data.selfInvoiceOutcome = {
    outcome: REFUSED,
    message: 'CFDI40145 - El nombre del receptor no coincide con el del RFC en la lista del SAT.',
  };
}));
/** Invoiced already (F-27): the page sends it again to whoever writes its RFC. */
export const YA_FACTURADO = asOption(prepare((data) => {
  data.selfInvoiceTicket = { status: INVOICED, invoiceNumber: 'F-27', invoiceBy: null };
}));
/** Paid in September: online, it could be invoiced until the last day of that month. */
export const FUERA_DE_PLAZO = asOption(prepare((data) => {
  data.selfInvoiceTicket = {
    date: '2026-09-27T19:40:00Z', invoiceBy: null, status: NOT_ONLINE,
    message: 'Esta compra se podía facturar en línea hasta el miércoles 30 de septiembre de 2026: pídele tu factura al negocio.',
  };
}));
/** The business turned self-invoicing off. */
export const NO_DISPONIBLE = asOption(prepare((data) => {
  data.selfInvoiceOff = true;
}));

const selfInvoiceRoutes: ExtraRoutes = (data, helpers) => {
  const ticketOf = (body: { folio?: string; code?: string }) => (body?.folio === FOLIO && body.code === CODE ? ticket(data) : null);
  return [
    ['GET', /^\/public\/self-invoice\/([^/]+)$/, (match) => (match[1] === SLUG
      ? helpers.ok({ businessName: data.tenant.name, available: !data.selfInvoiceOff, sandbox: false })
      : problem(404, 'No encontramos ese negocio.'))],
    ['POST', /^\/public\/self-invoice\/([^/]+)\/ticket$/, (_match, _query, body) => {
      const found = ticketOf(body);
      return found ? helpers.ok(found) : problem(404, NOT_FOUND);
    }],
    ['POST', /^\/public\/self-invoice\/([^/]+)\/invoice$/, (_match, _query, body) => (ticketOf(body)
      ? helpers.ok({ invoiceNumber: 'F-27', email: body.email, ...data.selfInvoiceOutcome })
      : problem(404, NOT_FOUND))],
    ['POST', /^\/public\/self-invoice\/([^/]+)\/resend$/, (_match, _query, body) => (ticketOf(body)
      ? helpers.ok({ invoiceNumber: 'F-27', email: body.email })
      : problem(404, NOT_FOUND))],
    ...publicRoutes(data, helpers),
  ];
};

export const AUTOFACTURA_ROUTES = asOption(selfInvoiceRoutes);
