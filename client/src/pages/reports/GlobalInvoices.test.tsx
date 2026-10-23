import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GlobalInvoice } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { GlobalInvoicePeriodicity, OrderInvoiceStatus, TenantCapability, type TenantProfileDto } from '../../types';
import GlobalInvoices from './GlobalInvoices';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

const request = vi.mocked(apiRequest);
type Options = { query?: { pageNumber?: number; pageSize?: number; search?: string } };

function invoice(patch: Partial<GlobalInvoice> = {}): GlobalInvoice {
  return {
    id: 'g-2', number: 'F-7', status: OrderInvoiceStatus.Stamped, periodicity: GlobalInvoicePeriodicity.Daily,
    periodFrom: '2026-10-22', periodTo: '2026-10-22', months: '10', year: 2026, orderCount: 3, paymentForm: '04',
    subTotal: 387.93, taxTotal: 62.07, total: 450, requestedAt: '2026-10-23T15:00:00Z', uuid: 'A1', stampedAt: '2026-10-23T15:00:02Z',
    hasXml: true, hasPdf: true, failureMessage: null, refusedAt: null, cancellationRequestedAt: null, cancelledAt: null,
    ...patch,
  };
}

function page(items: GlobalInvoice[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

function renderList(answer: (options?: Options) => GlobalInvoice[]) {
  request.mockImplementation(((method: string, path: string, options?: Options) => {
    if (method === 'get' && path === '/api/v1/invoicing/global-invoices') return Promise.resolve(page(answer(options)));
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <GlobalInvoices />
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

describe('GlobalInvoices', () => {
  beforeEach(signInAsOwner);
  afterEach(() => vi.clearAllMocks());

  it('lists each global invoice with its state and period, and tells a cancelled one from the one issued again', async () => {
    renderList(() => [
      invoice(),
      invoice({ id: 'g-1', number: 'F-5', status: OrderInvoiceStatus.Cancelled, orderCount: 1 }),
    ]);

    expect(await screen.findByRole('link', { name: 'Ver la factura global F-7 timbrada (Diaria · 22 oct. 2026)' }))
      .toHaveAttribute('href', '/reports/global-invoices/g-2');
    expect(screen.getByRole('link', { name: 'Ver la factura global F-5 cancelada (Diaria · 22 oct. 2026)' }))
      .toHaveAttribute('href', '/reports/global-invoices/g-1');
    expect(screen.getByText('3 ventas')).toBeInTheDocument();
    expect(screen.getByText('1 venta')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Nueva factura global' })).toHaveAttribute('href', '/reports/global-invoices/new');
  });

  it('finds which global invoice took a sale by its folio', async () => {
    const user = userEvent.setup();
    renderList((options) => (options?.query?.search === 'ORD-000123' ? [invoice({ number: 'F-3', id: 'g-3' })] : [invoice()]));
    await screen.findByText('F-7');

    await user.type(screen.getByRole('searchbox', { name: 'Buscar folio, UUID o folio de una venta' }), 'ORD-000123');

    expect(await screen.findByText('F-3')).toBeInTheDocument();
    await waitFor(() => expect(request).toHaveBeenLastCalledWith('get', '/api/v1/invoicing/global-invoices', expect.objectContaining({
      query: { pageNumber: 1, pageSize: 20, search: 'ORD-000123' },
    })));
  });

  it('says why the list could not be read and reads it again on request', async () => {
    const user = userEvent.setup();
    let reads = 0;
    request.mockImplementation((() => (reads++ === 0
      ? Promise.reject(Object.assign(new Error('503'), { isAxiosError: true, response: { status: 503, data: {} } }))
      : Promise.resolve(page([invoice()])))) as unknown as typeof apiRequest);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <GlobalInvoices />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('No se pudieron cargar las facturas globales.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByText('F-7')).toBeInTheDocument();
  });

  it('says there is none yet', async () => {
    renderList(() => []);

    expect(await screen.findByText('Todavía no emites facturas globales.')).toBeInTheDocument();
  });
});
