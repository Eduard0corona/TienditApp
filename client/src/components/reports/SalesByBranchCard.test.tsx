import { render, screen, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { reportsApi, type SalesByBranchReport } from '../../api/reportsApi';
import type { Schemas } from '../../api/typedClient';
import { useActiveBranches } from '../../hooks/useWorkingBranch';
import SalesByBranchCard from './SalesByBranchCard';

vi.mock('../../api/reportsApi', () => ({ reportsApi: { getSalesByBranch: vi.fn() } }));
vi.mock('../../hooks/useWorkingBranch', () => ({ useActiveBranches: vi.fn() }));

type Figures = Schemas['SalesFiguresDto'];

function figures(patch: Partial<Figures> = {}): Figures {
  return {
    ordersCreated: 52,
    paidOrders: 50,
    paymentsCount: 53,
    paymentsAmount: 4200,
    cashAmount: 1890,
    cardAmount: 1680,
    transferAmount: 420,
    walletAmount: 0,
    otherAmount: 210,
    platformAmount: 0,
    onlineAmount: 0,
    refundsCount: 1,
    refundsAmount: 63,
    netSales: 4137,
    averageTicket: 82.74,
    ...patch,
  };
}

function report(roma: Figures, coyoacan: Figures, totals: Figures): SalesByBranchReport {
  return {
    from: '2026-10-01',
    to: '2026-10-04',
    totals,
    branches: [
      { branchId: 'roma', branchName: 'Sucursal Roma Norte', figures: roma },
      { branchId: 'coyoacan', branchName: 'Sucursal Coyoacán', figures: coyoacan },
    ],
    days: [{ date: '2026-10-04', netSales: 4137, paymentsAmount: 4200, refundsAmount: 63, paidOrders: 50, ordersCreated: 52 }],
    snapshotDays: 3,
    liveDays: 1,
  };
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SalesByBranchCard />
    </QueryClientProvider>,
  );
}

describe('SalesByBranchCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useActiveBranches).mockReturnValue({ data: [] } as unknown as ReturnType<typeof useActiveBranches>);
  });

  it('keeps the columns it always had while no platform charged anything in the period', async () => {
    vi.mocked(reportsApi.getSalesByBranch).mockResolvedValue(report(figures(), figures(), figures({ paymentsAmount: 8400 })));
    renderCard();

    await screen.findByRole('table');
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Sucursal', 'Ventas netas', 'Tickets', 'Ticket promedio', 'Efectivo', 'Tarjeta', 'Transferencia', 'Otros', 'Devoluciones',
    ]);
  });

  it('adds what the platforms charged in a column of its own, apart from "Otros"', async () => {
    const roma = figures({ paymentsAmount: 5200, platformAmount: 1000, netSales: 5137 });
    vi.mocked(reportsApi.getSalesByBranch).mockResolvedValue(report(roma, figures(), figures({ paymentsAmount: 9400, platformAmount: 1000 })));
    renderCard();

    await screen.findByRole('table');
    const headers = screen.getAllByRole('columnheader').map((header) => header.textContent);
    expect(headers).toEqual([
      'Sucursal', 'Ventas netas', 'Tickets', 'Ticket promedio', 'Efectivo', 'Tarjeta', 'Transferencia', 'Otros', 'Plataformas', 'Devoluciones',
    ]);
    const romaRow = screen.getByRole('cell', { name: 'Sucursal Roma Norte' }).closest('tr') as HTMLElement;
    const cells = within(romaRow).getAllByRole('cell').map((cell) => cell.textContent);
    expect(cells[headers.indexOf('Otros')]).toBe('$210.00');
    expect(cells[headers.indexOf('Plataformas')]).toBe('$1,000.00');
    const coyoacanRow = screen.getByRole('cell', { name: 'Sucursal Coyoacán' }).closest('tr') as HTMLElement;
    expect(within(coyoacanRow).getAllByRole('cell').map((cell) => cell.textContent)[headers.indexOf('Plataformas')]).toBe('$0.00');
  });
});
