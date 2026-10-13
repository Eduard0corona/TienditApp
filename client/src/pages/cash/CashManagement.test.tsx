import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { courierKeys } from '../../api/courierSettlements';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { DeliveryCourier, TenantCapability } from '../../types';
import CashManagement from './CashManagement';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useWorkingBranch', () => ({ useWorkingBranch: () => ({ branches: [], branch: null, setBranch: vi.fn() }) }));
vi.mock('./MyDrawer', () => ({ default: () => <p>Mi caja abierta</p> }));
vi.mock('./ShiftHistory', () => ({ default: () => <p>Turnos</p> }));
vi.mock('./DailyCut', () => ({ default: () => <p>Corte</p> }));
vi.mock('./CourierCash', () => ({
  default: ({ canSettle }: { canSettle: boolean }) => <p>{canSettle ? 'Repartidores: puede liquidar' : 'Repartidores: solo lee'}</p>,
}));

const receivable = (patch: Record<string, unknown> = {}) => ({
  courier: DeliveryCourier.Paquetenvia, owedOrders: 0, owedAmount: 0, onTheWayOrders: 0, onTheWayAmount: 0, lastSettledAt: null, ...patch,
});

/** With `alreadyRead`, what the couriers owe is in the cache from the first render: nothing left to wait for. */
function renderCash(permissions: string[], receivables: unknown[], alreadyRead = false) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'STAFF', permissions } } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Food } as unknown as ReturnType<typeof useTenant>);
  vi.mocked(apiRequest).mockImplementation(((_method: string, path: string) =>
    path === '/api/v1/courier-settlements/receivable' ? Promise.resolve(receivables) : Promise.reject(new Error(path))) as unknown as typeof apiRequest);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (alreadyRead) client.setQueryData(courierKeys.receivables, receivables);
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/cash']}>
        <CashManagement />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('CashManagement', () => {
  beforeEach(() => vi.resetAllMocks());

  it('shows Repartidores once a courier collected something, and lets whoever also charges settle it', async () => {
    renderCash(['ManageCash', 'ChargeOrders'], [receivable({ owedOrders: 1, owedAmount: 275 })]);

    await userEvent.click(await screen.findByRole('tab', { name: 'Repartidores' }));

    expect(screen.getByText('Repartidores: puede liquidar')).toBeInTheDocument();
  });

  it('shows Repartidores with only its history, read-only for whoever only handles the cash', async () => {
    renderCash(['ManageCash'], [receivable({ lastSettledAt: '2026-10-12T23:00:00Z' })]);

    await userEvent.click(await screen.findByRole('tab', { name: 'Repartidores' }));

    expect(screen.getByText('Repartidores: solo lee')).toBeInTheDocument();
  });

  it('leaves Repartidores out while no courier collected anything', async () => {
    renderCash(['ManageCash', 'ChargeOrders'], [receivable()], true);

    expect(await screen.findByText('Mi caja abierta')).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Repartidores' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('tab')).toHaveLength(3);
  });
});
