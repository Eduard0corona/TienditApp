import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Branch } from '../../api/branchesApi';
import { cashApi } from '../../api/cashApi';
import { useTenant } from '../../hooks/useTenant';
import { CashSessionStatus, CashTransactionType, type CashSessionDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import ShiftHistory from './ShiftHistory';

const toast = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn() }));
const refreshCashState = vi.hoisted(() => vi.fn());

vi.mock('../../api/cashApi', () => ({ cashApi: { getSessions: vi.fn(), getTransactions: vi.fn(), closeSession: vi.fn() } }));
vi.mock('../../api/queries', () => ({ useRefreshCashState: () => refreshCashState }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const branches = [
  { id: 'b1', name: 'Centro', isDefault: true },
  { id: 'b2', name: 'Playas', isDefault: false },
] as unknown as Branch[];

function shift(patch: Partial<CashSessionDto>): CashSessionDto {
  return {
    id: 's1', openedAt: '2026-10-09T14:00:00Z', openedBy: 'ana@cafelaesquina.mx', openedByName: 'Ana', openingAmount: 500,
    status: CashSessionStatus.Closed, inflows: 1000, outflows: 250, nonCashInflows: 0, expectedBalance: 1250, isMine: true,
    branchId: 'b1', branchName: 'Centro', cashCount: [],
    ...patch,
  };
}

/** Luis's drawer still open at Playas, Ana's that balanced and Marta's $16.00 short. */
const shifts = [
  shift({ id: 'open', openedByName: 'Luis', openedBy: 'luis@cafelaesquina.mx', isMine: false, status: CashSessionStatus.Open, expectedBalance: 840, branchName: 'Playas' }),
  shift({ id: 'even', closedAt: '2026-10-09T22:00:00Z', closingAmount: 1250, expectedClosingAmount: 1250, closingDifference: 0 }),
  shift({
    id: 'short', openedByName: null, openedBy: 'marta@cafelaesquina.mx', isMine: false, closedAt: '2026-10-09T23:00:00Z',
    // What it should have held is frozen at the close: the live balance may have moved since.
    closedBy: 'ana@cafelaesquina.mx', closedByName: 'Ana', closingAmount: 1234, expectedBalance: 1290, expectedClosingAmount: 1250, closingDifference: -16,
    openingAmount: 300, inflows: 1180, outflows: 230, nonCashInflows: 75,
    closingNotes: 'Un cambio mal dado', cashCount: [{ denomination: 500, quantity: 2 }, { denomination: 200, quantity: 1 }, { denomination: 1, quantity: 34 }],
  }),
];

const listPage = <T,>(items: T[]) =>
  ({ data: { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false } }) as never;

function renderHistory({ canCloseOthers = true, refreshKey = 0 } = {}) {
  return render(<ShiftHistory branches={branches} canCloseOthers={canCloseOthers} refreshKey={refreshKey} />);
}

const row = async (text: string) => (await screen.findByText(text)).closest('tr') as HTMLElement;

describe('ShiftHistory', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // Not the zone of the team's machines (Mexico City): going back to the device's zone fails there too, not only in CI.
    setBusinessTimeZone('America/Cancun');
    vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'Café La Esquina' } } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(cashApi.getSessions).mockResolvedValue(listPage(shifts));
    vi.mocked(cashApi.getTransactions).mockResolvedValue(listPage([]));
  });

  afterEach(() => setBusinessTimeZone(null));

  it('lists each drawer with what it should have held, what was counted and how it came out', async () => {
    renderHistory();

    const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell').map((cell) => cell.textContent);
    const open = await row('Luis');
    expect(open).toHaveTextContent('Abierta');
    expect(cells(open).slice(2)).toEqual(['Playas', '$840.00', '—', 'En curso']);

    const even = await row('Ana');
    expect(even).toHaveTextContent('Tú');
    expect(even).toHaveTextContent('Cuadró');
    const short = await row('marta@cafelaesquina.mx');
    expect(cells(short).slice(2)).toEqual(['Centro', '$1,250.00', '$1,234.00', 'Faltan $16.00']);
    expect(cashApi.getSessions).toHaveBeenCalledWith({
      fromDate: undefined, toDate: undefined, branchId: undefined, onlyWithDifference: undefined, pageNumber: 1, pageSize: 10,
    });
  });

  it('filters by the days of the business, the branch and the drawers that did not balance', async () => {
    const user = userEvent.setup();
    renderHistory();
    await row('Luis');

    await user.type(screen.getByLabelText('Desde'), '2026-10-01');
    await user.type(screen.getByLabelText('Hasta'), '2026-10-05');
    await user.selectOptions(screen.getByLabelText('Sucursal'), 'b2');
    await user.click(screen.getByLabelText('Solo con faltante o sobrante'));

    // Cancún is UTC-5 all year: its day starts at 05:00 UTC and ends at 04:59:59 UTC of the next.
    await waitFor(() => expect(cashApi.getSessions).toHaveBeenLastCalledWith({
      fromDate: '2026-10-01T05:00:00.000Z', toDate: '2026-10-06T04:59:59.000Z', branchId: 'b2', onlyWithDifference: true,
      pageNumber: 1, pageSize: 10,
    }));
    expect(screen.getByRole('button', { name: 'Filtros (4)' })).toBeInTheDocument();
  });

  it('opens a closed shift with its figures, the note and count of its close and its movements', async () => {
    const user = userEvent.setup();
    vi.mocked(cashApi.getTransactions).mockResolvedValue(listPage([
      { id: 't1', cashSessionId: 'short', transactionType: CashTransactionType.Expense, amount: -250, occurredAt: '2026-10-09T16:00:00Z', reference: 'Gas' },
    ]));
    renderHistory();

    await user.click(within(await row('marta@cafelaesquina.mx')).getByRole('button'));
    const detail = await screen.findByRole('dialog', { name: /Turno de marta@cafelaesquina\.mx/ });
    expect(cashApi.getTransactions).toHaveBeenCalledWith({ cashSessionId: 'short', pageSize: 200 });
    expect(detail).toHaveTextContent('Fondo inicial$300.00Entró en efectivo$1,180.00Salió en efectivo$230.00Tarjeta y otros$75.00');
    expect(detail).toHaveTextContent('Debía haber$1,250.00');
    expect(detail).toHaveTextContent('Contado (Ana)$1,234.00');
    expect(detail).toHaveTextContent('DiferenciaFaltan $16.00');
    expect(detail).toHaveTextContent('Nota del cierre: Un cambio mal dado');
    expect(detail).toHaveTextContent('Conteo: 2 × $500.00, 1 × $200.00, 34 × $1.00');
    expect(await within(detail).findByText(/Gasto · Gas/)).toBeInTheDocument();
    expect(within(detail).getByText('−$250.00')).toBeInTheDocument();
    expect(within(detail).getByRole('button', { name: 'Imprimir corte' })).toBeInTheDocument();
    expect(within(detail).queryByRole('button', { name: 'Cerrar esta caja' })).not.toBeInTheDocument();
  });

  it('lets an owner close the drawer another cashier left open, and reads the shifts again', async () => {
    const user = userEvent.setup();
    vi.mocked(cashApi.closeSession).mockResolvedValue({ data: {} } as never);
    renderHistory();

    await user.click(within(await row('Luis')).getByRole('button'));
    const detail = await screen.findByRole('dialog', { name: /Turno de Luis/ });
    expect(within(detail).getByRole('button', { name: 'Imprimir corte parcial' })).toBeInTheDocument();
    await user.click(within(detail).getByRole('button', { name: 'Cerrar esta caja' }));

    const close = await screen.findByRole('dialog', { name: 'Cerrar caja' });
    for (const [label, quantity] of [['$500.00', '1'], ['$200.00', '1'], ['$100.00', '1'], ['$20.00', '1']]) {
      await user.type(within(close).getByLabelText(label), quantity);
    }
    expect(within(close).getByText('Faltan $20.00')).toBeInTheDocument();
    await user.type(within(close).getByLabelText('Notas (explica la diferencia)'), 'Faltó un billete');
    await user.click(within(close).getByRole('button', { name: 'Cerrar caja' }));

    expect(cashApi.closeSession).toHaveBeenCalledWith('open', {
      closingAmount: 820, notes: 'Faltó un billete',
      cashCount: [{ denomination: 500, quantity: 1 }, { denomination: 200, quantity: 1 }, { denomination: 100, quantity: 1 }, { denomination: 20, quantity: 1 }],
    });
    expect(toast.success).toHaveBeenCalledWith('Caja de Luis cerrada.');
    expect(refreshCashState).toHaveBeenCalled();
    await waitFor(() => expect(cashApi.getSessions).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole('dialog', { name: 'Cerrar caja' })).not.toBeInTheDocument();
  });

  it('offers to close another drawer only to whoever may, and never their own one', async () => {
    const user = userEvent.setup();
    vi.mocked(cashApi.getSessions).mockResolvedValue(listPage([shifts[0], shift({ id: 'mine', status: CashSessionStatus.Open, openedByName: 'Ana' })]));
    const { unmount } = renderHistory({ canCloseOthers: false });

    await user.click(within(await row('Luis')).getByRole('button'));
    expect(within(await screen.findByRole('dialog', { name: /Turno de Luis/ })).queryByRole('button', { name: 'Cerrar esta caja' })).not.toBeInTheDocument();
    unmount();

    renderHistory();
    await user.click(within(await row('Ana')).getByRole('button'));
    expect(within(await screen.findByRole('dialog', { name: /Turno de Ana/ })).queryByRole('button', { name: 'Cerrar esta caja' })).not.toBeInTheDocument();
  });

  it('reads the shifts again when a drawer opens or closes elsewhere on the page', async () => {
    const { rerender } = renderHistory();
    await row('Luis');
    expect(cashApi.getSessions).toHaveBeenCalledTimes(1);

    rerender(<ShiftHistory branches={branches} canCloseOthers refreshKey={1} />);
    await waitFor(() => expect(cashApi.getSessions).toHaveBeenCalledTimes(2));
  });

  it('says when the shifts could not be read, and reads them again on retry', async () => {
    const user = userEvent.setup();
    vi.mocked(cashApi.getSessions).mockRejectedValueOnce(new Error('Network Error')).mockResolvedValueOnce(listPage(shifts));
    renderHistory();

    expect(await screen.findByText('No se pudo cargar el historial de turnos.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await row('Luis')).toBeInTheDocument();
    expect(screen.queryByText('No se pudo cargar el historial de turnos.')).not.toBeInTheDocument();
  });
});
