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
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn() }) }));
vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));

const request = vi.mocked(apiRequest);

const RECEIVABLES = {
  totalOwed: 250,
  totalOverdue: 0,
  creditTermDays: null,
  page: {
    items: [
      { phone: '+525512345678', name: 'Ana López', customerId: 'c1', owed: 200, orders: 2, since: '2026-10-10T18:00:00Z', creditLimit: 150, overLimit: true, overdue: 0 },
      { phone: '+525566667777', name: 'Luis Pérez', customerId: null, owed: 50, orders: 1, since: '2026-10-18T18:00:00Z', creditLimit: null, overLimit: false, overdue: 0 },
    ],
    pageNumber: 1, totalPages: 1, totalCount: 2, hasPreviousPage: false, hasNextPage: false,
  },
};

const LUIS_ACCOUNT = {
  phone: '+525566667777', name: 'Luis Pérez', customerId: null, creditLimit: null, sellsOnCredit: false, owed: 50, available: null,
  overLimit: false, ordersCount: 1, recentPayments: [], recentWriteOffs: [], creditTermDays: null, overdue: 0,
  orders: [{
    orderId: 'o9', orderNumber: 'ORD-000020', createdAt: '2026-10-18T18:00:00Z', status: 6, soldOnCredit: false, total: 50, paid: 0,
    writtenOff: 0, balance: 50, dueOn: null, isOverdue: false,
  }],
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

  it('shows what is overdue with a term, and lists only who has something overdue', async () => {
    request.mockImplementation(((_method: string, path: string) => Promise.resolve(path === '/api/v1/customers/receivables'
      ? {
        ...RECEIVABLES,
        creditTermDays: 15,
        totalOverdue: 120,
        page: { ...RECEIVABLES.page, items: [{ ...RECEIVABLES.page.items[0], overdue: 120 }, RECEIVABLES.page.items[1]] },
      }
      : LUIS_ACCOUNT)) as never);
    renderPage();

    const ana = (await screen.findByText('Ana López')).closest('tr')!;
    expect(within(ana).getByText('$120.00')).toBeInTheDocument();
    expect(screen.getByText('Plazo de 15 días')).toBeInTheDocument();
    // Looking after customers is not setting the term.
    expect(screen.queryByRole('button', { name: 'Cambiar plazo' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByLabelText('Solo vencidos'));

    expect(request).toHaveBeenLastCalledWith('get', '/api/v1/customers/receivables', expect.objectContaining({
      query: expect.objectContaining({ overdueOnly: true, pageNumber: 1 }),
    }));
  });

  it('lets an owner set the term of the fiado', async () => {
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
    request.mockImplementation(((method: string, path: string) => Promise.resolve(method === 'put'
      ? { creditTermDays: 30 }
      : path === '/api/v1/customers/receivables' ? RECEIVABLES : LUIS_ACCOUNT)) as never);
    renderPage();

    expect(await screen.findByText('Sin plazo')).toBeInTheDocument();
    expect(screen.queryByLabelText('Solo vencidos')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Poner plazo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Plazo del fiado' });
    await userEvent.type(within(dialog).getByLabelText('Te pagan en'), '121');
    expect(within(dialog).getByText('El plazo del fiado va de 1 a 120 días.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Guardar plazo' })).toBeDisabled();
    await userEvent.clear(within(dialog).getByLabelText('Te pagan en'));
    await userEvent.type(within(dialog).getByLabelText('Te pagan en'), '30');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar plazo' }));

    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/credit-term', { body: { creditTermDays: 30 } });
  });

  it('takes «Solo vencidos» off when the owner takes the term away, so the list does not stay empty', async () => {
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
    let term: number | null = 15;
    request.mockImplementation(((method: string, path: string, options?: { body?: { creditTermDays: number | null } }) => {
      if (method === 'put') {
        term = options?.body?.creditTermDays ?? null;
        return Promise.resolve({ creditTermDays: term });
      }
      return Promise.resolve(path === '/api/v1/customers/receivables' ? { ...RECEIVABLES, creditTermDays: term } : LUIS_ACCOUNT);
    }) as never);
    renderPage();

    await userEvent.click(await screen.findByLabelText('Solo vencidos'));
    await userEvent.click(screen.getByRole('button', { name: 'Cambiar plazo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Plazo del fiado' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Quitar plazo' }));

    expect(request).toHaveBeenCalledWith('put', '/api/v1/customers/credit-term', { body: { creditTermDays: null } });
    expect(await screen.findByText('Sin plazo')).toBeInTheDocument();
    expect(screen.queryByLabelText('Solo vencidos')).not.toBeInTheDocument();
    expect(request).toHaveBeenLastCalledWith('get', '/api/v1/customers/receivables', expect.objectContaining({
      query: expect.objectContaining({ overdueOnly: undefined }),
    }));
    expect(await screen.findByText('Ana López')).toBeInTheDocument();
  });

  it('says why the server refused the term, and keeps it to try again', async () => {
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
    request.mockImplementation(((method: string, path: string) => method === 'put'
      ? Promise.reject({ isAxiosError: true, response: { status: 400, data: { detail: 'El plazo del fiado va de 1 a 120 días.' } } })
      : Promise.resolve(path === '/api/v1/customers/receivables' ? RECEIVABLES : LUIS_ACCOUNT)) as never);
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Poner plazo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Plazo del fiado' });
    await userEvent.type(within(dialog).getByLabelText('Te pagan en'), '30');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar plazo' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El plazo del fiado va de 1 a 120 días.');
    expect(within(dialog).getByLabelText('Te pagan en')).toHaveValue('30');
  });

  it('does not let whoever only charges set the term', async () => {
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions: ['ManageOrders', 'ChargeOrders'] } } as unknown as ReturnType<typeof useAuth>);
    renderPage();

    expect(await screen.findByText('Ana López')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /plazo/ })).not.toBeInTheDocument();
  });

  it('opens the account the customer card linked to', async () => {
    renderPage('/customers/receivables?cuenta=%2B525566667777');

    expect(await screen.findByRole('dialog', { name: 'Estado de cuenta' })).toBeInTheDocument();
    expect(await screen.findByText('ORD-000020')).toBeInTheDocument();
  });
});
