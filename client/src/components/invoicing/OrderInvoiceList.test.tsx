import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
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
  receiverEmail: 'karla@ejemplo.mx', emails: [], requestedByCustomer: false, canSubstitute: false, awaitsReplacedCancellation: false,
};

function renderList(invoices: Invoice[]) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <OrderInvoiceList orderId="o-1" invoices={invoices} canCancel />
    </QueryClientProvider>,
  );
}

describe('OrderInvoiceList', () => {
  it('stops offering to ask the PAC again past its window, and says where it is checked instead (Fase 28K)', () => {
    const stuck: Invoice = {
      ...F7, status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false,
      stampWindowEndsAt: '2020-01-04T17:00:00Z', failureMessage: 'El PAC no responde.',
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
});
