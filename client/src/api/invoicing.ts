import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import api from './axios';
import { apiRequest, type Schemas } from './typedClient';
import type { CfdiReceiver } from './queries';
import { CfdiCancellationMotive, OrderInvoiceStatus } from '../types';

// Invoicing (Fase 28A, ADR 0045): Configuración › Facturación and the invoice of an order. Kept out of queries.ts, which the
// first visit loads.

export type InvoicingSettings = Schemas['InvoicingSettingsDto'];
export type OrderInvoicing = Schemas['OrderInvoicingDto'];
export type OrderInvoice = Schemas['OrderInvoiceDto'];

export const invoicingSettingsKey = ['invoicing', 'settings'] as const;
export const orderInvoicingKey = (orderId: string) => ['invoicing', 'orders', orderId] as const;

/** While an invoice waits for the PAC or the SAT, it is asked again. */
export const PENDING_REFRESH_MS = 30_000;

/** Whether the platform invoices, the business's fiscal data, its CSD and serie (owners and admins). */
export function useInvoicingSettings() {
  return useQuery({
    queryKey: invoicingSettingsKey,
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/settings', { signal }),
  });
}

/** Each change answers the settings as they are now. */
function useSettingsChange<TVariables>(change: (variables: TVariables) => Promise<InvoicingSettings>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: change,
    onSuccess: (settings) => {
      client.setQueryData(invoicingSettingsKey, settings);
      void client.invalidateQueries({ queryKey: ['invoicing', 'orders'] });
    },
    onError: () => client.invalidateQueries({ queryKey: invoicingSettingsKey }),
  });
}

/** The .cer and .key go in base64 with the key's password; TienditApp checks them and the PAC keeps them. */
export function useUploadInvoicingCertificate() {
  return useSettingsChange((body: { certificate: string; privateKey: string; password: string }) =>
    apiRequest('put', '/api/v1/invoicing/settings/certificate', { body }));
}

export function useRemoveInvoicingCertificate() {
  return useSettingsChange(() => apiRequest('delete', '/api/v1/invoicing/settings/certificate'));
}

export function useChangeInvoiceSerie() {
  return useSettingsChange((serie: string) => apiRequest('put', '/api/v1/invoicing/settings/serie', { body: { serie } }));
}

/** Whether the order can be invoiced, whom to, and its invoices; asked again while one waits for the PAC or the SAT. */
export function useOrderInvoicing(orderId: string) {
  return useQuery({
    queryKey: orderInvoicingKey(orderId),
    queryFn: ({ signal }) => apiRequest('get', '/api/v1/invoicing/orders/{orderId}', { path: { orderId }, signal }),
    refetchInterval: (query) =>
      query.state.data?.invoices.some((invoice) => isWaiting(invoice)) ? PENDING_REFRESH_MS : false,
  });
}

/** «Timbrando» waits for the PAC; «Cancelándose», for the SAT. */
export function isWaiting(invoice: Pick<OrderInvoice, 'status'>): boolean {
  return invoice.status === OrderInvoiceStatus.Stamping || invoice.status === OrderInvoiceStatus.Cancelling;
}

/** Each change answers the invoice as it ended up; the order's invoicing and its draft are read again. */
function useInvoiceChange<TVariables>(orderId: string, change: (variables: TVariables) => Promise<OrderInvoice>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: change,
    onSettled: () => {
      void client.invalidateQueries({ queryKey: orderInvoicingKey(orderId) });
      void client.invalidateQueries({ queryKey: ['orders', orderId, 'cfdi-draft'] });
    },
  });
}

export function useIssueOrderInvoice(orderId: string) {
  return useInvoiceChange(orderId, (body: { receiver: CfdiReceiver; saveToCustomer: boolean }) =>
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

/** The stamped XML or the PDF of an invoice, fetched with the session. */
export async function downloadInvoiceFile(invoiceId: string, kind: 'xml' | 'pdf'): Promise<{ blob: Blob; fileName: string }> {
  const response = await api.get<Blob>(`/invoicing/invoices/${encodeURIComponent(invoiceId)}/${kind}`, { responseType: 'blob' });
  const disposition = String(response.headers['content-disposition'] ?? '');
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  return { blob: response.data, fileName: match ? decodeURIComponent(match[1]) : `factura.${kind}` };
}

/** The customer's fiscal data, for their next invoices; null forgets it. */
export function useUpdateCustomerFiscalData(customerId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (fiscalData: CfdiReceiver | null) =>
      // Without fiscalData the API forgets it.
      apiRequest('put', '/api/v1/customers/{id}/fiscal-data', { path: { id: customerId }, body: fiscalData ? { fiscalData } : {} }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['customers'] }),
  });
}
