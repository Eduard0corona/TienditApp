import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
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
  receiverEmail: 'karla@ejemplo.mx', emails: [], requestedByCustomer: false, canSubstitute: false,
};

function renderList(invoices: Invoice[]) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <OrderInvoiceList orderId="o-1" invoices={invoices} canCancel />
    </QueryClientProvider>,
  );
}

describe('OrderInvoiceList', () => {
  it('says an invoice was requested by the customer online with the ticket (Fase 28F)', () => {
    renderList([{ ...F7, requestedByCustomer: true }, { ...F7, id: 'inv-6', number: 'F-6', status: OrderInvoiceStatus.Cancelled }]);

    expect(screen.getAllByText('La pidió el cliente en línea con su ticket.')).toHaveLength(1);
  });

  it('says a replaced invoice is cancelled with relation once its replacement is stamped, with nothing to press before (Fase 28G)', () => {
    const replacement: Invoice = { ...F7, id: 'inv-8', number: 'F-8', status: OrderInvoiceStatus.Stamping, uuid: null, replacesInvoiceId: 'inv-7', replacesNumber: 'F-7' };
    renderList([replacement, { ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8' }]);

    expect(screen.getByText('En cuanto la F-8 quede timbrada, TienditApp le pide al SAT cancelar esta con relación (motivo 01).')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Cancelar con relación/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar factura F-7' })).not.toBeInTheDocument();
    // What goes to the customer is the new one.
    expect(screen.queryByRole('button', { name: /^Enviar por correo la factura F-7/ })).not.toBeInTheDocument();
  });

  it('says a replaced invoice is cancelled in the next check once its replacement is stamped, and lets it be asked now', () => {
    const replacement: Invoice = { ...F7, id: 'inv-8', number: 'F-8', replacesInvoiceId: 'inv-7', replacesNumber: 'F-7' };
    renderList([replacement, { ...F7, replacedByInvoiceId: 'inv-8', replacedByNumber: 'F-8' }]);

    expect(screen.getByText(
      'La F-8 ya está timbrada: TienditApp le pide al SAT cancelar esta con relación (motivo 01) en su siguiente revisión, cada 30 minutos.',
    )).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar con relación la factura F-7' })).toBeEnabled();
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
