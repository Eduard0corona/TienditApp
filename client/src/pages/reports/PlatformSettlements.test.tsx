import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PlatformReceivable, PlatformSettlement } from '../../api/platformSettlements';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { OrderChannel, PlatformSettlementStatus, TenantCapability, type TenantProfileDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import PlatformSettlements from './PlatformSettlements';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

type Options = { query?: Record<string, unknown> };

function receivable(channel: OrderChannel, patch: Partial<PlatformReceivable> = {}): PlatformReceivable {
  return { channel, isActive: true, pendingOrders: 0, pendingAmount: 0, ...patch };
}

function settlement(patch: Partial<PlatformSettlement> = {}): PlatformSettlement {
  return {
    id: 's1',
    channel: OrderChannel.Rappi,
    status: PlatformSettlementStatus.Closed,
    periodFrom: '2026-09-28',
    periodTo: '2026-10-04',
    depositedOn: '2026-10-07',
    depositedAmount: 4812.4,
    reference: 'SPEI 4471',
    notes: null,
    ordersTotal: 4862.4,
    adjustmentsTotal: -50,
    expectedDeposit: 4812.4,
    difference: 0,
    orderCount: 12,
    revision: 3,
    createdAt: '2026-10-07T18:00:00Z',
    closedAt: '2026-10-07T18:30:00Z',
    ...patch,
  };
}

function page(items: PlatformSettlement[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

// Rappi owes 3 orders since the 5th and settled through the 4th; Uber Eats has a settlement open; DiDi Food is off but owes one.
const RECEIVABLES = [
  receivable(OrderChannel.Rappi, { pendingOrders: 3, pendingAmount: 1684.44, oldestPendingOn: '2026-10-05', settledThrough: '2026-10-04' }),
  receivable(OrderChannel.UberEats, { pendingOrders: 1, pendingAmount: 561.48, oldestPendingOn: '2026-10-06', openSettlementId: 'u1' }),
  receivable(OrderChannel.DidiFood, { isActive: false, pendingOrders: 1, pendingAmount: -348, oldestPendingOn: '2026-10-08' }),
];

function serve(receivables: PlatformReceivable[], settlements: (query: Record<string, unknown>) => PlatformSettlement[]) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options: Options = {}) => {
    if (method === 'get' && path === '/api/v1/platform-settlements/receivable') return Promise.resolve(receivables);
    if (method === 'get' && path === '/api/v1/platform-settlements') return Promise.resolve(page(settlements(options.query ?? {})));
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
}

function listCalls() {
  return vi.mocked(apiRequest).mock.calls
    .filter(([method, path]) => method === 'get' && path === '/api/v1/platform-settlements')
    .map(([, , options]) => (options as Options).query);
}

function signIn(role: string, permissions: string[] = []) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions } } as unknown as ReturnType<typeof useAuth>);
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MemoryRouter initialEntries={['/reports/settlements']}>
      <QueryClientProvider client={queryClient}>
        <PlatformSettlements />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

async function card(name: string) {
  return (await screen.findByRole('heading', { name, level: 3 })).closest('article') as HTMLElement;
}

describe('PlatformSettlements', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    setBusinessTimeZone('America/Mexico_City');
    signIn('TenantAdmin');
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita', timeZone: 'America/Mexico_City' } as TenantProfileDto,
      capabilities: TenantCapability.Food,
      isLoading: false,
      refreshTenant: vi.fn(),
    });
  });

  afterEach(() => {
    setBusinessTimeZone(null);
  });

  it('says what each platform owes and lets the owner register its deposit or continue the open one', async () => {
    serve(RECEIVABLES, () => []);
    renderPage();

    const rappi = await card('Rappi');
    expect(within(rappi).getByText('$1,684.44')).toBeInTheDocument();
    expect(within(rappi).getByText('3 pedidos pendientes desde el 5 oct.')).toBeInTheDocument();
    expect(within(rappi).getByText('Liquidado hasta el 4 oct.')).toBeInTheDocument();
    expect(within(rappi).getByRole('link', { name: 'Registrar depósito de Rappi' })).toHaveAttribute('href', '/reports/settlements/new?channel=3');

    const uber = await card('Uber Eats');
    expect(within(uber).getByText('Todavía sin liquidaciones cerradas.')).toBeInTheDocument();
    expect(within(uber).getByRole('link', { name: 'Continuar la liquidación abierta de Uber Eats' })).toHaveAttribute('href', '/reports/settlements/u1');
    expect(within(uber).queryByRole('link', { name: /Registrar depósito/ })).not.toBeInTheDocument();

    // Off, but it still owes: the courier paid the business and DiDi will take its commission.
    const didi = await card('DiDi Food');
    expect(within(didi).getByText('Apagada')).toBeInTheDocument();
    expect(within(didi).getByText('-$348.00')).toBeInTheDocument();
    expect(within(didi).getByText('1 pedido pendiente desde el 8 oct.')).toBeInTheDocument();
  });

  it('only shows to whoever reads the reports, without registering deposits', async () => {
    signIn('Staff', ['ViewReports']);
    serve(RECEIVABLES, () => [settlement({ status: PlatformSettlementStatus.Open })]);
    renderPage();

    const rappi = await card('Rappi');
    expect(within(rappi).queryByRole('link')).not.toBeInTheDocument();
    expect(within(await card('Uber Eats')).getByRole('link', { name: 'Ver la liquidación abierta de Uber Eats' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: /^Ver la liquidación de Rappi del 28 sept?\. al 4 oct\.$/ })).toHaveAttribute('href', '/reports/settlements/s1');
  });

  it('asks the owner to turn a platform on when none is active nor owes anything', async () => {
    serve([], () => []);
    renderPage();

    expect(await screen.findByText(/No tienes plataformas de reparto activas/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Configuración › Negocio › Plataformas de reparto' })).toHaveAttribute('href', '/settings/business');
    expect(await screen.findByText('Todavía no registras depósitos de las plataformas.')).toBeInTheDocument();
  });

  it('lists the settlements with what was deposited and the difference, and filters and searches them on the server', async () => {
    const user = userEvent.setup();
    serve(RECEIVABLES, (query) => (query.search === 'spei'
      ? [settlement()]
      : [settlement({ id: 's2', status: PlatformSettlementStatus.Open, periodFrom: '2026-10-05', periodTo: '2026-10-11', depositedAmount: 1634.44, difference: -50, orderCount: 1 }), settlement()]));
    renderPage();

    const open = (await screen.findByRole('link', { name: 'Continuar la liquidación de Rappi del 5 al 11 oct.' })).closest('tr') as HTMLElement;
    expect(within(open).getByText('$1,634.44')).toBeInTheDocument();
    expect(within(open).getByText('-$50.00')).toBeInTheDocument();
    expect(within(open).getByText('Abierta')).toBeInTheDocument();
    expect(within(open).getByText('1 pedido')).toBeInTheDocument();
    expect(listCalls()[0]).toEqual({ channel: undefined, status: undefined, search: undefined, pageNumber: 1, pageSize: 20 });

    await user.selectOptions(screen.getByLabelText('Plataforma'), String(OrderChannel.UberEats));
    await user.selectOptions(screen.getByLabelText('Estado'), String(PlatformSettlementStatus.Closed));
    await waitFor(() => expect(listCalls().at(-1)).toMatchObject({ channel: OrderChannel.UberEats, status: PlatformSettlementStatus.Closed }));

    await user.type(screen.getByRole('searchbox', { name: 'Buscar referencia o notas' }), 'spei');
    await waitFor(() => expect(listCalls().at(-1)).toMatchObject({ search: 'spei', pageNumber: 1 }));
    await waitFor(() => expect(screen.queryByText('Continuar')).not.toBeInTheDocument());
  });
});
