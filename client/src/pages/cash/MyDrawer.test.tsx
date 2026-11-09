import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Branch } from '../../api/branchesApi';
import { cashApi } from '../../api/cashApi';
import { useTenant } from '../../hooks/useTenant';
import { CashSessionStatus, CashTransactionType, OrderPaymentMethod, type CashSessionDto, type CashTransactionDto } from '../../types';
import MyDrawer from './MyDrawer';

const toast = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn() }));
const refreshCashState = vi.hoisted(() => vi.fn());

vi.mock('../../api/cashApi', () => ({
  cashApi: { getCurrentSession: vi.fn(), getTransactions: vi.fn(), openSession: vi.fn(), closeSession: vi.fn(), addTransaction: vi.fn() },
}));
vi.mock('../../api/queries', () => ({ useRefreshCashState: () => refreshCashState }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const branches = [
  { id: 'b1', name: 'Centro', isDefault: true },
  { id: 'b2', name: 'Playas', isDefault: false },
] as unknown as Branch[];

/**
 * Ana's drawer at Centro: $500 to start, $1,030 in cash and $250 out. What should be in it is what the server says
 * ($1,250: a deposit of the customer's balance does not count here), never a sum made on the screen.
 */
function drawer(patch: Partial<CashSessionDto> = {}): CashSessionDto {
  return {
    id: 's1', openedAt: '2026-10-09T14:00:00Z', openedBy: 'ana@cafelaesquina.mx', openedByName: 'Ana', openingAmount: 500,
    status: CashSessionStatus.Open, inflows: 1030, outflows: 250, nonCashInflows: 400, expectedBalance: 1250, isMine: true,
    inventoryLocationId: 'l1', branchId: 'b1', branchName: 'Centro', cashCount: [],
    ...patch,
  };
}

const page = (items: CashTransactionDto[] = []) =>
  ({ data: { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false } }) as never;

function renderDrawer(current: CashSessionDto | null) {
  vi.mocked(cashApi.getCurrentSession).mockResolvedValue({ data: current } as never);
  const onBranchChosen = vi.fn();
  const onChanged = vi.fn();
  render(<MyDrawer branches={branches} workingBranchId="b2" onBranchChosen={onBranchChosen} onChanged={onChanged} />);
  return { onBranchChosen, onChanged };
}

/** What a KPI of the open drawer shows: its label and amount. */
const kpi = (label: string) => screen.getByText(label).parentElement as HTMLElement;

describe('MyDrawer', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'Café La Esquina' } } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(cashApi.getTransactions).mockResolvedValue(page());
  });

  it('opens the drawer at the branch the device works at, with its fund and notes', async () => {
    const user = userEvent.setup();
    const { onBranchChosen, onChanged } = renderDrawer(null);
    vi.mocked(cashApi.openSession).mockResolvedValue({ data: drawer({ openingAmount: 350.5, inflows: 0, outflows: 0, nonCashInflows: 0, expectedBalance: 350.5, branchId: 'b2', branchName: 'Playas' }) } as never);

    expect(await screen.findByRole('heading', { name: 'Abre tu caja' })).toBeInTheDocument();
    expect(screen.getByLabelText('Sucursal')).toHaveValue('b2');
    await user.type(screen.getByLabelText('Fondo inicial'), '350.50');
    await user.type(screen.getByLabelText('Notas (opcional)'), 'Turno de la mañana');
    await user.click(screen.getByRole('button', { name: 'Abrir mi caja' }));

    expect(cashApi.openSession).toHaveBeenCalledWith({ openingAmount: 350.5, notes: 'Turno de la mañana', branchId: 'b2' });
    expect(await screen.findByRole('button', { name: 'Cerrar caja' })).toBeInTheDocument();
    expect(kpi('Debe haber en caja')).toHaveTextContent('$350.50');
    expect(onBranchChosen).toHaveBeenCalledWith('b2');
    expect(onChanged).toHaveBeenCalledTimes(1);
    expect(refreshCashState).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith('Tu caja quedó abierta.');
  });

  it('opens with no fund when the field is left empty, and refuses a fund with more than two decimals', async () => {
    const user = userEvent.setup();
    renderDrawer(null);
    vi.mocked(cashApi.openSession).mockResolvedValue({ data: drawer({ openingAmount: 0 }) } as never);

    await user.type(await screen.findByLabelText('Fondo inicial'), '12.345');
    await user.click(screen.getByRole('button', { name: 'Abrir mi caja' }));
    expect(screen.getByText('Escribe el fondo inicial, por ejemplo 500 o 350.50.')).toBeInTheDocument();
    expect(cashApi.openSession).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText('Fondo inicial'));
    await user.selectOptions(screen.getByLabelText('Sucursal'), 'b1');
    await user.click(screen.getByRole('button', { name: 'Abrir mi caja' }));
    expect(cashApi.openSession).toHaveBeenCalledWith({ openingAmount: 0, notes: undefined, branchId: 'b1' });
  });

  it('shows what the drawer should hold and its movements, each with its sign', async () => {
    vi.mocked(cashApi.getTransactions).mockResolvedValue(page([
      { id: 't1', cashSessionId: 's1', transactionType: CashTransactionType.SalePayment, amount: 180, occurredAt: '2026-10-09T15:00:00Z', paymentMethod: OrderPaymentMethod.Cash, reference: 'ORD-000123' },
      { id: 't2', cashSessionId: 's1', transactionType: CashTransactionType.Expense, amount: -250, occurredAt: '2026-10-09T16:00:00Z', reference: 'Gas', notes: 'Tanque', createdBy: 'ana@cafelaesquina.mx' },
    ]));
    renderDrawer(drawer());

    expect(await screen.findByText(/Tu caja · Centro · abierta/)).toBeInTheDocument();
    expect(kpi('Fondo inicial')).toHaveTextContent('$500.00');
    expect(kpi('Entró en efectivo')).toHaveTextContent('$1,030.00');
    expect(kpi('Salió en efectivo')).toHaveTextContent('$250.00');
    expect(kpi('Tarjeta y otros')).toHaveTextContent('$400.00');
    expect(kpi('Debe haber en caja')).toHaveTextContent('$1,250.00');
    expect(cashApi.getTransactions).toHaveBeenCalledWith({ cashSessionId: 's1', search: undefined, pageNumber: 1, pageSize: 10 });

    const sale = (await screen.findByText('ORD-000123')).closest('tr') as HTMLElement;
    expect(sale).toHaveTextContent('Pago de venta');
    expect(sale).toHaveTextContent('Efectivo');
    expect(sale).toHaveTextContent('+$180.00');
    const expense = screen.getByText('Gas').closest('tr') as HTMLElement;
    expect(expense).toHaveTextContent('−$250.00');
    expect(expense).toHaveTextContent('Tanque · ana@cafelaesquina.mx');
  });

  it('says it is the drawer of whoever opened it when it is not mine', async () => {
    renderDrawer(drawer({ isMine: false, openedByName: 'Luis', branchName: null, inventoryLocationName: 'Barra' }));

    expect(await screen.findByText(/Caja de Luis · Barra · abierta/)).toBeInTheDocument();
  });

  it('records a withdrawal as a positive amount, the server applying its sign, and reads the drawer again', async () => {
    const user = userEvent.setup();
    renderDrawer(drawer());
    vi.mocked(cashApi.addTransaction).mockResolvedValue({ data: {} } as never);

    await user.click(await screen.findByRole('button', { name: 'Gasto, retiro o depósito' }));
    const dialog = await screen.findByRole('dialog', { name: 'Registrar movimiento' });
    expect(within(dialog).getByRole('radio', { name: 'Gasto' })).toHaveAttribute('aria-checked', 'true');
    await user.click(within(dialog).getByRole('radio', { name: 'Retiro' }));

    await user.type(within(dialog).getByLabelText('Monto'), '0');
    await user.click(within(dialog).getByRole('button', { name: 'Registrar' }));
    expect(within(dialog).getByText('Escribe un monto mayor a 0, con máximo 2 decimales.')).toBeInTheDocument();
    expect(cashApi.addTransaction).not.toHaveBeenCalled();

    await user.clear(within(dialog).getByLabelText('Monto'));
    await user.type(within(dialog).getByLabelText('Monto'), '1,200');
    await user.type(within(dialog).getByLabelText('Referencia (opcional)'), ' Depósito al banco ');
    await user.click(within(dialog).getByRole('button', { name: 'Registrar' }));

    expect(cashApi.addTransaction).toHaveBeenCalledWith({
      cashSessionId: 's1', transactionType: CashTransactionType.Withdrawal, amount: 1200, inventoryLocationId: 'l1',
      reference: 'Depósito al banco', notes: undefined,
    });
    expect(toast.success).toHaveBeenCalledWith('Retiro registrado.');
    await waitFor(() => expect(cashApi.getCurrentSession).toHaveBeenCalledTimes(2));
    expect(refreshCashState).toHaveBeenCalled();
  });

  it('closes counting the bills, and keeps the cut at hand to print or send', async () => {
    const user = userEvent.setup();
    const { onChanged } = renderDrawer(drawer());
    vi.mocked(cashApi.closeSession).mockResolvedValue({
      data: drawer({ status: CashSessionStatus.Closed, closingAmount: 1250, expectedClosingAmount: 1250, closingDifference: 0 }),
    } as never);

    await user.click(await screen.findByRole('button', { name: 'Cerrar caja' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar caja' });
    await user.type(within(dialog).getByLabelText('$500.00'), '2');
    await user.type(within(dialog).getByLabelText('$200.00'), '1');
    await user.type(within(dialog).getByLabelText('$50.00'), '1');
    expect(within(dialog).getByText('Cuadró')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar caja' }));

    expect(cashApi.closeSession).toHaveBeenCalledWith('s1', {
      closingAmount: 1250, notes: undefined,
      cashCount: [{ denomination: 500, quantity: 2 }, { denomination: 200, quantity: 1 }, { denomination: 50, quantity: 1 }],
    });
    expect(toast.success).toHaveBeenCalledWith('Caja cerrada: cuadró exacto.');
    const cut = (await screen.findByRole('heading', { name: 'Corte de tu caja' })).closest('.card') as HTMLElement;
    expect(cut).toHaveTextContent('Debía haber $1,250.00 · Contado $1,250.00 · Cuadró');
    expect(within(cut).getByRole('button', { name: 'Imprimir corte' })).toBeInTheDocument();
    expect(within(cut).getByRole('link', { name: 'Enviar por WhatsApp' })).toHaveAttribute('href', expect.stringContaining('wa.me'));
    // The movements of the closed drawer come for its printed cut.
    expect(cashApi.getTransactions).toHaveBeenLastCalledWith({ cashSessionId: 's1', pageSize: 200 });
    expect(onChanged).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('heading', { name: 'Abre tu caja' })).toBeInTheDocument();

    await user.click(within(cut).getByRole('button', { name: 'Listo' }));
    expect(screen.queryByRole('heading', { name: 'Corte de tu caja' })).not.toBeInTheDocument();
  });

  it('asks why when the count is short, and closes with the total typed and its note', async () => {
    const user = userEvent.setup();
    renderDrawer(drawer());
    vi.mocked(cashApi.closeSession).mockResolvedValue({
      data: drawer({ status: CashSessionStatus.Closed, closingAmount: 1234, expectedClosingAmount: 1250, closingDifference: -16 }),
    } as never);

    await user.click(await screen.findByRole('button', { name: 'Cerrar caja' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar caja' });
    // Bills counted first and then the total typed: the total wins and no count by denomination is sent.
    await user.type(within(dialog).getByLabelText('$1,000.00'), '1');
    await user.click(within(dialog).getByRole('button', { name: 'Escribir el total' }));
    await user.type(within(dialog).getByLabelText('Efectivo contado'), '1234');
    expect(within(dialog).getByText('Faltan $16.00')).toBeInTheDocument();
    expect(within(dialog).getByText('Para cerrar con diferencia escribe qué pasó.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cerrar caja' })).toBeDisabled();

    await user.type(within(dialog).getByLabelText('Notas (explica la diferencia)'), 'Un cambio mal dado');
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar caja' }));

    expect(cashApi.closeSession).toHaveBeenCalledWith('s1', { closingAmount: 1234, notes: 'Un cambio mal dado', cashCount: undefined });
    expect(toast.warning).toHaveBeenCalledWith('Caja cerrada con faltante de $16.00.');
    const cut = (await screen.findByRole('heading', { name: 'Corte de tu caja' })).closest('.card') as HTMLElement;
    expect(cut).toHaveTextContent('Debía haber $1,250.00 · Contado $1,234.00 · Faltan $16.00');
    expect(screen.queryByRole('dialog', { name: 'Cerrar caja' })).not.toBeInTheDocument();
  });

  it('counts the coins of fifty cents in cents, and closes even with the count by denomination', async () => {
    const user = userEvent.setup();
    renderDrawer(drawer({ expectedBalance: 1249.5 }));
    vi.mocked(cashApi.closeSession).mockResolvedValue({
      data: drawer({ status: CashSessionStatus.Closed, closingAmount: 1249.5, expectedClosingAmount: 1249.5, closingDifference: 0 }),
    } as never);

    await user.click(await screen.findByRole('button', { name: 'Cerrar caja' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar caja' });
    for (const [label, quantity] of [['$500.00', '2'], ['$200.00', '1'], ['$20.00', '2'], ['$5.00', '1'], ['$2.00', '2'], ['$0.50', '1']]) {
      await user.type(within(dialog).getByLabelText(label), quantity);
    }
    expect(within(dialog).getByText('Cuadró')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar caja' }));

    expect(cashApi.closeSession).toHaveBeenCalledWith('s1', {
      closingAmount: 1249.5, notes: undefined,
      cashCount: [
        { denomination: 500, quantity: 2 }, { denomination: 200, quantity: 1 }, { denomination: 20, quantity: 2 },
        { denomination: 5, quantity: 1 }, { denomination: 2, quantity: 2 }, { denomination: 0.5, quantity: 1 },
      ],
    });
  });

  it('balances a total typed with cents without asking for a note', async () => {
    const user = userEvent.setup();
    renderDrawer(drawer({ expectedBalance: 1250.35 }));
    vi.mocked(cashApi.closeSession).mockResolvedValue({
      data: drawer({ status: CashSessionStatus.Closed, closingAmount: 1250.35, expectedClosingAmount: 1250.35, closingDifference: 0 }),
    } as never);

    await user.click(await screen.findByRole('button', { name: 'Cerrar caja' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar caja' });
    await user.click(within(dialog).getByRole('button', { name: 'Escribir el total' }));
    await user.type(within(dialog).getByLabelText('Efectivo contado'), '1250.35');
    expect(within(dialog).getByText('Cuadró')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Notas (opcional)')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar caja' }));

    expect(cashApi.closeSession).toHaveBeenCalledWith('s1', { closingAmount: 1250.35, notes: undefined, cashCount: undefined });
  });

  it('asks why when there is cash over too, and says it as over in the notice and the cut', async () => {
    const user = userEvent.setup();
    renderDrawer(drawer());
    vi.mocked(cashApi.closeSession).mockResolvedValue({
      data: drawer({ status: CashSessionStatus.Closed, closingAmount: 1260, expectedClosingAmount: 1250, closingDifference: 10 }),
    } as never);

    await user.click(await screen.findByRole('button', { name: 'Cerrar caja' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar caja' });
    await user.click(within(dialog).getByRole('button', { name: 'Escribir el total' }));
    await user.type(within(dialog).getByLabelText('Efectivo contado'), '1260');
    expect(within(dialog).getByText('Sobran $10.00')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cerrar caja' })).toBeDisabled();
    await user.type(within(dialog).getByLabelText('Notas (explica la diferencia)'), 'Propina sin registrar');
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar caja' }));

    expect(cashApi.closeSession).toHaveBeenCalledWith('s1', { closingAmount: 1260, notes: 'Propina sin registrar', cashCount: undefined });
    expect(toast.warning).toHaveBeenCalledWith('Caja cerrada con sobrante de $10.00.');
    const cut = (await screen.findByRole('heading', { name: 'Corte de tu caja' })).closest('.card') as HTMLElement;
    expect(cut).toHaveTextContent('Debía haber $1,250.00 · Contado $1,260.00 · Sobran $10.00');
  });

  it('keeps the drawer open and says why when closing fails', async () => {
    const user = userEvent.setup();
    renderDrawer(drawer());
    vi.mocked(cashApi.closeSession).mockRejectedValue({ isAxiosError: true, response: { status: 409, data: { error: 'La caja ya estaba cerrada.' } } });

    await user.click(await screen.findByRole('button', { name: 'Cerrar caja' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar caja' });
    await user.click(within(dialog).getByRole('button', { name: 'Escribir el total' }));
    await user.type(within(dialog).getByLabelText('Efectivo contado'), '1250');
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar caja' }));

    expect(await within(dialog).findByText('La caja ya estaba cerrada.')).toBeInTheDocument();
    // The cashier can try again.
    expect(within(dialog).getByRole('button', { name: 'Cerrar caja' })).toBeEnabled();
    expect(toast.success).not.toHaveBeenCalled();
    expect(screen.queryByRole('heading', { name: 'Abre tu caja' })).not.toBeInTheDocument();
  });

  it('says when the drawer could not be read, and reads it again on retry', async () => {
    const user = userEvent.setup();
    vi.mocked(cashApi.getCurrentSession)
      .mockRejectedValueOnce(new Error('Network Error'))
      .mockResolvedValueOnce({ data: drawer() } as never);
    render(<MyDrawer branches={branches} onBranchChosen={vi.fn()} onChanged={vi.fn()} />);

    expect(await screen.findByText('No se pudo cargar tu caja.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByRole('button', { name: 'Cerrar caja' })).toBeInTheDocument();
  });
});
