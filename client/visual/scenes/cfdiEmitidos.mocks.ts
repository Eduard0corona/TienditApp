import { asOption, type SceneOptions } from '../fixtures';
import { uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;
type PrepareData = NonNullable<SceneOptions['prepareData']>;

// Reportes › CFDI emitidos (Fase 28H, ADR 0052) of Café La Esquina on Sunday 2026-10-04: what it stamped in October (an
// invoice in installments and its payment's complement, an invoice replaced by another one and the credit note of the new
// one, one refused by the PAC, yesterday's global invoice and today's invoice) and what they add up to. September has none.
// Since the Fase 28K (ADR 0056), the CFDI still «Timbrando» past the PAC's window (`data.cfdisToReview`): none, unless a
// scene asks for them.

// IssuedCfdiKind, OrderInvoiceStatus and GlobalInvoicePeriodicity as the contract numbers them.
const INVOICE = 0;
const GLOBAL = 1;
const COMPLEMENT = 2;
const CREDIT_NOTE = 3;
const STAMPING = 0;
const STAMPED = 1;
const REFUSED = 2;
const CANCELLED = 4;
const DAILY = 1;

const r2 = (amount: number) => Math.round(amount * 100) / 100;

function cfdi(kind: number, number: string, issuedAt: string, fields: Record<string, unknown>) {
  const total = fields.total === null ? null : Number(fields.total ?? 450);
  const subTotal = total === null ? null : r2(total / 1.16);
  return {
    id: uuid(`cfdi-${number}`), kind, number, uuid: `${number.replace('-', '')}A1F42-7C0B-4D3E-9A71-2B6C5D4E3F10`.slice(0, 36),
    status: STAMPED, issuedAt, stampedAt: issuedAt, cancellationMotive: null, cancelledAt: null, failureMessage: null,
    receiverRfc: 'XIQB891116QE4', receiverName: 'KARLA FUENTE NOLASCO', paymentMethod: kind === INVOICE ? 'PUE' : null,
    paymentForm: '04', subTotal, taxTotal: total === null || subTotal === null ? null : r2(total - subTotal), total, paidAmount: null,
    orderId: uuid(`order-${number}`), orderNumber: 'ORD-000245', relatedNumber: null, replacedByNumber: null, periodicity: null,
    periodFrom: null, periodTo: null, orderCount: null, hasXml: true,
    ...fields,
  };
}

// The newest first, as the API answers.
const OCTOBER = [
  cfdi(INVOICE, 'F-60', '2026-10-04T16:20:00Z', { total: 450 }),
  cfdi(COMPLEMENT, 'P-3', '2026-10-04T15:00:00Z', {
    total: null, paidAmount: 300, paymentForm: '03', relatedNumber: 'F-55', orderNumber: 'ORD-000231',
  }),
  cfdi(GLOBAL, 'F-59', '2026-10-04T14:05:00Z', {
    total: 1450, paymentForm: '01', paymentMethod: 'PUE', receiverRfc: 'XAXX010101000', receiverName: 'PUBLICO EN GENERAL',
    orderId: null, orderNumber: null, periodicity: DAILY, periodFrom: '2026-10-03', periodTo: '2026-10-03', orderCount: 12,
  }),
  cfdi(INVOICE, 'F-58', '2026-10-04T02:00:00Z', {
    status: REFUSED, uuid: null, stampedAt: null, hasXml: false, total: 230, orderNumber: 'ORD-000240',
    failureMessage: 'El RFC del receptor no está en la lista de RFC inscritos no cancelados del SAT.',
  }),
  cfdi(CREDIT_NOTE, 'NC-2', '2026-10-04T00:30:00Z', { total: 120, relatedNumber: 'F-57', orderNumber: 'ORD-000238' }),
  cfdi(INVOICE, 'F-57', '2026-10-03T19:00:00Z', { total: 820, relatedNumber: 'F-56', orderNumber: 'ORD-000238' }),
  cfdi(INVOICE, 'F-56', '2026-10-02T18:00:00Z', {
    total: 820, status: CANCELLED, cancellationMotive: 1, cancelledAt: '2026-10-03T19:05:00Z', replacedByNumber: 'F-57',
    orderNumber: 'ORD-000238',
  }),
  cfdi(INVOICE, 'F-55', '2026-10-01T22:00:00Z', { total: 600, paymentForm: '99', paymentMethod: 'PPD', orderNumber: 'ORD-000231' }),
];

function kind(kind: number, inForce: number, total: number, rest: Record<string, number> = {}) {
  const subTotal = kind === COMPLEMENT ? 0 : r2(total / 1.16);
  return {
    kind, inForce, subTotal, taxTotal: kind === COMPLEMENT ? 0 : r2(total - subTotal), total,
    cancelling: 0, cancelled: 0, stamping: 0, refused: 0, ...rest,
  };
}

function summaryFor(query: URLSearchParams) {
  const month = Number(query.get('month'));
  const october = month === 10;
  return {
    year: 2026, month,
    from: `2026-${String(month).padStart(2, '0')}-01`, to: october ? '2026-10-31' : '2026-09-30',
    kinds: october
      ? [kind(INVOICE, 3, 1870, { cancelled: 1, refused: 1 }), kind(GLOBAL, 1, 1450), kind(COMPLEMENT, 1, 300), kind(CREDIT_NOTE, 1, 120)]
      : [kind(INVOICE, 0, 0), kind(GLOBAL, 0, 0), kind(COMPLEMENT, 0, 0), kind(CREDIT_NOTE, 0, 0)],
  };
}

const issuedCfdiRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/invoicing\/issued-cfdis$/, (_match, query) =>
    helpers.ok(helpers.paginate(Number(query.get('month')) === 10 ? OCTOBER : [], query, 20))],
  ['GET', /^\/invoicing\/issued-cfdis\/summary$/, (_match, query) => helpers.ok(summaryFor(query))],
  ['GET', /^\/invoicing\/issued-cfdis\/to-review$/, () => helpers.ok(data.cfdisToReview ?? [])],
];

export const CFDI_EMITIDOS_ROUTES = asOption(issuedCfdiRoutes);

/**
 * Two CFDI stayed «Timbrando» past the PAC's window, the oldest first: September's invoice of an order, asked for while the
 * PAC did not answer, and a payment complement of two days later. The SAT already shows whatever was stamped for the
 * invoice («No se timbró» is offered); for the complement, it says from when.
 */
const porRevisar: PrepareData = (data) => {
  data.cfdisToReview = [
    cfdi(INVOICE, 'F-52', '2026-09-28T17:40:00Z', {
      status: STAMPING, uuid: null, stampedAt: null, hasXml: false, total: 980, orderNumber: 'ORD-000219',
      receiverRfc: 'GAL1203157T2', receiverName: 'GRUPO ALTAVISTA',
      stampWindowEndsAt: '2026-10-01T16:40:00Z', notStampedFrom: '2026-10-04T16:40:00Z',
    }),
    cfdi(COMPLEMENT, 'P-2', '2026-09-30T16:10:00Z', {
      status: STAMPING, uuid: null, stampedAt: null, hasXml: false, total: null, paidAmount: 250, paymentForm: '03',
      relatedNumber: 'F-49', orderNumber: 'ORD-000204',
      stampWindowEndsAt: '2026-10-03T15:10:00Z', notStampedFrom: '2026-10-06T15:10:00Z',
    }),
  ];
};

export const CFDI_POR_REVISAR = asOption(porRevisar);
