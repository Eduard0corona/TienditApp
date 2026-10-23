import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import api from '../../api/axios';
import type { GlobalInvoiceDetail } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { CfdiCancellationMotive, GlobalInvoicePeriodicity, OrderInvoiceStatus, TenantCapability, type TenantProfileDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import GlobalInvoicePage from './GlobalInvoicePage';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../api/axios', () => ({ default: { get: vi.fn() } }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const request = vi.mocked(apiRequest);

function invoice(patch: Partial<GlobalInvoiceDetail> = {}): GlobalInvoiceDetail {
  return {
    id: 'g-1', number: 'F-5', status: OrderInvoiceStatus.Stamped, periodicity: GlobalInvoicePeriodicity.Weekly,
    periodFrom: '2026-10-12', periodTo: '2026-10-18', months: '10', year: 2026, orderCount: 2, paymentForm: '01',
    subTotal: 258.62, taxTotal: 41.38, total: 300, requestedAt: '2026-10-19T15:00:00Z',
    uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: '2026-10-19T15:00:02Z', hasXml: true, hasPdf: true,
    failureMessage: null, refusedAt: null, cancellationRequestedAt: null, cancelledAt: null,
    orders: [
      { orderId: 'o-1', orderNumber: 'ORD-000001', total: 200 },
      { orderId: 'o-2', orderNumber: 'ORD-000002', total: 100 },
    ],
    ...patch,
  };
}

function renderPage(detail: () => Promise<GlobalInvoiceDetail>, write: (path: string, body: unknown) => Promise<unknown> = () => Promise.reject(new Error('sin respuesta'))) {
  request.mockImplementation(((method: string, path: string, options?: { body?: unknown }) => {
    if (method === 'get' && path === '/api/v1/invoicing/global-invoices/{invoiceId}') return detail();
    return write(path, options?.body);
  }) as unknown as typeof apiRequest);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/reports/global-invoices/g-1']}>
        <Routes>
          <Route path="/reports/global-invoices/:id" element={<GlobalInvoicePage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** An owner of a restaurant: the global invoice is theirs, and the tabs of Reportes show it. */
function signInAsOwner() {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita', timeZone: 'America/Mexico_City' } as TenantProfileDto,
    capabilities: TenantCapability.Food,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

describe('GlobalInvoicePage', () => {
  beforeEach(() => {
    setBusinessTimeZone('America/Mexico_City');
    signInAsOwner();
  });
  afterEach(() => vi.clearAllMocks());

  it('shows the period, the amounts and the sales it covers', async () => {
    renderPage(() => Promise.resolve(invoice()));

    expect(await screen.findByRole('heading', { name: 'Semanal · 12 al 18 oct. 2026' })).toBeInTheDocument();
    expect(screen.getByText('Timbrada', { selector: '.ta-chip' })).toBeInTheDocument();
    expect(screen.getByText('5FB2822E-396D-4725-8521-CDC4BDD20CCF')).toBeInTheDocument();
    expect(screen.getByText('01 · Efectivo')).toBeInTheDocument();
    const sales = screen.getByRole('table');
    expect(within(sales).getByText('ORD-000002')).toBeInTheDocument();
  });

  it('cancels with 04 by default, the motive of a customer asking for the invoice of a sale it holds', async () => {
    const user = userEvent.setup();
    let current = invoice();
    const write = vi.fn((path: string, body: unknown) => {
      current = invoice({ status: OrderInvoiceStatus.Cancelled, cancelledAt: '2026-10-23T18:00:00Z' });
      return Promise.resolve({ ...current, path, body });
    });
    renderPage(() => Promise.resolve(current), write);

    await user.click(await screen.findByRole('button', { name: 'Cancelar factura global' }));
    const dialog = screen.getByRole('dialog', { name: 'Cancelar la factura global F-5' });
    expect(within(dialog).getByLabelText('04 · Un cliente pidió la factura de una de sus ventas')).toBeChecked();
    // 03 is an order's motive, not a global invoice's.
    expect(within(dialog).queryByLabelText(/^03/)).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar factura global' }));

    await waitFor(() => expect(write).toHaveBeenCalledWith(
      '/api/v1/invoicing/global-invoices/{invoiceId}/cancel', { motive: CfdiCancellationMotive.NominativeInGlobalInvoice }));
    expect(toast.success).toHaveBeenCalledWith('Factura global F-5 cancelada.');
    expect(await screen.findByText('Cancelada', { selector: '.ta-chip' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar factura global' })).not.toBeInTheDocument();
  });

  it('downloads its XML with the session', async () => {
    const user = userEvent.setup();
    vi.mocked(api.get).mockResolvedValue({ data: new Blob(['<cfdi/>']), headers: { 'content-disposition': 'attachment; filename="F-5_5FB2.xml"' } });
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    renderPage(() => Promise.resolve(invoice()));

    await user.click(await screen.findByRole('button', { name: 'Descargar XML de la factura global F-5' }));

    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/invoicing/global-invoices/g-1/xml', { responseType: 'blob' }));
    expect(created).toHaveBeenCalled();
    created.mockRestore();
  });

  it('asks the PAC again while it stamps, and offers neither files nor cancelling', async () => {
    const user = userEvent.setup();
    const write = vi.fn(() => Promise.resolve(invoice({ status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false })));
    renderPage(() => Promise.resolve(invoice({ status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false })), write);

    await user.click(await screen.findByRole('button', { name: 'Volver a pedir el timbre' }));

    await waitFor(() => expect(write).toHaveBeenCalledWith('/api/v1/invoicing/global-invoices/{invoiceId}/retry', undefined));
    expect(toast.info).toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: /Descargar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar factura global' })).not.toBeInTheDocument();
  });

  it('says so when the global invoice is not the business\'s', async () => {
    renderPage(() => Promise.reject(Object.assign(new Error('404'), { isAxiosError: true, response: { status: 404, data: {} } })));

    expect(await screen.findByText('No encontramos esta factura global.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
  });
});
