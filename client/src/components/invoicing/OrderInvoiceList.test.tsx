import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { orderInvoicingKey } from '../../api/invoicing';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { CfdiCancellationMotive, OrderInvoiceStatus } from '../../types';
import OrderInvoiceList from './OrderInvoiceList';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

type Invoice = Schemas['OrderInvoiceDto'];

const F7: Invoice = {
  id: 'inv-7', orderId: 'o-1', number: 'F-7', status: OrderInvoiceStatus.Stamped,
  receiver: { rfc: 'XIQB891116QE4', name: 'KARLA FUENTE NOLASCO', postalCode: '26015', fiscalRegime: '612', cfdiUse: 'G03' },
  paymentForm: '04', paymentMethod: 'PUE', subTotal: 100, taxTotal: 16, total: 116, requestedAt: '2026-10-26T18:00:00Z',
  uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: '2026-10-26T18:00:05Z', hasXml: true, hasPdf: true,
  failureMessage: null, refusedAt: null, cancellationMotive: undefined, cancellationRequestedAt: null, cancelledAt: null,
  receiverEmail: 'karla@ejemplo.mx', emails: [], whatsApps: [], requestedByCustomer: false, canSubstitute: false, awaitsReplacedCancellation: false,
};

function renderList(invoices: Invoice[]) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <OrderInvoiceList orderId="o-1" invoices={invoices} canCancel />
    </QueryClientProvider>,
  );
}


/** The order's invoicing on screen: read once, and read again after a change, when it does not come back. */
function InvoicingOnScreen({ read }: { read: () => Promise<unknown> }) {
  useQuery({ queryKey: orderInvoicingKey('o-1'), queryFn: read });
  return null;
}

describe('OrderInvoiceList', () => {
  it('stops offering to ask the PAC again past its window, and says where it is checked instead (Fase 28K)', () => {
    const stuck: Invoice = {
      ...F7, status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false,
      stampWindowEndsAt: '2020-01-04T17:00:00Z', failureMessage: null,
    };
    const waiting: Invoice = { ...stuck, id: 'inv-8', number: 'F-8', stampWindowEndsAt: '2999-01-01T00:00:00Z' };
    const view = (canCancel: boolean) => (
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <OrderInvoiceList orderId="o-1" invoices={[waiting, stuck]} canCancel={canCancel} />
        </MemoryRouter>
      </QueryClientProvider>
    );
    const { rerender } = render(view(true));

    expect(screen.getByRole('button', { name: 'Volver a pedir el timbre de la factura F-8' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Volver a pedir el timbre de la factura F-7' })).not.toBeInTheDocument();
    // TienditApp keeps asking only for the one still within the window.
    expect(screen.getAllByText(/TienditApp lo vuelve a intentar/)).toHaveLength(1);
    expect(screen.getByText(/Se pidió hace más de 71 horas y el PAC ya no devuelve su timbre/)).toHaveTextContent(
      'Revisa en el portal del SAT si se timbró y dilo en CFDI emitidos.');
    expect(screen.getByRole('link', { name: 'CFDI emitidos' })).toHaveAttribute('href', '/reports/issued-cfdis');

    // Whoever does not open CFDI emitidos is told who checks it.
    rerender(view(false));
    expect(screen.getByText(/Se pidió hace más de 71 horas/)).toHaveTextContent(
      'El dueño o un administrador revisa en el portal del SAT si se timbró.');
    expect(screen.queryByRole('link', { name: 'CFDI emitidos' })).not.toBeInTheDocument();
  });

  it('says an invoice was requested by the customer online with the ticket (Fase 28F)', () => {
    renderList([{ ...F7, requestedByCustomer: true }, { ...F7, id: 'inv-6', number: 'F-6', status: OrderInvoiceStatus.Cancelled }]);

    expect(screen.getAllByText('La pidió el cliente en línea con su ticket.')).toHaveLength(1);
  });

  it('says a replaced invoice is cancelled with relation once its replacement is stamped, with nothing to press before (Fase 28G)', () => {
    const replacement: Invoice = { ...F7, id: 'inv-8', number: 'F-8', status: OrderInvoiceStatus.Stamping, uuid: null, replacesInvoiceId: 'inv-7', replacesNumber: 'F-7' };
    renderList([replacement, { ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8', awaitsReplacedCancellation: true }]);

    expect(screen.getByText('En cuanto la F-8 quede timbrada, TienditApp le pide al SAT cancelar esta con relación (motivo 01).')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Cancelar con relación/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar factura F-7' })).not.toBeInTheDocument();
    // What goes to the customer is the new one.
    expect(screen.queryByRole('button', { name: /^Enviar por correo la factura F-7/ })).not.toBeInTheDocument();
  });

  it('says a replaced invoice is cancelled in the next check once its replacement is stamped, and lets it be asked now', () => {
    const replacement: Invoice = { ...F7, id: 'inv-8', number: 'F-8', replacesInvoiceId: 'inv-7', replacesNumber: 'F-7' };
    renderList([replacement, { ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8', awaitsReplacedCancellation: true }]);

    expect(screen.getByText(
      'La F-8 ya está timbrada: TienditApp le pide al SAT cancelar esta con relación (motivo 01) en su siguiente revisión, cada 30 minutos.',
    )).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar con relación la factura F-7' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Cancelar factura F-7' })).not.toBeInTheDocument();
  });

  it('says TienditApp no longer asks on its own once the API says so (72 hours), and lets it be cancelled with or without relation', () => {
    const replacement: Invoice = { ...F7, id: 'inv-8', number: 'F-8', replacesInvoiceId: 'inv-7', replacesNumber: 'F-7' };
    renderList([replacement, { ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8', awaitsReplacedCancellation: false }]);

    expect(screen.getByText(
      'Pasaron 72 horas desde que se timbró la F-8 y TienditApp ya no lo pide solo: usa «Cancelar con relación» o cancélala sin relación (02).',
    )).toBeInTheDocument();
    expect(screen.queryByText(/en su siguiente revisión/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar con relación la factura F-7' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Cancelar factura F-7' })).toBeEnabled();
  });

  it('cancels without relation (02) a replaced invoice when the SAT does not relate it to its replacement', async () => {
    renderList([{ ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8', failureMessage: 'El SAT no la relaciona.' }]);

    await userEvent.click(screen.getByRole('button', { name: 'Cancelar factura F-7' }));

    const dialog = screen.getByRole('dialog', { name: 'Cancelar la factura F-7' });
    expect(dialog).toHaveTextContent('La F-8 la sustituye: cancélala así solo si el SAT no la cancela con relación (01).');
    expect(within(dialog).getByLabelText('02 · Tiene errores, sin relacionarla con la F-8')).toBeChecked();
    expect(within(dialog).getByLabelText('03 · La venta no se llevó a cabo')).toBeInTheDocument();

    vi.mocked(apiRequest).mockResolvedValue({ ...F7, status: OrderInvoiceStatus.Cancelling });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar factura' }));

    await vi.waitFor(() => expect(apiRequest).toHaveBeenCalledWith(
      'post', '/api/v1/invoicing/invoices/{invoiceId}/cancel',
      { path: { invoiceId: 'inv-7' }, body: { motive: CfdiCancellationMotive.ErrorsWithoutReplacement } },
    ));
  });

  it.each([
    ['asks the PAC again', [{
      ...F7, id: 'inv-8', number: 'F-8', status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false,
      stampWindowEndsAt: '2999-01-01T00:00:00Z',
    }], 'Volver a pedir el timbre de la factura F-8', 'Factura F-8 timbrada.'],
    ['cancels with relation', [
      { ...F7, id: 'inv-8', number: 'F-8', replacesInvoiceId: 'inv-7', replacesNumber: 'F-7' },
      { ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8', awaitsReplacedCancellation: true },
    ], 'Cancelar con relación la factura F-7', 'Factura F-7 cancelada.'],
  ] as [string, Invoice[], string, string][])('%s with the answer, and offers it again only once the invoicing is read again', async (_, invoices, name, said) => {
    vi.mocked(apiRequest).mockReset();
    toast.success.mockReset();
    vi.mocked(apiRequest).mockImplementation(((_method: string, path: string) => Promise.resolve(path.endsWith('/retry')
      ? { ...invoices[0], status: OrderInvoiceStatus.Stamped }
      : { ...F7, status: OrderInvoiceStatus.Cancelled })) as unknown as typeof apiRequest);
    let reread: (value: unknown) => void = () => undefined;
    const invoicing = vi.fn().mockResolvedValueOnce({}).mockReturnValue(new Promise((resolve) => { reread = resolve; }));
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MemoryRouter>
          <OrderInvoiceList orderId="o-1" invoices={invoices} canCancel />
        </MemoryRouter>
        <InvoicingOnScreen read={invoicing} />
      </QueryClientProvider>,
    );
    await waitFor(() => expect(invoicing).toHaveBeenCalledTimes(1));

    await userEvent.click(screen.getByRole('button', { name }));

    // The answer arrives although the invoicing does not come back; until it does, what it said before is not offered.
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(said));
    expect(invoicing).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('button', { name })).toBeDisabled();
    reread({});
    await waitFor(() => expect(screen.getByRole('button', { name })).toBeEnabled());
  });

  it.each<[string, Invoice[], string]>([
    ['the PAC is asked again for one', [{
      ...F7, id: 'inv-8', number: 'F-8', status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false,
      stampWindowEndsAt: '2999-01-01T00:00:00Z',
    }], 'Volver a pedir el timbre de la factura F-8'],
    ['one is cancelled with relation', [
      { ...F7, id: 'inv-8', number: 'F-8', replacesInvoiceId: 'inv-7', replacesNumber: 'F-7' },
      { ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8', awaitsReplacedCancellation: true },
    ], 'Cancelar con relación la factura F-7'],
  ])('offers nothing from a list opened again while %s', async (_, invoices, name) => {
    vi.mocked(apiRequest).mockReset();
    vi.mocked(apiRequest).mockReturnValue(new Promise(() => undefined) as never);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const view = (open: boolean) => (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>{open && <OrderInvoiceList orderId="o-1" invoices={invoices} canCancel />}</MemoryRouter>
      </QueryClientProvider>
    );
    const { rerender } = render(view(true));
    await userEvent.click(screen.getByRole('button', { name }));

    rerender(view(false));
    rerender(view(true));

    expect(screen.getByRole('button', { name })).toBeDisabled();
    expect(apiRequest).toHaveBeenCalledTimes(1);
  });

  it('stays open while the cancellation is on its way, and closes with the answer before the invoicing is read again', async () => {
    const user = userEvent.setup();
    let cancelled: (value: unknown) => void = () => undefined;
    vi.mocked(apiRequest).mockReturnValue(new Promise((resolve) => { cancelled = resolve; }) as never);
    const invoicing = vi.fn().mockResolvedValueOnce({}).mockReturnValue(new Promise(() => undefined));
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <OrderInvoiceList orderId="o-1" invoices={[F7]} canCancel />
        <InvoicingOnScreen read={invoicing} />
      </QueryClientProvider>,
    );
    await waitFor(() => expect(invoicing).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole('button', { name: 'Cancelar factura F-7' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la factura F-7' });
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar factura' }));

    expect(within(dialog).getByRole('button', { name: 'Cancelando…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Volver' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog', { name: 'Cancelar la factura F-7' })).toBeInTheDocument();

    cancelled({ ...F7, status: OrderInvoiceStatus.Cancelling });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith('Cancelación de F-7 pedida: el SAT espera la respuesta del cliente.');
    expect(invoicing).toHaveBeenCalledTimes(2);
    // Until the invoicing comes back, it is not offered from what it said before.
    expect(screen.getByRole('button', { name: 'Cancelar factura F-7' })).toBeDisabled();
  });
});

describe('OrderInvoiceList by WhatsApp (Fase 28M)', () => {
  const sent = {
    id: 'm-1', document: 0, cfdiId: 'inv-7', phone: '+525512345678', requestedAt: '2026-10-26T18:00:06Z', byTienditApp: true,
    status: 1, sentAt: '2026-10-26T18:00:30Z', deliveredAt: '2026-10-26T18:00:35Z', readAt: null, attempts: 1, error: null,
  } as Invoice['whatsApps'][number];

  it('offers «Enviar por WhatsApp» only with the WhatsApp of the business connected, and says how the latest went', () => {
    const view = (whatsAppConnected: boolean) => (
      <QueryClientProvider client={new QueryClient()}>
        <OrderInvoiceList orderId="o-1" invoices={[{ ...F7, whatsApps: [sent] }]} canCancel whatsAppConnected={whatsAppConnected} />
      </QueryClientProvider>
    );
    const { rerender } = render(view(false));
    expect(screen.queryByRole('button', { name: /^Enviar por WhatsApp/ })).not.toBeInTheDocument();
    // What was sent before it was disconnected still shows.
    expect(screen.getByText(/^Se entregó por WhatsApp a 5512345678 el /)).toBeInTheDocument();

    rerender(view(true));
    expect(screen.getByRole('button', { name: 'Enviar por WhatsApp la factura F-7' })).toBeInTheDocument();
  });

  it('sends what replaces an invoice, never the replaced one', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <OrderInvoiceList orderId="o-1" invoices={[{ ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8' }]} canCancel whatsAppConnected />
      </QueryClientProvider>,
    );
    expect(screen.queryByRole('button', { name: /^Enviar por WhatsApp/ })).not.toBeInTheDocument();
  });
});
