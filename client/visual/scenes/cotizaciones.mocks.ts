import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { publicRoutes } from './publico-cuenta-marco.mocks';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// The quotes (Fase 29C, ADR 0053) of Plomería Hernández, a plumber whose prices do not include IVA: the list of open ones,
// a draft from a visit (its visit taken off, «Se descuenta si contratas»), the same one sent with its link, and the page the
// customer opens without an account. Numbers as the server totals them.

const DRAFT_ID = '7a3f1c52-2d4b-4e6a-9c1f-0b8e5d2a7c41';
const SENT_ID = '0c9d2e71-5b3a-4f8e-a6d4-3e1b7c9f2a58';

/** The link of the sent quote: 43 characters, as TienditApp makes them. */
export const QUOTE_LINK = 'Qt5Lm8Rv2PxK7nWc4Yb9Hd1Mf6Js3Ga0Ue-_RtXpZa';
/** The link of its first version, replaced by the second. */
export const REPLACED_QUOTE_LINK = 'Rp4Kq9Lm2Xc7Vz1Hd6Pf3Js8Ga0Ue5Yt-_RwTiOpQs';

const LINES = [
  { position: 1, productId: 'svc-calentador', description: 'Cambio de calentador de paso', quantity: 1, unitPrice: 2800, taxRate: 16, isTaxExempt: false, total: 2800 },
  { position: 2, productId: null, description: 'Tubería de cobre de 1/2" (metro)', quantity: 3, unitPrice: 180, taxRate: 16, isTaxExempt: false, total: 540 },
  { position: 3, productId: null, description: 'Mano de obra', quantity: 2, unitPrice: 350, taxRate: 16, isTaxExempt: false, total: 700 },
];

// 4,040 plus 16 % IVA (646.40) is 4,686.40; the visit paid, $464.00 (400 plus IVA), comes off.
const TOTALS = {
  subTotal: 4040,
  tax: 646.4,
  taxes: [{ ratePercent: 16, isExempt: false, taxableBase: 4040, taxAmount: 646.4 }],
  total: 4686.4,
  visitCredit: { amount: 464, applies: true },
  toPay: 4222.4,
};

const APPOINTMENT = { id: 'appt-valeria', startAt: '2026-10-02T17:00:00Z', serviceName: 'Visita y diagnóstico', postalCode: '06700' };

const DRAFT = {
  id: DRAFT_ID,
  number: 'COT-000012',
  revision: 1,
  status: 0,
  customerId: null,
  customerName: 'Valeria Ruiz',
  customerPhone: '5522107765',
  customerEmail: 'valeria@correo.mx',
  appointment: APPOINTMENT,
  pricesIncludeTax: false,
  validityDays: 15,
  validUntil: null,
  note: 'Incluye retirar el calentador viejo. El material se compra al aceptar.',
  lines: LINES,
  ...TOTALS,
  sentAt: null,
  trackingToken: null,
  closedAt: null,
  replyChannel: null,
  customerResponse: null,
  draftRevisionId: null,
  versions: [{ id: DRAFT_ID, revision: 1, status: 0, total: 4686.4, sentAt: null, closedAt: null }],
  createdAt: '2026-10-03T16:00:00Z',
};

const SENT = {
  ...DRAFT,
  id: SENT_ID,
  number: 'COT-000011',
  revision: 2,
  status: 1,
  validUntil: '2026-10-19',
  sentAt: '2026-10-04T16:20:00Z',
  trackingToken: QUOTE_LINK,
  versions: [
    { id: 'replaced', revision: 1, status: 5, total: 5150.4, sentAt: '2026-10-03T15:00:00Z', closedAt: '2026-10-04T16:20:00Z' },
    { id: SENT_ID, revision: 2, status: 1, total: 4686.4, sentAt: '2026-10-04T16:20:00Z', closedAt: null },
  ],
};

const SUMMARIES = [
  { id: DRAFT_ID, number: 'COT-000012', revision: 1, status: 0, customerName: 'Valeria Ruiz', customerPhone: '5522107765', total: 4686.4, validUntil: null, updatedAt: '2026-10-04T16:30:00Z', fromVisit: true },
  { id: SENT_ID, number: 'COT-000011', revision: 2, status: 1, customerName: 'Jorge Salinas', customerPhone: '5544332211', total: 4686.4, validUntil: '2026-10-19', updatedAt: '2026-10-04T16:20:00Z', fromVisit: false },
  { id: 'q-9', number: 'COT-000009', revision: 1, status: 1, customerName: 'Condominio Ámsterdam 210', customerPhone: '5598761234', total: 12528, validUntil: '2026-10-08', updatedAt: '2026-09-23T18:00:00Z', fromVisit: true },
];

/** The owner of the plumbing business, its prices without IVA. */
function plumber(data: DemoData) {
  data.tenant.name = 'Plomería Hernández';
  data.tenant.industry = 'Plomería';
  data.tenant.pricesIncludeTax = false;
}

const quotesRoutes: ExtraRoutes = (_data, { ok, paginate }) => [
  ['GET', /^\/quotes$/, (_match, query) => ok(paginate(SUMMARIES, query, 20))],
  ['GET', /^\/quotes\/settings$/, () => ok({ validityDays: 15, pricesIncludeTax: false, defaultTaxRate: 16 })],
  ['GET', new RegExp(`^/quotes/${DRAFT_ID}$`), () => ok(DRAFT)],
  ['GET', new RegExp(`^/quotes/${SENT_ID}$`), () => ok(SENT)],
];

/** What the customer reads: the folio, the lines, the IVA on top, the visit taken off and until when. */
const PUBLIC_QUOTE = {
  businessName: 'Plomería Hernández',
  businessSlug: 'plomeria-hernandez',
  businessContactPhone: '5512345678',
  number: 'COT-000011',
  revision: 2,
  status: 1,
  customerName: 'Valeria Ruiz',
  sentAt: '2026-10-04T16:20:00Z',
  validUntil: '2026-10-19',
  pricesIncludeTax: false,
  lines: LINES.map(({ description, quantity, unitPrice, total }) => ({ description, quantity, unitPrice, total })),
  ...TOTALS,
  note: DRAFT.note,
  newerToken: null,
};

function problem(status: number, detail: string) {
  return { status, json: { status, title: 'Not Found', detail, code: 'not_found', error: detail, traceId: '00-visual-00' } };
}

const publicQuoteRoutes: ExtraRoutes = (data, helpers) => [
  ['POST', /^\/public\/quotes\/view$/, (_match, _query, body: { token?: string }) => {
    if (body?.token === QUOTE_LINK) return helpers.ok(PUBLIC_QUOTE);
    if (body?.token === REPLACED_QUOTE_LINK) {
      return helpers.ok({ ...PUBLIC_QUOTE, revision: 1, status: 5, total: 5150.4, toPay: 4686.4, newerToken: QUOTE_LINK });
    }
    return problem(404, 'Esta cotización no existe o su enlace ya venció. Pídele al negocio que te la mande otra vez.');
  }],
  ...publicRoutes(data, helpers),
];

export const DRAFT_QUOTE_ID = DRAFT_ID;
export const SENT_QUOTE_ID = SENT_ID;
export const PLUMBER_QUOTES = asOption(plumber);
export const QUOTES_ROUTES = asOption(quotesRoutes);
export const PUBLIC_QUOTE_ROUTES = asOption(publicQuoteRoutes);
