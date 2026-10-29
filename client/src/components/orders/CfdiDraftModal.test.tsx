import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { CfdiCancellationMotive, CreditNoteAdjustment, OrderInvoiceStatus } from '../../types';
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
  customerId: 'c-1', customerName: 'Karla', invoices: [], payments: [], adjustments: [],
};

const STAMPED: Invoice = {
  id: 'inv-1', orderId: 'o-1', number: 'F-1', status: OrderInvoiceStatus.Stamped, receiver: KARLA, paymentForm: '04',
  paymentMethod: 'PUE', subTotal: 172.41, taxTotal: 27.59, total: 200, requestedAt: '2026-10-23T17:00:00Z',
  uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: '2026-10-23T17:00:05Z', hasXml: true, hasPdf: true,
  failureMessage: null, refusedAt: null, cancellationMotive: undefined, cancellationRequestedAt: null, cancelledAt: null, emails: [], requestedByCustomer: false,
  canSubstitute: false,
  awaitsReplacedCancellation: false,
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
    expect(issued).toEqual({
      method: 'post', path: '/api/v1/invoicing/orders/{orderId}/invoices', body: { receiver: KARLA, saveToCustomer: true, receiverEmail: null },
    });
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
    expect(issued).toEqual({ receiver: KARLA, saveToCustomer: false, receiverEmail: null });
  });

  it('sends the stamped invoice to the email it proposes, and takes no stamp with one that is not an address', async () => {
    let issued: unknown;
    serve({ ...INVOICING, proposedEmail: 'karla@ejemplo.mx' }, (_method, _path, body) => {
      issued = body;
      return Promise.resolve(STAMPED);
    });
    renderModal();

    const stamp = await screen.findByRole('button', { name: 'Timbrar factura' });
    const email = screen.getByLabelText('Correo del cliente (opcional)');
    expect(email).toHaveValue('karla@ejemplo.mx');
    expect(screen.getByLabelText('Guardar estos datos y el correo en Karla para sus próximas facturas')).toBeChecked();
    await waitFor(() => expect(stamp).toBeEnabled());

    await userEvent.clear(email);
    await userEvent.type(email, 'karla@ejemplo');
    expect(stamp).toBeDisabled();
    expect(email).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Escribe un correo válido, como nombre@ejemplo.com, o déjalo vacío.')).toBeInTheDocument();

    await userEvent.type(email, '.mx ');
    await waitFor(() => expect(stamp).toBeEnabled());
    await userEvent.click(stamp);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Factura F-1 timbrada: se está enviando a karla@ejemplo.mx.'));
    expect(issued).toEqual({ receiver: KARLA, saveToCustomer: true, receiverEmail: 'karla@ejemplo.mx' });
  });

  it('keeps nothing in the customer once the data are hidden, with the box to keep them', async () => {
    let issued: unknown;
    serve({ ...INVOICING, proposedEmail: 'karla@ejemplo.mx' }, (_method, _path, body) => {
      issued = body;
      return Promise.resolve(STAMPED);
    });
    renderModal();

    const stamp = await screen.findByRole('button', { name: 'Timbrar factura' });
    await userEvent.click(screen.getByRole('button', { name: 'Factura para XIQB891116QE4' }));
    expect(screen.queryByLabelText(/Guardar estos datos/)).not.toBeInTheDocument();
    await waitFor(() => expect(stamp).toBeEnabled());
    await userEvent.click(stamp);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Factura F-1 timbrada: se está enviando a karla@ejemplo.mx.'));
    expect(issued).toEqual({ receiver: KARLA, saveToCustomer: false, receiverEmail: 'karla@ejemplo.mx' });
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

  it('says the global invoice still covers a voided sale of it', async () => {
    serve({ ...INVOICING, canInvoice: false, problem: 'Una orden cancelada no se factura.', invoices: [], globalInvoiceNumber: 'F-12' });
    renderModal(false, undefined, true, true);

    expect(await screen.findByText(/^El pedido está cancelado y la factura global F-12 lo ampara/)).toBeInTheDocument();
    expect(screen.queryByText('Este pedido está cancelado y no tiene facturas.')).not.toBeInTheDocument();
  });

  it('is only a preview while the platform does not invoice', async () => {
    serve({ ...INVOICING, available: false, canInvoice: false, proposedReceiver: undefined, customerId: undefined });
    renderModal();

    expect(await screen.findByRole('heading', { name: 'Factura de ORD-000001 (vista previa)' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Timbrar factura' })).not.toBeInTheDocument();
  });

  it('says why the order cannot be invoiced', async () => {
    serve({
      ...INVOICING, canInvoice: false, problem: 'Este pedido ya tiene la factura F-1: cancélala para facturarlo otra vez.', invoices: [STAMPED],
      proposedEmail: 'karla@ejemplo.mx',
    });
    renderModal();

    expect(await screen.findByText('Este pedido ya tiene la factura F-1: cancélala para facturarlo otra vez.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Timbrar factura' })).not.toBeInTheDocument();
    // Without a stamp to go with, there is no email to write nor to keep.
    expect(screen.queryByLabelText('Correo del cliente (opcional)')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Guardar estos datos en Karla para sus próximas facturas')).toBeInTheDocument();
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

  it('shows the credit notes of its refunds, and says which CFDI go before cancelling the invoice', async () => {
    const note = {
      id: 'nc-1', orderInvoiceId: 'inv-1', number: 'NC-1', status: OrderInvoiceStatus.Stamped, paymentForm: '04', subTotal: 17.24,
      taxTotal: 2.76, total: 20, requestedAt: '2026-10-24T17:05:00Z', issuedAutomatically: true, uuid: null, stampedAt: null,
      hasXml: false, hasPdf: false, failureMessage: null, refusedAt: null, cancellationMotive: undefined, cancellationRequestedAt: null,
      cancelledAt: null, emails: [], requestedByCustomer: false,
    };
    serve({
      ...INVOICING,
      canInvoice: false,
      invoices: [STAMPED],
      adjustments: [{
        kind: CreditNoteAdjustment.Refund, adjustmentId: 'ref-1', at: '2026-10-24T17:00:00Z', amount: 20, method: 1, writeOffReason: undefined,
        notes: null, reversed: false, canIssue: false, problem: null, awaitsAutomaticIssue: false, creditNotes: [note],
      }],
    });
    renderModal(true);

    expect(await screen.findByRole('heading', { name: 'Notas de crédito' })).toBeInTheDocument();
    expect(screen.getByText('NC-1')).toBeInTheDocument();
    // The SAT does not cancel F-1 while NC-1 stands: the screen says so instead of offering what the API refuses.
    expect(screen.queryByRole('button', { name: 'Cancelar factura F-1' })).not.toBeInTheDocument();
    expect(screen.getByText('Para cancelarla, primero cancela NC-1.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar nota de crédito NC-1' })).toBeInTheDocument();
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

  describe('replacing an invoice with errors (Fase 28G)', () => {
    const LIVE_PROBLEM = 'Este pedido ya tiene la factura F-1: cancélala para facturarlo otra vez.';

    it('opens the form with the invoice\'s data and email to correct them, and stamps the new one in its place', async () => {
      let substituted: unknown;
      const wrong: Invoice = { ...STAMPED, receiver: { ...KARLA, postalCode: '26016' }, receiverEmail: 'karla.f@ejemplo.mx', canSubstitute: true };
      // The customer's fiscal data still has the wrong postal code: the order proposes it until someone changes it.
      const before: Invoicing = {
        ...INVOICING, canInvoice: false, problem: LIVE_PROBLEM, proposedEmail: 'karla@ejemplo.mx',
        proposedReceiver: wrong.receiver, invoices: [wrong],
      };
      const replacement: Invoice = { ...STAMPED, id: 'inv-2', number: 'F-2', replacesInvoiceId: 'inv-1', replacesNumber: 'F-1' };
      let current = before;
      serve(
        () => Promise.resolve(current),
        (method, path, body) => {
          substituted = { method, path, body };
          current = {
            ...before, problem: 'Este pedido ya tiene la factura F-2.',
            invoices: [replacement, { ...wrong, canSubstitute: false, replacedByInvoiceId: 'inv-2', replacedByNumber: 'F-2', status: OrderInvoiceStatus.Cancelling }],
          };
          return Promise.resolve(replacement);
        },
      );
      renderModal(true);

      await userEvent.click(await screen.findByRole('button', { name: 'Sustituir factura F-1' }));

      expect(screen.getByRole('group', { name: 'Sustituir la factura F-1' })).toHaveFocus();
      expect(screen.queryByText(LIVE_PROBLEM)).not.toBeInTheDocument();
      expect(screen.getByLabelText('Correo del cliente (opcional)')).toHaveValue('karla.f@ejemplo.mx');
      expect(screen.queryByRole('button', { name: 'Público en general' })).not.toBeInTheDocument();
      const zip = screen.getByLabelText('Código postal fiscal');
      expect(zip).toHaveValue('26016');
      await userEvent.clear(zip);
      await userEvent.type(zip, '26015');
      const replace = screen.getByRole('button', { name: 'Timbrar y sustituir la F-1' });
      expect(replace).toBeDisabled();
      await userEvent.click(screen.getByRole('button', { name: 'Ver con estos datos' }));
      await waitFor(() => expect(replace).toBeEnabled());
      await userEvent.click(replace);

      await waitFor(() => expect(toast.success)
        .toHaveBeenCalledWith('Factura F-2 timbrada en lugar de la F-1: se está enviando a karla.f@ejemplo.mx.'));
      expect(substituted).toEqual({
        method: 'post', path: '/api/v1/invoicing/invoices/{invoiceId}/substitute',
        body: { receiver: KARLA, saveToCustomer: true, receiverEmail: 'karla.f@ejemplo.mx' },
      });
      expect(screen.queryByRole('group', { name: 'Sustituir la factura F-1' })).not.toBeInTheDocument();
      // The form keeps what the new one went with, and the focus goes to the order's invoices.
      expect(screen.getByLabelText('Código postal fiscal')).toHaveValue('26015');
      expect(screen.getByRole('heading', { name: 'Facturas de este pedido' })).toHaveFocus();
    });

    it('gives the focus back to «Sustituir factura» when it is left', async () => {
      serve({ ...INVOICING, canInvoice: false, problem: LIVE_PROBLEM, invoices: [{ ...STAMPED, canSubstitute: true }] });
      renderModal(true);
      const open = await screen.findByRole('button', { name: 'Sustituir factura F-1' });
      await userEvent.click(open);
      expect(screen.getByRole('button', { name: 'Cancelar factura F-1' })).toBeDisabled();

      await userEvent.click(screen.getByRole('button', { name: 'No sustituir' }));

      expect(open).toHaveFocus();
      expect(screen.getByRole('button', { name: 'Cancelar factura F-1' })).toBeEnabled();
    });

    it('says the invoice is no longer replaced from here once it stops being the order\'s invoice', async () => {
      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      let current: Invoicing = { ...INVOICING, canInvoice: false, problem: LIVE_PROBLEM, invoices: [{ ...STAMPED, canSubstitute: true }] };
      serve(() => Promise.resolve(current));
      renderModal(true, queryClient);
      await userEvent.click(await screen.findByRole('button', { name: 'Sustituir factura F-1' }));

      // Someone cancelled it meanwhile.
      current = {
        ...current, problem: 'Este pedido ya tiene la factura F-1.',
        invoices: [{ ...STAMPED, status: OrderInvoiceStatus.Cancelling, cancellationMotive: CfdiCancellationMotive.NotCarriedOut }],
      };
      await queryClient.invalidateQueries({ queryKey: ['invoicing', 'orders', 'o-1'] });

      expect(await screen.findByText('La F-1 ya no es la factura vigente del pedido: no se sustituye.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Timbrar y sustituir la F-1' })).toBeDisabled();
    });

    it('keeps the form when the PAC refuses the new one, since the old one still stands, until it is left', async () => {
      const refusal = 'CFDI40147 - El código postal del receptor no corresponde al RFC.';
      serve(
        { ...INVOICING, canInvoice: false, problem: LIVE_PROBLEM, invoices: [{ ...STAMPED, canSubstitute: true }] },
        () => Promise.resolve({ ...STAMPED, id: 'inv-2', number: 'F-2', status: OrderInvoiceStatus.Refused, failureMessage: refusal }),
      );
      renderModal(true);
      await userEvent.click(await screen.findByRole('button', { name: 'Sustituir factura F-1' }));
      const replace = screen.getByRole('button', { name: 'Timbrar y sustituir la F-1' });
      await waitFor(() => expect(replace).toBeEnabled());

      await userEvent.click(replace);

      await waitFor(() => expect(toast.error).toHaveBeenCalledWith(`${refusal} La F-1 sigue vigente.`));
      expect(screen.getByRole('group', { name: 'Sustituir la factura F-1' })).toBeInTheDocument();
      await userEvent.click(screen.getByRole('button', { name: 'No sustituir' }));
      expect(screen.queryByRole('group', { name: 'Sustituir la factura F-1' })).not.toBeInTheDocument();
      expect(screen.getByText(LIVE_PROBLEM)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /Timbr/ })).not.toBeInTheDocument();
    });

    it('is offered to owners and admins only', async () => {
      serve({ ...INVOICING, canInvoice: false, problem: LIVE_PROBLEM, invoices: [{ ...STAMPED, canSubstitute: true }] });
      renderModal(false);

      expect(await screen.findByRole('button', { name: 'Descargar XML de la factura F-1' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Sustituir factura F-1' })).not.toBeInTheDocument();
    });

    it('says why the invoice cannot be replaced now', async () => {
      const otherRfc = 'La factura F-1 se timbró con otro RFC del negocio: cancélala (02) y factura otra vez.';
      serve({ ...INVOICING, canInvoice: false, problem: LIVE_PROBLEM, invoices: [{ ...STAMPED, substitutionProblem: otherRfc }] });
      renderModal(true);

      expect(await screen.findByText(`No se puede sustituir. ${otherRfc}`)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Sustituir factura F-1' })).not.toBeInTheDocument();
    });

    it('shows which invoice replaces which, cancels the old one with relation, and the new one only after it', async () => {
      const failure = 'El SAT todavía no relaciona esta factura con la que la sustituye: puede que aún no reciba la nueva (tarda hasta dos días).';
      const replacement: Invoice = { ...STAMPED, id: 'inv-2', number: 'F-2', replacesInvoiceId: 'inv-1', replacesNumber: 'F-1' };
      const replaced: Invoice = { ...STAMPED, replacedByInvoiceId: 'inv-2', replacedByNumber: 'F-2', failureMessage: failure };
      let asked: unknown;
      serve({ ...INVOICING, canInvoice: false, problem: 'Este pedido ya tiene la factura F-2.', invoices: [replacement, replaced] }, (method, path) => {
        asked = { method, path };
        return Promise.resolve({
          ...replaced, status: OrderInvoiceStatus.Cancelling, cancellationMotive: CfdiCancellationMotive.ErrorsWithReplacement, failureMessage: null,
        });
      });
      renderModal(true);

      expect(await screen.findByText('Sustituye a la factura F-1.')).toBeInTheDocument();
      expect(screen.getByText('Sustituida por la factura F-2.')).toBeInTheDocument();
      expect(screen.getByText(failure)).toBeInTheDocument();
      // The SAT cancels F-2 only once F-1, whose cancellation names it, is cancelled.
      expect(screen.queryByRole('button', { name: 'Cancelar factura F-2' })).not.toBeInTheDocument();
      expect(screen.getByText('Para cancelarla, primero cancela F-1.')).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Cancelar con relación la factura F-1' }));

      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cancelación de F-1 pedida: el SAT espera la respuesta del cliente.'));
      expect(asked).toEqual({ method: 'post', path: '/api/v1/invoicing/invoices/{invoiceId}/cancel-replaced' });
    });
  });
});
