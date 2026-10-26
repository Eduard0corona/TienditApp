import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it, vi } from 'vitest';
import type { Schemas } from '../../api/typedClient';
import { OrderInvoiceStatus } from '../../types';
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
  receiverEmail: 'karla@ejemplo.mx', emails: [], requestedByCustomer: false,
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
});
