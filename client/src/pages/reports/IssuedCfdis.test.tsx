import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import api from '../../api/axios';
import type { IssuedCfdi, IssuedCfdiSummary } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import {
  GlobalInvoicePeriodicity, IssuedCfdiExportFormat, IssuedCfdiKind, OrderInvoiceStatus, TenantCapability, type TenantProfileDto,
} from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import IssuedCfdis from './IssuedCfdis';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../api/axios', () => ({ default: { get: vi.fn() } }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const request = vi.mocked(apiRequest);
type Query = { year: number; month: number; kind?: IssuedCfdiKind; status?: OrderInvoiceStatus; search?: string; pageNumber?: number };

function cfdi(patch: Partial<IssuedCfdi> = {}): IssuedCfdi {
  return {
    id: 'i-1', kind: IssuedCfdiKind.OrderInvoice, number: 'F-12', uuid: '5FB2A1', status: OrderInvoiceStatus.Stamped,
    issuedAt: '2026-10-20T18:00:00Z', stampedAt: '2026-10-20T18:00:02Z', cancellationMotive: undefined, cancelledAt: null,
    failureMessage: null, receiverRfc: 'XIQB891116QE4', receiverName: 'KARLA FUENTE NOLASCO', paymentMethod: 'PUE',
    paymentForm: '04', subTotal: 172.41, taxTotal: 27.59, total: 200, paidAmount: null, orderId: 'o-1', orderNumber: 'ORD-000123',
    relatedNumber: null, replacedByNumber: null, periodicity: undefined, periodFrom: null, periodTo: null, orderCount: null, hasXml: true,
    ...patch,
  };
}

const SUMMARY: IssuedCfdiSummary = {
  year: 2026, month: 10, from: '2026-10-01', to: '2026-10-31',
  kinds: [
    { kind: IssuedCfdiKind.OrderInvoice, inForce: 2, subTotal: 344.82, taxTotal: 55.18, total: 400, cancelling: 1, cancelled: 1, stamping: 0, refused: 1 },
    { kind: IssuedCfdiKind.GlobalInvoice, inForce: 1, subTotal: 86.21, taxTotal: 13.79, total: 100, cancelling: 0, cancelled: 0, stamping: 0, refused: 0 },
    { kind: IssuedCfdiKind.PaymentComplement, inForce: 1, subTotal: 0, taxTotal: 0, total: 120, cancelling: 0, cancelled: 0, stamping: 0, refused: 0 },
    { kind: IssuedCfdiKind.CreditNote, inForce: 1, subTotal: 17.24, taxTotal: 2.76, total: 20, cancelling: 0, cancelled: 0, stamping: 0, refused: 0 },
  ],
};

function renderPage(list: (query: Query) => IssuedCfdi[]) {
  request.mockImplementation(((method: string, path: string, options?: { query: Query }) => {
    if (path === '/api/v1/invoicing/issued-cfdis') {
      const items = list(options!.query);
      return Promise.resolve({ items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false });
    }
    if (path === '/api/v1/invoicing/issued-cfdis/summary') return Promise.resolve({ ...SUMMARY, ...options!.query });
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/reports/issued-cfdis']}>
        <IssuedCfdis />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function lastQuery(path: string): Query {
  const calls = request.mock.calls.filter(([, called]) => called === path);
  return (calls[calls.length - 1][2] as unknown as { query: Query }).query;
}

/** An owner of a store: the month's CFDI are theirs, and the tabs of Reportes show them. */
function signInAsOwner() {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Tienda Luna', slug: 'luna', timeZone: 'America/Mexico_City' } as TenantProfileDto,
    capabilities: TenantCapability.Retail,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

describe('IssuedCfdis', () => {
  beforeEach(() => {
    signInAsOwner();
    // 26 October 2026 at noon in Mexico City.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T18:00:00Z'));
    setBusinessTimeZone('America/Mexico_City');
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it("opens on the business's month with what the CFDI in force add up to, and what was cancelled, waits or was refused", async () => {
    renderPage(() => [cfdi()]);

    expect(await screen.findByRole('heading', { name: 'Octubre de 2026' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'CFDI emitidos' })).toHaveAttribute('aria-current', 'page');
    expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ year: 2026, month: 10, pageNumber: 1 });
    const summary = await screen.findByRole('region', { name: 'Resumen del mes' });
    const invoices = within(summary).getByRole('row', { name: /Facturas 2/ });
    expect(invoices).toHaveTextContent('$344.82');
    expect(invoices).toHaveTextContent('$400.00');
    expect(within(summary).getByRole('row', { name: /Complementos de pago/ })).toHaveTextContent('Pagado $120.00');
    expect(summary).toHaveTextContent(
      'Además: 1 cancelado; 1 espera que el cliente acepte su cancelación en el SAT; 1 rechazado por el PAC: no son CFDI y explican los folios que faltan.',
    );
    // A month that has not come yet has nothing to show.
    expect(screen.getByRole('button', { name: 'Mes siguiente (noviembre de 2026)' })).toBeDisabled();
  });

  it('lists each CFDI with its kind, state, receiver and where it comes from', async () => {
    renderPage(() => [
      cfdi({ id: 'p-1', kind: IssuedCfdiKind.PaymentComplement, number: 'P-1', total: null, subTotal: null, taxTotal: null, paidAmount: 120,
        paymentMethod: null, relatedNumber: 'F-11' }),
      cfdi({ id: 'g-1', kind: IssuedCfdiKind.GlobalInvoice, number: 'F-10', receiverRfc: 'XAXX010101000', receiverName: 'PUBLICO EN GENERAL',
        orderId: null, orderNumber: null, periodicity: GlobalInvoicePeriodicity.Daily, periodFrom: '2026-10-08', periodTo: '2026-10-08',
        orderCount: 3 }),
      cfdi({ id: 'i-2', number: 'F-9', relatedNumber: 'F-8' }),
      cfdi({ id: 'i-1', number: 'F-8', status: OrderInvoiceStatus.Cancelled, replacedByNumber: 'F-9' }),
      cfdi({ id: 'i-0', number: 'F-7', status: OrderInvoiceStatus.Refused, uuid: null, hasXml: false, failureMessage: 'El RFC del receptor no existe.' }),
    ]);

    const complement = (await screen.findByText('P-1')).closest('tr')!;
    expect(complement).toHaveTextContent('Complemento de pago');
    expect(complement).toHaveTextContent('Timbrado');
    expect(complement).toHaveTextContent('Pago de la F-11');
    expect(complement).toHaveTextContent('Pagado $120.00');
    expect(within(complement).getByRole('link', { name: 'ORD-000123' })).toHaveAttribute('href', '/orders?search=ORD-000123');
    expect(within(complement).getByRole('button', { name: 'XML del complemento de pago P-1' })).toBeInTheDocument();

    const global = screen.getByText('F-10').closest('tr')!;
    expect(within(global).getByRole('link', { name: 'Diaria · 8 oct. 2026: ver la factura global F-10' }))
      .toHaveAttribute('href', '/reports/global-invoices/g-1');
    expect(global).toHaveTextContent('3 ventas');
    expect(global).toHaveTextContent('PUBLICO EN GENERAL');

    expect(screen.getByText('F-9').closest('tr')).toHaveTextContent('Sustituye a la F-8');
    const replaced = screen.getByText('F-8', { selector: '.fw-semibold' }).closest('tr')!;
    expect(replaced).toHaveTextContent('Cancelada');
    expect(replaced).toHaveTextContent('Sustituida por la F-9');
    // A refused one says why, and has no files.
    const refused = screen.getByText('F-7').closest('tr')!;
    expect(refused).toHaveTextContent('El RFC del receptor no existe.');
    expect(within(refused).queryByRole('button', { name: /XML/ })).not.toBeInTheDocument();
  });

  it('narrows the month by kind, state and search, and goes back a month', async () => {
    const user = userEvent.setup();
    renderPage(() => [cfdi()]);
    await screen.findByText('F-12');

    await user.selectOptions(screen.getByLabelText('Tipo'), 'Notas de crédito');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ kind: IssuedCfdiKind.CreditNote }));
    await user.selectOptions(screen.getByLabelText('Estado'), 'Canceladas');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis'))
      .toMatchObject({ kind: IssuedCfdiKind.CreditNote, status: OrderInvoiceStatus.Cancelled }));
    await user.type(screen.getByPlaceholderText('Buscar folio, UUID, RFC, nombre o pedido'), 'xiqb');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ search: 'xiqb' }));

    await user.click(screen.getByRole('button', { name: 'Mes anterior (septiembre de 2026)' }));
    expect(await screen.findByRole('heading', { name: 'Septiembre de 2026' })).toBeInTheDocument();
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ year: 2026, month: 9, pageNumber: 1 }));
    expect(lastQuery('/api/v1/invoicing/issued-cfdis/summary')).toMatchObject({ year: 2026, month: 9 });
    expect(screen.getByRole('button', { name: 'Mes siguiente (octubre de 2026)' })).toBeEnabled();
  });

  it("downloads the month's XML for the accountant, and says why when it cannot", async () => {
    const user = userEvent.setup();
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.mocked(api.get).mockResolvedValueOnce({
      data: new Blob(['zip']), headers: { 'content-disposition': 'attachment; filename="CFDI_2026-10.zip"' },
    });
    renderPage(() => [cfdi()]);
    await screen.findByText('F-12');

    await user.click(screen.getByRole('button', { name: 'Descargar XML' }));

    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/invoicing/issued-cfdis/export', {
      responseType: 'blob', params: { year: 2026, month: 10, format: IssuedCfdiExportFormat.Zip },
    }));
    expect(created).toHaveBeenCalled();

    vi.mocked(api.get).mockRejectedValueOnce(Object.assign(new Error('400'), {
      isAxiosError: true,
      response: { status: 400, data: new Blob([JSON.stringify({ detail: 'El mes tiene 6,000 CFDI y la descarga junta hasta 5,000.' })]) },
    }));
    await user.click(screen.getByRole('button', { name: 'Descargar lista' }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('El mes tiene 6,000 CFDI y la descarga junta hasta 5,000.'));
    created.mockRestore();
  });

  it('downloads the XML of a row from where its kind keeps it', async () => {
    const user = userEvent.setup();
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.mocked(api.get).mockResolvedValue({ data: new Blob(['<cfdi/>']), headers: {} });
    renderPage(() => [cfdi({ id: 'n-1', kind: IssuedCfdiKind.CreditNote, number: 'NC-1', relatedNumber: 'F-12' })]);

    await user.click(await screen.findByRole('button', { name: 'PDF de la nota de crédito NC-1' }));

    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/invoicing/credit-notes/n-1/pdf', { responseType: 'blob' }));
    created.mockRestore();
  });
});
