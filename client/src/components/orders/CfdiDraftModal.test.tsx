import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { CfdiCancellationMotive, OrderInvoiceStatus } from '../../types';
import CfdiDraftModal from './CfdiDraftModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

type Invoicing = Schemas['OrderInvoicingDto'];
type Invoice = Schemas['OrderInvoiceDto'];
type Draft = Schemas['CfdiDraftDto'];

const KARLA = { rfc: 'XIQB891116QE4', name: 'KARLA FUENTE NOLASCO', postalCode: '26015', fiscalRegime: '612', cfdiUse: 'G03' };

const INVOICING: Invoicing = {
  available: true, sandbox: false, canInvoice: true, problem: null, proposedReceiver: KARLA,
  customerId: 'c-1', customerName: 'Karla', invoices: [],
};

const STAMPED: Invoice = {
  id: 'inv-1', orderId: 'o-1', number: 'F-1', status: OrderInvoiceStatus.Stamped, receiver: KARLA, paymentForm: '04',
  paymentMethod: 'PUE', subTotal: 172.41, taxTotal: 27.59, total: 200, requestedAt: '2026-10-23T17:00:00Z',
  uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: '2026-10-23T17:00:05Z', hasXml: true, hasPdf: true,
  failureMessage: null, refusedAt: null, cancellationMotive: undefined, cancellationRequestedAt: null, cancelledAt: null,
};

function draft(receiver: typeof KARLA | null): Draft {
  return {
    orderId: 'o-1', orderNumber: 'ORD-000001', subTotal: 172.41, totalTransferred: 27.59, total: 200, orderTotal: 200, difference: 0,
    issuer: { rfc: 'EKU9003173C9', name: 'ESCUELA KEMPER URGATE', fiscalRegime: '601' }, issuedAtPostalCode: '06000',
    receiver: receiver ?? { rfc: 'XAXX010101000', name: 'PUBLICO EN GENERAL', fiscalRegime: '616', cfdiUse: 'S01', postalCode: '06000' },
    isGeneralPublic: receiver === null, paymentForm: '04', paymentMethod: 'PUE', currency: 'MXN', exportation: '01', voucherType: 'I',
    concepts: [], transfers: [], warnings: [], blockers: [], isReadyToStamp: true,
  } as unknown as Draft;
}

function serve(
  invoicing: Invoicing | (() => Promise<Invoicing>),
  write: (method: string, path: string, body: unknown) => Promise<unknown> = () => Promise.resolve(STAMPED),
) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options?: { query?: { rfc?: string }; body?: unknown }) => {
    if (method === 'get' && path === '/api/v1/invoicing/orders/{orderId}') {
      return typeof invoicing === 'function' ? invoicing() : Promise.resolve(invoicing);
    }
    if (method === 'get' && path === '/api/v1/orders/{id}/cfdi-draft') return Promise.resolve(draft(options?.query?.rfc ? KARLA : null));
    return write(method, path, options?.body);
  }) as unknown as typeof apiRequest);
}

function renderModal(canCancel = false, queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  return render(
    <QueryClientProvider client={queryClient}>
      <CfdiDraftModal orderId="o-1" orderNumber="ORD-000001" canCancel={canCancel} onClose={() => {}} />
    </QueryClientProvider>,
  );
}

describe('CfdiDraftModal', () => {
  beforeEach(() => vi.resetAllMocks());

  it('stamps the invoice for the proposed customer and keeps their data when asked', async () => {
    let issued: unknown;
    serve(INVOICING, (method, path, body) => {
      issued = { method, path, body };
      return Promise.resolve(STAMPED);
    });
    renderModal();

    expect(await screen.findByRole('heading', { name: 'Facturar ORD-000001' })).toBeInTheDocument();
    expect(screen.getByLabelText('RFC')).toHaveValue('XIQB891116QE4');
    const stamp = await screen.findByRole('button', { name: 'Timbrar factura' });
    await waitFor(() => expect(stamp).toBeEnabled());
    await userEvent.click(stamp);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Factura F-1 timbrada.'));
    expect(issued).toEqual({ method: 'post', path: '/api/v1/invoicing/orders/{orderId}/invoices', body: { receiver: KARLA, saveToCustomer: true } });
  });

  it('asks to review the invoice again after the data changes, before stamping it', async () => {
    serve(INVOICING);
    renderModal();
    const stamp = await screen.findByRole('button', { name: 'Timbrar factura' });
    await waitFor(() => expect(stamp).toBeEnabled());

    await userEvent.type(screen.getByLabelText('Nombre o razón social'), ' X');

    expect(stamp).toBeDisabled();
    expect(screen.getByText('Cambiaste los datos: revisa la factura con ellos antes de timbrarla.')).toBeInTheDocument();
  });

  it('waits for this opening\'s answer instead of what an earlier one left in the cache', async () => {
    // Opened before the order was paid and the customer's postal code corrected: the cache still says so.
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000 } } });
    const notPaid = 'Solo se factura una venta pagada por completo.';
    queryClient.setQueryData(['invoicing', 'orders', 'o-1'], {
      ...INVOICING, canInvoice: false, problem: notPaid, proposedReceiver: { ...KARLA, postalCode: '26000' },
    });
    serve(INVOICING);
    renderModal(false, queryClient);

    const stamp = await screen.findByRole('button', { name: 'Timbrar factura' });
    await waitFor(() => expect(stamp).toBeEnabled());
    expect(screen.getByLabelText('Código postal fiscal')).toHaveValue('26015');
    expect(screen.queryByText(notPaid)).not.toBeInTheDocument();
  });

  it('says it could not read the order\'s invoices, never that invoicing is not available, and reads them again', async () => {
    let calls = 0;
    serve(() => (++calls === 1 ? Promise.reject(new Error('sin conexión')) : Promise.resolve({ ...INVOICING, invoices: [STAMPED] })));
    renderModal();

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar las facturas de este pedido.');
    expect(screen.getByRole('heading', { name: 'Factura de ORD-000001' })).toBeInTheDocument();
    expect(screen.queryByText(/la facturación todavía no está disponible/)).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('heading', { name: 'Facturar ORD-000001' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar XML de la factura F-1' })).toBeInTheDocument();
    expect(screen.getByLabelText('RFC')).toHaveValue('XIQB891116QE4');
  });

  it('keeps the stamp pending until the order\'s invoices are read again, so it cannot be pressed twice', async () => {
    let calls = 0;
    let answer: (value: Invoicing) => void = () => {};
    serve(() => {
      calls += 1;
      if (calls === 1) return Promise.resolve(INVOICING);
      return new Promise<Invoicing>((resolve) => { answer = resolve; });
    });
    renderModal();
    const stamp = await screen.findByRole('button', { name: 'Timbrar factura' });
    await waitFor(() => expect(stamp).toBeEnabled());

    await userEvent.click(stamp);

    await waitFor(() => expect(calls).toBe(2));
    expect(screen.getByRole('button', { name: 'Timbrando…' })).toBeDisabled();
    expect(toast.success).not.toHaveBeenCalled();

    answer({ ...INVOICING, canInvoice: false, problem: 'Este pedido ya tiene la factura F-1.', invoices: [STAMPED] });

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Factura F-1 timbrada.'));
    expect(screen.queryByRole('button', { name: /Timbr/ })).not.toBeInTheDocument();
    expect(vi.mocked(apiRequest).mock.calls.filter(([method]) => method === 'post')).toHaveLength(1);
  });

  it('is only a preview while the platform does not invoice', async () => {
    serve({ ...INVOICING, available: false, canInvoice: false, proposedReceiver: undefined, customerId: undefined });
    renderModal();

    expect(await screen.findByRole('heading', { name: 'Factura de ORD-000001 (vista previa)' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Timbrar factura' })).not.toBeInTheDocument();
  });

  it('says why the order cannot be invoiced', async () => {
    serve({ ...INVOICING, canInvoice: false, problem: 'Este pedido ya tiene la factura F-1: cancélala para facturarlo otra vez.', invoices: [STAMPED] });
    renderModal();

    expect(await screen.findByText('Este pedido ya tiene la factura F-1: cancélala para facturarlo otra vez.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Timbrar factura' })).not.toBeInTheDocument();
    // Whoever manages the orders downloads it, but only owners and admins cancel it.
    expect(screen.getByRole('button', { name: 'Descargar XML de la factura F-1' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar la factura F-1' })).not.toBeInTheDocument();
  });

  it('lets owners and admins cancel a stamped invoice with its reason', async () => {
    let cancelled: unknown;
    serve({ ...INVOICING, canInvoice: false, invoices: [STAMPED] }, (method, path, body) => {
      cancelled = { method, path, body };
      return Promise.resolve({ ...STAMPED, status: OrderInvoiceStatus.Cancelling, cancellationMotive: CfdiCancellationMotive.NotCarriedOut });
    });
    renderModal(true);

    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar la factura F-1' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la factura F-1' });
    await userEvent.click(within(dialog).getByLabelText('03 · La venta no se llevó a cabo'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar factura' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cancelación de F-1 pedida: el SAT espera la respuesta del cliente.'));
    expect(cancelled).toEqual({ method: 'post', path: '/api/v1/invoicing/invoices/{invoiceId}/cancel', body: { motive: CfdiCancellationMotive.NotCarriedOut } });
  });
});
