import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import api from './axios';
import { apiRequest, type Schemas } from './typedClient';
import {
  CfdiCancellationMotive,
  CfdiEmailDocument,
  CreditNoteAdjustment,
  GlobalInvoicePeriodicity,
  IssuedCfdiExportFormat,
  IssuedCfdiKind,
  OrderInvoiceStatus,
  OrderStatus,
  type OrderDto,
} from '../types';
import { isDeliveryPlatform } from '../utils/deliveryPlatforms';

// Invoicing (Fase 28A, ADR 0045): Configuración › Facturación and the invoice of an order; the global invoice of a period
// (Fase 28B, ADR 0046) in Reportes › Factura global; the payment complements of an invoice in installments (Fase 28C,
// ADR 0047); the credit notes of an invoiced order's refunds and write-offs (Fase 28D, ADR 0048); and the emails of those
// CFDI to the customer (Fase 28E, ADR 0049); self-invoicing, turned on here and printed on each ticket (Fase 28F,
// ADR 0050); and the CFDI issued in a month, with their XML for the accountant (Fase 28H, ADR 0052), in Reportes › CFDI
// emitidos. Kept out of queries.ts, which the first visit loads.

export type InvoicingSettings = Schemas['InvoicingSettingsDto'];
export type OrderInvoicing = Schemas['OrderInvoicingDto'];
export type OrderInvoice = Schemas['OrderInvoiceDto'];
export type OrderPaymentComplements = Schemas['OrderPaymentComplementsDto'];
export type PaymentComplement = Schemas['PaymentComplementDto'];
export type OrderAdjustment = Schemas['OrderAdjustmentDto'];
export type CreditNote = Schemas['CreditNoteDto'];
export type CfdiEmail = Schemas['CfdiEmailDto'];
export type GlobalInvoice = Schemas['GlobalInvoiceDto'];
export type GlobalInvoiceDetail = Schemas['GlobalInvoiceDetailDto'];
export type GlobalInvoiceDraft = Schemas['GlobalInvoiceDraftDto'];
export type OrderSelfInvoice = Schemas['OrderSelfInvoiceDto'];
export type IssuedCfdi = Schemas['IssuedCfdiDto'];
export type IssuedCfdiSummary = Schemas['IssuedCfdiSummaryDto'];

export const invoicingSettingsKey = ['invoicing', 'settings'] as const;
export const orderInvoicingKey = (orderId: string) => ['invoicing', 'orders', orderId] as const;
const cfdiDraftKey = (orderId: string, receiver?: CfdiReceiver) => ['orders', orderId, 'cfdi-draft', receiver ?? null] as const;
/** Everything of the global invoices (list, previews and each one): a change reads it all again. */
const globalInvoicesKey = ['invoicing', 'global-invoices'] as const;
const issuedCfdisKey = ['invoicing', 'issued-cfdis'] as const;

/** Fiscal data of the customer an invoice is for (CFDI 4.0 receptor). */
export interface CfdiReceiver {
  rfc: string;
  name: string;
  postalCode: string;
  fiscalRegime: string;
  cfdiUse: string;
}

/** While an invoice waits for the PAC or the SAT, it is asked again. */
export const PENDING_REFRESH_MS = 30_000;

/** Whether the platform invoices, the business's fiscal data, its CSD and serie (owners and admins). */
export function useInvoicingSettings() {
  return useQuery({
    queryKey: invoicingSettingsKey,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/settings', { signal }),
  });
}

/** Each change answers the settings as they are now. With `forget`, what it sent leaves the cache as soon as it ends. */
function useSettingsChange<TVariables>(change: (variables: TVariables) => Promise<InvoicingSettings>, forget = false) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: change,
    gcTime: forget ? 0 : undefined,
    onSuccess: (settings) => {
      client.setQueryData(invoicingSettingsKey, settings);
      void client.invalidateQueries({ queryKey: ['invoicing', 'orders'] });
    },
    onError: () => client.invalidateQueries({ queryKey: invoicingSettingsKey }),
  });
}

/**
 * The .cer and .key go in base64 with the key's password; TienditApp checks them and the PAC keeps them. The page resets
 * the mutation when it ends, so neither the key nor its password stays in memory.
 */
export function useUploadInvoicingCertificate() {
  return useSettingsChange((body: { certificate: string; privateKey: string; password: string }) =>
    apiRequest('put', '/api/v1/invoicing/settings/certificate', { body }), true);
}

export function useRemoveInvoicingCertificate() {
  return useSettingsChange(() => apiRequest('delete', '/api/v1/invoicing/settings/certificate'));
}

/** The serie of the invoices, the one of the payment complements and the one of the credit notes: each keeps its folios. */
export function useChangeInvoiceSerie() {
  return useSettingsChange((body: { serie: string; paymentSerie: string; creditNoteSerie: string }) =>
    apiRequest('put', '/api/v1/invoicing/settings/serie', { body }));
}

/** Self-invoicing on (only while the business can invoice) or off; off keeps the codes the tickets already printed. */
export function useSetSelfInvoicing() {
  return useSettingsChange((on: boolean) => apiRequest('put', '/api/v1/invoicing/settings/self-invoicing', { body: { on } }));
}

/**
 * Whether the order's ticket may carry «Factura en línea»: paid in full (nothing owed or written off), with an amount, not
 * cancelled and not of a delivery app. The API decides the same (and whether self-invoicing is on); this spares asking it
 * for every other ticket.
 */
export function mayPrintSelfInvoice(order: OrderDto): boolean {
  return order.status !== OrderStatus.Cancelled && order.total > 0 && order.total - order.paidAmount <= 0
    && order.payments.length > 0 && !isDeliveryPlatform(order.channel);
}

/**
 * What the order's ticket prints for self-invoicing (its folio, code and until when), or null while it is off or for a sale
 * that is not invoiced online (the API answers nothing, 204). A ticket printed without it is still a ticket: an error is
 * null too. `preparing` while it is asked the first time: printing then would leave it out of the paper.
 */
export function useOrderSelfInvoice(order: OrderDto | null | undefined, enabled = true) {
  const orderId = order?.id ?? '';
  const query = useQuery({
    queryKey: ['invoicing', 'orders', orderId, 'self-invoice'] as const,
    queryFn: async ({ signal }): Promise<OrderSelfInvoice | null> =>
      (await apiRequest('get', '/api/v1/invoicing/orders/{orderId}/self-invoice', { path: { orderId }, signal })) || null,
    enabled: enabled && !!order && mayPrintSelfInvoice(order),
    retry: false,
    staleTime: 5 * 60_000,
  });
  return { selfInvoice: query.data ?? null, preparing: query.isLoading };
}

/**
 * Whether the order can be invoiced, whom to, its invoices, the complements of its payments and the credit notes of its
 * refunds and write-offs: read again each time the invoice opens (the order may have been paid, or the customer's fiscal
 * data changed) and while one of them waits for the PAC or the SAT, or for what TienditApp issues on its own.
 */
export function useOrderInvoicing(orderId: string) {
  return useQuery({
    queryKey: orderInvoicingKey(orderId),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/orders/{orderId}', { path: { orderId }, signal }),
    refetchOnMount: 'always',
    refetchInterval: (query) => (query.state.data && isOrderInvoicingWaiting(query.state.data) ? PENDING_REFRESH_MS : false),
  });
}

/**
 * Whether something of the order's invoicing changes on its own soon: an invoice, a complement or a credit note waits for
 * the PAC or the SAT, or for its email to go out, a replaced invoice waits for TienditApp to ask the SAT to cancel it with
 * motive 01 (Fase 28G, as the API says it), or a payment, a refund or a write-off waits for what TienditApp issues in its next
 * pass.
 */
export function isOrderInvoicingWaiting(data: Pick<OrderInvoicing, 'invoices' | 'payments' | 'adjustments'>): boolean {
  const waits = (cfdi: Pick<OrderInvoice, 'status' | 'emails'>) => isWaiting(cfdi) || isEmailOnItsWay(cfdi.emails[0]);
  return data.invoices.some((invoice) => waits(invoice) || invoice.awaitsReplacedCancellation)
    || data.payments.some((payment) => payment.awaitsAutomaticIssue || payment.complements.some(waits))
    || data.adjustments.some((adjustment) => adjustment.awaitsAutomaticIssue || adjustment.creditNotes.some(waits));
}

/**
 * The CFDI's latest email is neither sent nor ended yet: the queue keeps trying it, and ends it as not delivered after its
 * last attempt.
 */
export function isEmailOnItsWay(email: Pick<CfdiEmail, 'sentAt' | 'failedAt'> | undefined): boolean {
  return !!email && !email.sentAt && !email.failedAt;
}

/** Preview of the invoice of an order, read again each time it opens; without a receiver it goes to the general public. */
/** The CFDI the order would stamp; never read for a cancelled order, which is not invoiced. */
export function useCfdiDraft(orderId: string, receiver?: CfdiReceiver, enabled = true) {
  return useQuery({
    queryKey: cfdiDraftKey(orderId, receiver),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/orders/{id}/cfdi-draft', { path: { id: orderId }, query: receiver, signal }),
    refetchOnMount: 'always',
    retry: false,
    enabled,
  });
}

/** «Timbrando» waits for the PAC; «Cancelándose», for the SAT. */
export function isWaiting(invoice: Pick<OrderInvoice, 'status'>): boolean {
  return invoice.status === OrderInvoiceStatus.Stamping || invoice.status === OrderInvoiceStatus.Cancelling;
}

/**
 * Each change answers the invoice as it ended up; the order's invoicing and its draft are read again, and the change stays
 * pending until they are, so what it changed (a live invoice) is on screen before anything else can be pressed.
 */
function useInvoiceChange<TVariables, TResult = OrderInvoice>(orderId: string, change: (variables: TVariables) => Promise<TResult>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: change,
    onSettled: () => Promise.all([
      client.invalidateQueries({ queryKey: orderInvoicingKey(orderId) }),
      client.invalidateQueries({ queryKey: ['orders', orderId, 'cfdi-draft'] }),
    ]),
  });
}

/** With an email, the customer gets its XML and PDF once it is stamped, and those of its complements and credit notes. */
export function useIssueOrderInvoice(orderId: string) {
  return useInvoiceChange(orderId, (body: { receiver: CfdiReceiver; saveToCustomer: boolean; receiverEmail?: string | null }) =>
    apiRequest('post', '/api/v1/invoicing/orders/{orderId}/invoices', { path: { orderId }, body }));
}

export function useRetryOrderInvoice(orderId: string) {
  return useInvoiceChange(orderId, (invoiceId: string) =>
    apiRequest('post', '/api/v1/invoicing/invoices/{invoiceId}/retry', { path: { invoiceId } }));
}

export function useCancelOrderInvoice(orderId: string) {
  return useInvoiceChange(orderId, ({ invoiceId, motive }: { invoiceId: string; motive: CfdiCancellationMotive }) =>
    apiRequest('post', '/api/v1/invoicing/invoices/{invoiceId}/cancel', { path: { invoiceId }, body: { motive } }));
}

/**
 * «Sustituir factura» (Fase 28G, ADR 0051): a new invoice of the order with the data written now, related to the stamped
 * one with errors (04). Answers the new one; once it is stamped, the old one is cancelled with motive 01.
 */
export function useSubstituteOrderInvoice(orderId: string) {
  return useInvoiceChange(orderId, ({ invoiceId, ...body }: {
    invoiceId: string;
    receiver: CfdiReceiver;
    saveToCustomer: boolean;
    receiverEmail?: string | null;
  }) => apiRequest('post', '/api/v1/invoicing/invoices/{invoiceId}/substitute', { path: { invoiceId }, body }));
}

/** «Cancelar con relación»: asks the SAT again to cancel with motive 01 an invoice whose replacement is stamped. */
export function useCancelReplacedOrderInvoice(orderId: string) {
  return useInvoiceChange(orderId, (invoiceId: string) =>
    apiRequest('post', '/api/v1/invoicing/invoices/{invoiceId}/cancel-replaced', { path: { invoiceId } }));
}

/**
 * «Emitir ahora» (before TienditApp issues it on its own) or «Emitir otra vez» (after a refused or cancelled one): the
 * complement of a payment of an order whose invoice is in installments.
 */
export function useIssuePaymentComplement(orderId: string) {
  return useInvoiceChange(orderId, (paymentId: string) =>
    apiRequest('post', '/api/v1/invoicing/payments/{paymentId}/complements', { path: { paymentId } }));
}

export function useRetryPaymentComplement(orderId: string) {
  return useInvoiceChange(orderId, (complementId: string) =>
    apiRequest('post', '/api/v1/invoicing/payment-complements/{complementId}/retry', { path: { complementId } }));
}

export function useCancelPaymentComplement(orderId: string) {
  return useInvoiceChange(orderId, ({ complementId, motive }: { complementId: string; motive: CfdiCancellationMotive }) =>
    apiRequest('post', '/api/v1/invoicing/payment-complements/{complementId}/cancel', { path: { complementId }, body: { motive } }));
}

/**
 * Issues the credit note of a refund or of a write-off of an order whose invoice is stamped: before TienditApp does it on
 * its own, or again after a refused or cancelled one.
 */
export function useIssueCreditNote(orderId: string) {
  return useInvoiceChange(orderId, ({ kind, adjustmentId }: { kind: CreditNoteAdjustment; adjustmentId: string }) =>
    kind === CreditNoteAdjustment.Refund
      ? apiRequest('post', '/api/v1/invoicing/refunds/{refundId}/credit-notes', { path: { refundId: adjustmentId } })
      : apiRequest('post', '/api/v1/invoicing/write-offs/{writeOffId}/credit-notes', { path: { writeOffId: adjustmentId } }));
}

export function useRetryCreditNote(orderId: string) {
  return useInvoiceChange(orderId, (creditNoteId: string) =>
    apiRequest('post', '/api/v1/invoicing/credit-notes/{creditNoteId}/retry', { path: { creditNoteId } }));
}

export function useCancelCreditNote(orderId: string) {
  return useInvoiceChange(orderId, ({ creditNoteId, motive }: { creditNoteId: string; motive: CfdiCancellationMotive }) =>
    apiRequest('post', '/api/v1/invoicing/credit-notes/{creditNoteId}/cancel', { path: { creditNoteId }, body: { motive } }));
}

/**
 * «Enviar por correo»: the XML and PDF of a stamped invoice, payment complement or credit note go to the customer again,
 * to its email or another one; with `useForInvoice`, what comes next of its invoice goes there too. It goes out from the
 * queue: the order's invoicing shows how it ended.
 */
export function useSendCfdiByEmail(orderId: string) {
  return useInvoiceChange(orderId, ({ document, cfdiId, email, useForInvoice }: {
    document: CfdiEmailDocument;
    cfdiId: string;
    email: string;
    useForInvoice: boolean;
  }) => {
    const body = { email, useForInvoice };
    switch (document) {
      case CfdiEmailDocument.PaymentComplement:
        return apiRequest('post', '/api/v1/invoicing/payment-complements/{complementId}/email', { path: { complementId: cfdiId }, body });
      case CfdiEmailDocument.CreditNote:
        return apiRequest('post', '/api/v1/invoicing/credit-notes/{creditNoteId}/email', { path: { creditNoteId: cfdiId }, body });
      default:
        return apiRequest('post', '/api/v1/invoicing/invoices/{invoiceId}/email', { path: { invoiceId: cfdiId }, body });
    }
  });
}

/** What Reportes › CFDI emitidos narrows the month to; empty, all of them. */
export interface IssuedCfdiFilters {
  kind?: IssuedCfdiKind;
  status?: OrderInvoiceStatus;
  search: string;
}

/**
 * The CFDI the business issued in a month, the newest first; while one is being stamped, it is asked again. Another page of
 * the same list keeps the one on screen while it arrives; another month, kind, state or search does not (it would show
 * under the new title what the old one had).
 */
export function useIssuedCfdis(year: number, month: number, filters: IssuedCfdiFilters, page: number, pageSize: number) {
  const listing = [...issuedCfdisKey, 'list', year, month, filters.kind ?? null, filters.status ?? null, filters.search] as const;
  return useQuery({
    queryKey: [...listing, page, pageSize],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/issued-cfdis', {
      query: {
        year,
        month,
        kind: filters.kind,
        status: filters.status,
        search: filters.search || undefined,
        pageNumber: page,
        pageSize,
      },
      signal,
    }),
    placeholderData: (previous, previousQuery) =>
      previousQuery && listing.every((part, index) => previousQuery.queryKey[index] === part) ? previous : undefined,
    // A CFDI is stamped, cancelled or replaced from its order or its global invoice: coming back reads the month again.
    refetchOnMount: 'always',
    refetchInterval: (query) => query.state.data?.items.some((cfdi) => cfdi.status === OrderInvoiceStatus.Stamping)
      ? PENDING_REFRESH_MS
      : false,
  });
}

/** What the CFDI of a month add up to, by kind; while one is being stamped, it is asked again, as the list. */
export function useIssuedCfdiSummary(year: number, month: number) {
  return useQuery({
    queryKey: [...issuedCfdisKey, 'summary', year, month],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/issued-cfdis/summary', { query: { year, month }, signal }),
    refetchOnMount: 'always',
    refetchInterval: (query) => query.state.data?.kinds.some((kind) => kind.stamping > 0) ? PENDING_REFRESH_MS : false,
  });
}

/** The month's CFDI for the accountant: a ZIP with their XML and the list, or the list alone (CSV). */
export function downloadIssuedCfdis(year: number, month: number, format: IssuedCfdiExportFormat): Promise<{ blob: Blob; fileName: string }> {
  const name = `CFDI_${year}-${String(month).padStart(2, '0')}`;
  return downloadNamed(
    '/invoicing/issued-cfdis/export',
    `${name}.${format === IssuedCfdiExportFormat.Csv ? 'csv' : 'zip'}`,
    { year, month, format },
  );
}

/** The stamped XML or the PDF of a row of Reportes › CFDI emitidos, from where its kind keeps it. */
export function downloadIssuedCfdiFile(cfdi: IssuedCfdi, kind: 'xml' | 'pdf'): Promise<{ blob: Blob; fileName: string }> {
  switch (cfdi.kind) {
    case IssuedCfdiKind.GlobalInvoice:
      return downloadGlobalInvoiceFile(cfdi.id, kind);
    case IssuedCfdiKind.PaymentComplement:
      return downloadPaymentComplementFile(cfdi.id, kind);
    case IssuedCfdiKind.CreditNote:
      return downloadCreditNoteFile(cfdi.id, kind);
    default:
      return downloadInvoiceFile(cfdi.id, kind);
  }
}

/** The stamped XML or the PDF of an invoice, fetched with the session. */
export function downloadInvoiceFile(invoiceId: string, kind: 'xml' | 'pdf'): Promise<{ blob: Blob; fileName: string }> {
  return downloadFile(`/invoicing/invoices/${encodeURIComponent(invoiceId)}/${kind}`, kind);
}

/** The stamped XML or the PDF of a payment complement, fetched with the session. */
export function downloadPaymentComplementFile(complementId: string, kind: 'xml' | 'pdf'): Promise<{ blob: Blob; fileName: string }> {
  return downloadFile(`/invoicing/payment-complements/${encodeURIComponent(complementId)}/${kind}`, kind, 'complemento');
}

/** The stamped XML or the PDF of a credit note, fetched with the session. */
export function downloadCreditNoteFile(creditNoteId: string, kind: 'xml' | 'pdf'): Promise<{ blob: Blob; fileName: string }> {
  return downloadFile(`/invoicing/credit-notes/${encodeURIComponent(creditNoteId)}/${kind}`, kind, 'nota-de-credito');
}

/** The stamped XML or the PDF of a global invoice, fetched with the session. */
export function downloadGlobalInvoiceFile(invoiceId: string, kind: 'xml' | 'pdf'): Promise<{ blob: Blob; fileName: string }> {
  return downloadFile(`/invoicing/global-invoices/${encodeURIComponent(invoiceId)}/${kind}`, kind);
}

function downloadFile(path: string, kind: 'xml' | 'pdf', fallbackName = 'factura'): Promise<{ blob: Blob; fileName: string }> {
  return downloadNamed(path, `${fallbackName}.${kind}`);
}

async function downloadNamed(
  path: string, fallbackFileName: string, params?: Record<string, string | number>,
): Promise<{ blob: Blob; fileName: string }> {
  let response;
  try {
    response = await api.get<Blob>(path, params ? { responseType: 'blob', params } : { responseType: 'blob' });
  } catch (err: unknown) {
    // The refusal comes as a blob too: read back, its message (the PDF not available, say) reaches the person.
    if (isAxiosError(err) && err.response?.data instanceof Blob) {
      try {
        err.response.data = JSON.parse(await err.response.data.text()) as unknown;
      } catch {
        // Not JSON: the generic message stays.
      }
    }
    throw err;
  }
  const disposition = String(response.headers['content-disposition'] ?? '');
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  return { blob: response.data, fileName: match ? decodeURIComponent(match[1]) : fallbackFileName };
}

/** The customer's fiscal data and the email for their invoices, for their next invoices; null forgets both. */
export function useUpdateCustomerFiscalData(customerId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ fiscalData, invoiceEmail }: { fiscalData: CfdiReceiver | null; invoiceEmail: string }) =>
      // Without fiscalData the API forgets it, with its email; an empty email forgets the email alone.
      apiRequest('put', '/api/v1/customers/{id}/fiscal-data', {
        path: { id: customerId },
        body: fiscalData ? { fiscalData, invoiceEmail } : {},
      }),
    // The invoice of their orders proposes it.
    onSuccess: () => Promise.all([
      client.invalidateQueries({ queryKey: ['customers'] }),
      client.invalidateQueries({ queryKey: ['invoicing', 'orders'] }),
    ]),
  });
}

/**
 * The business's global invoices, the newest period first, found by folio, UUID or the folio of a sale they cover; read
 * again while one waits for the PAC or the SAT.
 */
export function useGlobalInvoices(page: number, pageSize: number, search: string) {
  return useQuery({
    queryKey: [...globalInvoicesKey, 'list', page, pageSize, search],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/global-invoices', {
      query: { pageNumber: page, pageSize, search: search || undefined },
      signal,
    }),
    placeholderData: (previous) => previous,
    refetchInterval: (query) => query.state.data?.items.some((invoice) => isWaiting(invoice)) ? PENDING_REFRESH_MS : false,
  });
}

/**
 * The global invoice of the period of that periodicity that holds the day, before it is stamped: read again each time the
 * screen opens, since sales are paid and invoiced meanwhile.
 */
export function useGlobalInvoiceDraft(periodicity: GlobalInvoicePeriodicity, day: string, enabled = true) {
  return useQuery({
    queryKey: [...globalInvoicesKey, 'draft', periodicity, day],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/global-invoices/draft', { query: { periodicity, day }, signal }),
    refetchOnMount: 'always',
    retry: false,
    enabled,
  });
}

/** A global invoice with its sales, read again while it waits for the PAC or the SAT. */
export function useGlobalInvoice(invoiceId: string) {
  return useQuery({
    queryKey: [...globalInvoicesKey, invoiceId],
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/global-invoices/{invoiceId}', { path: { invoiceId }, signal }),
    refetchInterval: (query) => query.state.data && isWaiting(query.state.data) ? PENDING_REFRESH_MS : false,
  });
}

/**
 * Each change answers the global invoice as it ended up. The global invoices are read again, and so is the invoicing of the
 * orders: a sale in a live global invoice is not invoiced alone.
 */
function useGlobalInvoiceChange<TVariables>(change: (variables: TVariables) => Promise<GlobalInvoice>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: change,
    onSettled: () => Promise.all([
      client.invalidateQueries({ queryKey: globalInvoicesKey }),
      client.invalidateQueries({ queryKey: ['invoicing', 'orders'] }),
    ]),
  });
}

/**
 * Stamps the global invoice of a period as the preview showed it: its number of sales, total and fingerprint (which sales,
 * for how much). If the period's sales changed since, the API refuses it.
 */
export function useIssueGlobalInvoice() {
  return useGlobalInvoiceChange((body: {
    periodicity: GlobalInvoicePeriodicity;
    day: string;
    expectedSaleCount: number;
    expectedTotal: number;
    expectedFingerprint: string;
  }) => apiRequest('post', '/api/v1/invoicing/global-invoices', { body }));
}

export function useRetryGlobalInvoice() {
  return useGlobalInvoiceChange((invoiceId: string) =>
    apiRequest('post', '/api/v1/invoicing/global-invoices/{invoiceId}/retry', { path: { invoiceId } }));
}

export function useCancelGlobalInvoice() {
  return useGlobalInvoiceChange(({ invoiceId, motive }: { invoiceId: string; motive: CfdiCancellationMotive }) =>
    apiRequest('post', '/api/v1/invoicing/global-invoices/{invoiceId}/cancel', { path: { invoiceId }, body: { motive } }));
}
