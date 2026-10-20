import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { TenantCapability } from '../../types';
import ReceivablesPage from './ReceivablesPage';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));

const request = vi.mocked(apiRequest);

const RECEIVABLES = {
  totalOwed: 250,
  page: {
    items: [
      { phone: '+525512345678', name: 'Ana López', customerId: 'c1', owed: 200, orders: 2, since: '2026-10-10T18:00:00Z', creditLimit: 150, overLimit: true },
      { phone: '+525566667777', name: 'Luis Pérez', customerId: null, owed: 50, orders: 1, since: '2026-10-18T18:00:00Z', creditLimit: null, overLimit: false },
    ],
    pageNumber: 1, totalPages: 1, totalCount: 2, hasPreviousPage: false, hasNextPage: false,
  },
};

const LUIS_ACCOUNT = {
  phone: '+525566667777', name: 'Luis Pérez', customerId: null, creditLimit: null, sellsOnCredit: false, owed: 50, available: null,
  overLimit: false, ordersCount: 1, recentPayments: [],
  orders: [{ orderId: 'o9', orderNumber: 'ORD-000020', createdAt: '2026-10-18T18:00:00Z', status: 6, soldOnCredit: false, total: 50, paid: 0, balance: 50 }],
};

function renderPage(path = '/customers/receivables') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <ReceivablesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('ReceivablesPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue({ success: vi.fn(), error: vi.fn() } as unknown as ReturnType<typeof useToast>);
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions: ['ManageCustomers'] } } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'Tiendita' }, capabilities: TenantCapability.Retail } as unknown as ReturnType<typeof useTenant>);
    request.mockImplementation(((_method: string, path: string) =>
      Promise.resolve(path === '/api/v1/customers/receivables' ? RECEIVABLES : LUIS_ACCOUNT)) as never);
  });

  it('lists who owes, with the total, their limit and who passed it', async () => {
    renderPage();

    const ana = (await screen.findByText('Ana López')).closest('tr')!;
    expect(within(ana).getByText('$200.00')).toBeInTheDocument();
    expect(within(ana).getByText('Pasó su límite')).toBeInTheDocument();
    expect(within(screen.getByText('Luis Pérez').closest('tr')!).getByText('Sin fiado')).toBeInTheDocument();
    expect(screen.getByText('$250.00')).toBeInTheDocument();
    expect(screen.getByText(/2 clientes/)).toBeInTheDocument();
  });

  it('opens the account of a customer without a record by their phone', async () => {
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Ver cuenta de Luis Pérez' }));

    expect(await screen.findByText('ORD-000020')).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('get', '/api/v1/customers/account', expect.objectContaining({
      query: { customerId: undefined, phone: '+525566667777' },
    }));
  });

  it('opens the account the customer card linked to', async () => {
    renderPage('/customers/receivables?cuenta=%2B525566667777');

    expect(await screen.findByRole('dialog', { name: 'Estado de cuenta' })).toBeInTheDocument();
    expect(await screen.findByText('ORD-000020')).toBeInTheDocument();
  });
});
