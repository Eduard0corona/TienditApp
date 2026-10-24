import { asOption, type SceneOptions } from '../fixtures';
import { uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// The global invoice (Fase 28B, ADR 0046): Reportes › Factura global of Café La Esquina on Sunday 2026-10-04. The list
// (yesterday's stamped, a weekly one cancelled with 04 and issued again, one still stamping and one refused), the preview
// of yesterday's (ready, and one that cannot be stamped yet) and the detail of a stamped one.

// OrderInvoiceStatus, GlobalInvoicePeriodicity and CfdiCancellationMotive as the contract numbers them.
const STAMPING = 0;
const STAMPED = 1;
const REFUSED = 2;
const CANCELLED = 4;
const DAILY = 1;
const WEEKLY = 2;
const MONTHLY = 4;
const NOMINATIVE_IN_GLOBAL = 4;

const r2 = (amount: number) => Math.round(amount * 100) / 100;

function global(folio: number, fields: Record<string, unknown>) {
  const total = Number(fields.total ?? 1450);
  const subTotal = r2(total / 1.16);
  return {
    id: uuid(`global-${folio}`), number: `F-${folio}`, status: STAMPED, periodicity: DAILY,
    periodFrom: '2026-10-03', periodTo: '2026-10-03', months: '10', year: 2026, orderCount: 12, paymentForm: '01',
    subTotal, taxTotal: r2(total - subTotal), total, requestedAt: '2026-10-04T14:05:00Z',
    uuid: null, stampedAt: null, hasXml: false, hasPdf: false, failureMessage: null, refusedAt: null,
    cancellationRequestedAt: null, cancelledAt: null,
    ...fields,
  };
}

// A literal id: the scene opens its detail by it, and the coverage check reads the URL as written.
const GLOBAL_STAMPED_ID = '5f0c8a3e-2d41-4b7a-9e6f-1c2b3a4d5e58';

const STAMPED_YESTERDAY = global(58, {
  id: GLOBAL_STAMPED_ID,
  total: 1450, uuid: '8E3A1F42-7C0B-4D3E-9A71-2B6C5D4E3F10', stampedAt: '2026-10-04T14:05:03Z', hasXml: true, hasPdf: true,
});

const LIST = [
  STAMPED_YESTERDAY,
  global(57, { periodFrom: '2026-10-02', periodTo: '2026-10-02', total: 980, orderCount: 8, status: STAMPING, requestedAt: '2026-10-03T14:10:00Z' }),
  global(56, {
    periodicity: WEEKLY, periodFrom: '2026-09-28', periodTo: '2026-09-30', total: 6240.5, orderCount: 41,
    uuid: '1D9B7C6A-5E4F-4321-8B0A-9C8D7E6F5A41', stampedAt: '2026-10-01T15:00:02Z', hasXml: true, hasPdf: true,
  }),
  global(55, {
    periodicity: WEEKLY, periodFrom: '2026-09-28', periodTo: '2026-09-30', total: 6480.5, orderCount: 42,
    uuid: '0A1B2C3D-4E5F-4061-8728-394A5B6C7D8E', stampedAt: '2026-10-01T14:30:02Z', hasXml: true, hasPdf: true,
    status: CANCELLED, cancellationMotive: NOMINATIVE_IN_GLOBAL, cancellationRequestedAt: '2026-10-01T14:55:00Z',
    cancelledAt: '2026-10-01T14:55:04Z',
  }),
  global(54, {
    periodicity: MONTHLY, periodFrom: '2026-09-01', periodTo: '2026-09-30', total: 3120, orderCount: 25, status: REFUSED,
    refusedAt: '2026-10-01T14:00:03Z', failureMessage: 'El PAC rechazó la factura: CFDI40149 - El código postal del receptor debe ser el del lugar de expedición.',
  }),
];

const SALES = [
  { number: 'ORD-000331', paidAt: '2026-10-03T15:12:00Z', total: 185 },
  { number: 'ORD-000332', paidAt: '2026-10-03T15:40:00Z', total: 96 },
  { number: 'ORD-000334', paidAt: '2026-10-03T16:05:00Z', total: 240 },
  { number: 'ORD-000335', paidAt: '2026-10-03T17:22:00Z', total: 129 },
  { number: 'ORD-000337', paidAt: '2026-10-03T19:48:00Z', total: 310 },
  { number: 'ORD-000338', paidAt: '2026-10-03T21:15:00Z', total: 74 },
].map((sale) => ({ orderId: uuid(`sale-${sale.number}`), orderNumber: sale.number, paidAt: sale.paidAt, total: sale.total }));

const DRAFT_TOTAL = SALES.reduce((sum, sale) => sum + sale.total, 0);
const DRAFT_SUBTOTAL = r2(DRAFT_TOTAL / 1.16);

/** Yesterday's: six sales paid in full, two left out with their reason, ready to stamp. */
const READY_DRAFT = {
  periodicity: DAILY, periodFrom: '2026-10-03', periodTo: '2026-10-03', months: '10', year: 2026, available: true,
  sandbox: false, problem: null, blockers: [] as string[], warnings: [] as string[], isReadyToStamp: true, paymentForm: '01',
  subTotal: DRAFT_SUBTOTAL, totalTransferred: r2(DRAFT_TOTAL - DRAFT_SUBTOTAL), total: DRAFT_TOTAL,
  transfers: [{ base: DRAFT_SUBTOTAL, tax: '002', factorType: 'Tasa', rateOrQuota: 0.16, amount: r2(DRAFT_TOTAL - DRAFT_SUBTOTAL) }],
  saleCount: SALES.length, conceptCount: SALES.length, sales: SALES,
  fingerprint: '6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b',
  leftOut: [
    { orderId: uuid('sale-ORD-000333'), orderNumber: 'ORD-000333', total: 420, reason: 'Tiene su propia factura (F-52).' },
    { orderId: uuid('sale-ORD-000336'), orderNumber: 'ORD-000336', total: 150, reason: 'Se vendió a crédito (fiado): el SAT pide reflejar las ventas en parcialidades en una factura global aparte, que TienditApp todavía no genera.' },
  ],
};

/** This month's, which has not ended: a sale already in it, and what stops it. */
const NOT_ENDED_DRAFT = {
  ...READY_DRAFT,
  periodicity: MONTHLY, periodFrom: '2026-10-01', periodTo: '2026-10-31', isReadyToStamp: false,
  blockers: ['El periodo termina el 31 de octubre de 2026: su factura global se emite a partir del día siguiente.'],
};

const draftFor = (query: URLSearchParams) => (Number(query.get('periodicity')) === MONTHLY ? NOT_ENDED_DRAFT : READY_DRAFT);

const DETAIL = {
  ...STAMPED_YESTERDAY,
  orders: SALES.map((sale) => ({ orderId: sale.orderId, orderNumber: sale.orderNumber, total: sale.total })),
};

const globalInvoiceRoutes: ExtraRoutes = (_data, helpers) => [
  ['GET', /^\/invoicing\/global-invoices$/, (_match, query) => helpers.ok(helpers.paginate(LIST, query, 20))],
  ['GET', /^\/invoicing\/global-invoices\/draft$/, (_match, query) => helpers.ok(draftFor(query))],
  ['GET', /^\/invoicing\/global-invoices\/([^/]+)$/, () => helpers.ok(DETAIL)],
];

export const FACTURA_GLOBAL_ROUTES = asOption(globalInvoiceRoutes);
