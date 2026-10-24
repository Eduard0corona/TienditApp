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
  customerId: 'c-1', customerName: 'Karla', invoices: [], payments: [],
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

function renderModal(
  canCancel = false,
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  canSaveToCustomer = true,
  orderCancelled = false,
) {
  return render(
    <QueryClientProvider client={queryClient}>
      <CfdiDraftModal
        orderId="o-1"
        orderNumber="ORD-000001"
        canCancel={canCancel}
        canSaveToCustomer={canSaveToCustomer}
        orderCancelled={orderCancelled}
        onClose={() => {}}
      />
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

  it('stamps without keeping the fiscal data for whoever does not manage customers', async () => {
    let issued: unknown;
    serve(INVOICING, (_method, _path, body) => {
      issued = body;
      return Promise.resolve(STAMPED);
    });
    renderModal(false, undefined, false);

    const stamp = await screen.findByRole('button', { name: 'Timbrar factura' });
    expect(screen.queryByLabelText(/Guardar estos datos en/)).not.toBeInTheDocument();
    await waitFor(() => expect(stamp).toBeEnabled());
    await userEvent.click(stamp);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Factura F-1 timbrada.'));
    expect(issued).toEqual({ receiver: KARLA, saveToCustomer: false });
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

  it('keeps the invoices and what the person typed when a read in the background fails', async () => {
    let calls = 0;
    const stamping: Invoice = { ...STAMPED, status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false };
    serve(() => (++calls === 1 ? Promise.resolve({ ...INVOICING, invoices: [stamping] }) : Promise.reject(new Error('sin conexión'))));
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    renderModal(false, queryClient);
    await screen.findByRole('heading', { name: 'Facturar ORD-000001' });
    await userEvent.type(screen.getByLabelText('Nombre o razón social'), ' X');

    // While F-1 waits for its stamp the invoices are read every 30 s; one of those reads fails.
    await queryClient.invalidateQueries({ queryKey: ['invoicing', 'orders', 'o-1'] });

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron leer de nuevo las facturas de este pedido');
    expect(screen.getByRole('heading', { name: 'Facturar ORD-000001' })).toBeInTheDocument();
    expect(screen.getByText('F-1')).toBeInTheDocument();
    expect(screen.getByLabelText('Nombre o razón social')).toHaveValue('KARLA FUENTE NOLASCO X');
  });

  it('does not show what an earlier opening left in the cache when this opening\'s read fails', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const notPaid = 'Solo se factura una venta pagada por completo.';
    queryClient.setQueryData(['invoicing', 'orders', 'o-1'], { ...INVOICING, canInvoice: false, problem: notPaid, invoices: [STAMPED] });
    serve(() => Promise.reject(new Error('sin conexión')));
    renderModal(false, queryClient);

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar las facturas de este pedido.');
    expect(screen.getByRole('heading', { name: 'Factura de ORD-000001' })).toBeInTheDocument();
    expect(screen.queryByText(notPaid)).not.toBeInTheDocument();
    expect(screen.queryByText('F-1')).not.toBeInTheDocument();
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

  it('keeps the invoices of a cancelled order, to download them or cancel a stamped one, without a preview', async () => {
    serve({ ...INVOICING, canInvoice: false, problem: 'Una orden cancelada no se factura.', invoices: [STAMPED] });
    renderModal(true, undefined, true, true);

    expect(await screen.findByRole('heading', { name: 'Facturas de ORD-000001' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Descargar XML de la factura F-1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar factura F-1' })).toBeInTheDocument();
    expect(screen.getByText(/si una de sus facturas sigue timbrada, cancélala ante el SAT con el motivo 03/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Timbrar factura' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Facturar a un cliente con RFC' })).not.toBeInTheDocument();
    expect(vi.mocked(apiRequest).mock.calls.some(([, path]) => path === '/api/v1/orders/{id}/cfdi-draft')).toBe(false);
  });

  it('says a cancelled order has no invoices when it has none', async () => {
    serve({ ...INVOICING, canInvoice: false, problem: 'Una orden cancelada no se factura.', invoices: [] });
    renderModal(false, undefined, true, true);

    expect(await screen.findByText('Este pedido está cancelado y no tiene facturas.')).toBeInTheDocument();
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
    expect(screen.queryByRole('button', { name: 'Cancelar factura F-1' })).not.toBeInTheDocument();
  });

  it('shows the payment complements of an invoice in installments under it', async () => {
    serve({
      ...INVOICING,
      canInvoice: false,
      problem: 'Este pedido ya tiene la factura F-1.',
      invoices: [{ ...STAMPED, paymentForm: '99', paymentMethod: 'PPD' }],
      payments: [{
        orderPaymentId: 'pay-1', paidAt: '2026-10-04T17:00:00Z', method: 0, amount: 50, dueBy: '2999-11-05', canIssue: true,
        problem: null, awaitsAutomaticIssue: true, complements: [],
      }],
    });
    renderModal();

    expect(await screen.findByRole('heading', { name: 'Complementos de pago' })).toBeInTheDocument();
    expect(screen.getByText('F-1', { exact: true }).closest('li')).toHaveTextContent('en parcialidades (PPD)');
    expect(screen.getByRole('button', { name: /^Emitir ahora el complemento del cobro del/ })).toBeInTheDocument();
  });

  it('lets owners and admins cancel a stamped invoice with its reason', async () => {
    let cancelled: unknown;
    serve({ ...INVOICING, canInvoice: false, invoices: [STAMPED] }, (method, path, body) => {
      cancelled = { method, path, body };
      return Promise.resolve({ ...STAMPED, status: OrderInvoiceStatus.Cancelling, cancellationMotive: CfdiCancellationMotive.NotCarriedOut });
    });
    renderModal(true);

    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar factura F-1' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la factura F-1' });
    await userEvent.click(within(dialog).getByLabelText('03 · La venta no se llevó a cabo'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar factura' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cancelación de F-1 pedida: el SAT espera la respuesta del cliente.'));
    expect(cancelled).toEqual({ method: 'post', path: '/api/v1/invoicing/invoices/{invoiceId}/cancel', body: { motive: CfdiCancellationMotive.NotCarriedOut } });
  });
});
