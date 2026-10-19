import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reportsApi, type ChannelSales, type DeliveryPlatformOrder, type SalesByChannelReport } from '../../api/reportsApi';
import { useActiveBranches } from '../../hooks/useWorkingBranch';
import { OrderChannel, OrderStatus, PlatformCollectedBy } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import SalesByChannelCard from './SalesByChannelCard';

vi.mock('../../api/reportsApi', () => ({ reportsApi: { getSalesByChannel: vi.fn(), getDeliveryPlatformOrders: vi.fn() } }));
vi.mock('../../hooks/useWorkingBranch', () => ({ useActiveBranches: vi.fn() }));

const COLUMNS = ['Canal', 'Pedidos', 'Cobrado', 'Devoluciones', 'Venta neta', 'Comisión e IVA', 'Retenciones', 'Neto esperado'];

function sales(patch: Partial<ChannelSales>): ChannelSales {
  return {
    isDeliveryPlatform: false,
    paidOrders: 0,
    paymentsAmount: 0,
    refundsAmount: 0,
    netSales: 0,
    platformCollected: 0,
    businessCollected: 0,
    onlineCollected: 0,
    commission: 0,
    commissionTax: 0,
    isrWithheld: 0,
    vatWithheld: 0,
    expectedSettlement: 0,
    ...patch,
  };
}

// Two orders of $1,000 that Rappi charged (30 % on the total, ISR 2.5 %, half the IVA withheld), one of them given back,
// and $1,200 of DiDi Food the couriers paid the business: DiDi will take its commission from a deposit.
const COUNTER = sales({ channel: OrderChannel.Counter, paidOrders: 40, paymentsAmount: 9800, refundsAmount: 120, netSales: 9680 });
const WHATSAPP = sales({ channel: OrderChannel.WhatsApp, paidOrders: 6, paymentsAmount: 1450, netSales: 1450 });
const RAPPI = sales({
  channel: OrderChannel.Rappi,
  isDeliveryPlatform: true,
  paidOrders: 2,
  paymentsAmount: 2000,
  netSales: 2000,
  platformCollected: 2000,
  commission: 600,
  commissionTax: 96,
  isrWithheld: 43.1,
  vatWithheld: 137.94,
  expectedSettlement: 1122.96,
});
const DIDI = sales({
  channel: OrderChannel.DidiFood,
  isDeliveryPlatform: true,
  paidOrders: 2,
  paymentsAmount: 1200,
  netSales: 1200,
  businessCollected: 1200,
  commission: 360,
  commissionTax: 57.6,
  expectedSettlement: -417.6,
});
const TOTALS = sales({
  paidOrders: 50,
  paymentsAmount: 14450,
  refundsAmount: 120,
  netSales: 14330,
  platformCollected: 2000,
  businessCollected: 1200,
  commission: 960,
  commissionTax: 153.6,
  isrWithheld: 43.1,
  vatWithheld: 137.94,
  expectedSettlement: 705.36,
});

function report(patch: Partial<SalesByChannelReport> = {}): SalesByChannelReport {
  return { from: '2026-10-01', to: '2026-10-11', channels: [COUNTER, WHATSAPP, RAPPI, DIDI], totals: TOTALS, ...patch };
}

function order(patch: Partial<DeliveryPlatformOrder>): DeliveryPlatformOrder {
  return {
    orderId: 'o1',
    orderNumber: 'ORD-000123',
    externalOrderNumber: '152390',
    registeredAt: '2026-10-09T20:30:00Z',
    status: OrderStatus.Delivered,
    collectedBy: PlatformCollectedBy.Platform,
    orderTotal: 1000,
    commissionAndTax: 348,
    withheld: 90.52,
    expectedSettlement: 561.48,
    refundedAmount: 0,
    ...patch,
  };
}

function page(items: DeliveryPlatformOrder[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SalesByChannelCard />
    </QueryClientProvider>,
  );
}

/** The cells of a row of the table, by the channel it starts with. */
function cellsOf(name: string): string[] {
  const row = screen.getAllByRole('row').find((candidate) => within(candidate).queryAllByRole('cell')[0]?.textContent?.startsWith(name));
  if (!row) throw new Error(`No hay fila de ${name}`);
  return within(row).getAllByRole('cell').map((cell) => cell.textContent ?? '');
}

describe('SalesByChannelCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // Sunday the 11th at 21:00 in Mexico City, already Monday the 12th in UTC: the business's days decide.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-12T03:00:00Z'));
    setBusinessTimeZone('America/Mexico_City');
    vi.mocked(useActiveBranches).mockReturnValue({ data: [] } as unknown as ReturnType<typeof useActiveBranches>);
    vi.mocked(reportsApi.getSalesByChannel).mockResolvedValue(report());
  });

  afterEach(() => {
    vi.useRealTimers();
    setBusinessTimeZone(null);
  });

  it('opens on the month so far, in the business days, and says it is loading', async () => {
    renderCard();

    expect(screen.getByText('Cargando…')).toBeInTheDocument();
    expect(screen.getByLabelText('Desde')).toHaveValue('2026-10-01');
    expect(screen.getByLabelText('Hasta')).toHaveValue('2026-10-11');
    await screen.findByRole('table');
    expect(reportsApi.getSalesByChannel).toHaveBeenCalledWith({ from: '2026-10-01', to: '2026-10-11', branchId: undefined }, expect.anything());
    expect(screen.queryByText('Cargando…')).not.toBeInTheDocument();
  });

  it('shows each channel with a dash where it has no platform figures, and the total of the rows', async () => {
    renderCard();

    await screen.findByRole('table');
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(COLUMNS);
    expect(cellsOf('Mostrador')).toEqual(['Mostrador', '40', '$9,800.00', '$120.00', '$9,680.00', '—', '—', '—']);
    expect(cellsOf('WhatsApp')).toEqual(['WhatsApp', '6', '$1,450.00', '$0.00', '$1,450.00', '—', '—', '—']);
    // The commission with its IVA and both withholdings, as the server rounded each one.
    expect(cellsOf('Rappi')).toEqual(['RappiVer pedidos', '2', '$2,000.00', '$0.00', '$2,000.00', '$696.00', '$181.04', '$1,122.96']);
    expect(cellsOf('DiDi Food')).toEqual(['DiDi FoodVer pedidos', '2', '$1,200.00', '$0.00', '$1,200.00', '$417.60', '$0.00', '-$417.60']);
    expect(cellsOf('Total')).toEqual(['Total', '50', '$14,450.00', '$120.00', '$14,330.00', '$1,113.60', '$181.04', '$705.36']);
    expect(screen.getByRole('button', { name: 'Ver pedidos de Rappi' })).toHaveClass('btn-light', 'btn-sm');
    expect(screen.queryByRole('button', { name: /Ver pedidos de Mostrador/ })).not.toBeInTheDocument();
  });

  it('says what the business charged itself of a platform and what the expected deposit means', async () => {
    renderCard();

    expect(await screen.findByText('De DiDi Food cobraste tú $1,200.00: su comisión e IVA se descuentan de tu depósito.')).toBeInTheDocument();
    expect(screen.queryByText(/De Rappi cobraste tú/)).not.toBeInTheDocument();
    expect(screen.getByText(/^El neto esperado es lo que la plataforma debería depositarte/)).toBeInTheDocument();
    expect(screen.getByText(/^Por lo general las retenciones son pagos a cuenta de tus impuestos, no un costo\./)).toHaveTextContent(
      'la de Uber Eats va del lunes a las 4:00 al lunes siguiente a las 3:59',
    );
    // No main action of its own: the page already has one.
    expect(screen.queryAllByRole('button').filter((button) => button.classList.contains('btn-primary'))).toHaveLength(0);
  });

  it('leaves out the platform notes for a business without platforms', async () => {
    const counter = sales({ ...COUNTER });
    vi.mocked(reportsApi.getSalesByChannel).mockResolvedValue(report({ channels: [counter], totals: { ...counter, channel: undefined } }));
    renderCard();

    await screen.findByRole('table');
    expect(cellsOf('Total')).toEqual(['Total', '40', '$9,800.00', '$120.00', '$9,680.00', '—', '—', '—']);
    expect(screen.queryByText(/El neto esperado/)).not.toBeInTheDocument();
    expect(screen.queryByText(/se pagaron en línea/)).not.toBeInTheDocument();
  });

  it('says what the online menu was paid online, which is in Mercado Pago and not in the drawer (Fase 24I)', async () => {
    const online = sales({ channel: OrderChannel.Online, paidOrders: 3, paymentsAmount: 450, netSales: 450, onlineCollected: 290 });
    vi.mocked(reportsApi.getSalesByChannel).mockResolvedValue(report({ channels: [online], totals: { ...online, channel: undefined } }));
    renderCard();

    expect(await screen.findByText('De tu menú en línea se pagaron en línea $290.00: están en tu cuenta de Mercado Pago, no en tu caja.'))
      .toBeInTheDocument();
  });

  it('says when the period had no sales and when the report cannot be read', async () => {
    vi.mocked(reportsApi.getSalesByChannel).mockResolvedValue(report({ channels: [], totals: sales({}) }));
    const { unmount } = renderCard();

    expect(await screen.findByText('Sin ventas en el periodo.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    unmount();

    vi.mocked(reportsApi.getSalesByChannel).mockRejectedValue(apiError(500, 'server_error', 'Ocurrió un error inesperado.'));
    renderCard();

    expect(await screen.findByText('No se pudo cargar el reporte de ventas por canal.')).toHaveClass('alert-danger');
  });

  it('takes last week from Monday to Sunday in the business days, and goes back to the month', async () => {
    renderCard();
    await screen.findByRole('table');

    await userEvent.click(screen.getByRole('button', { name: 'Semana pasada' }));

    // Today is Sunday the 11th for the business (Monday the 12th in UTC): last week ran from the 28th to the 4th.
    expect(screen.getByLabelText('Desde')).toHaveValue('2026-09-28');
    expect(screen.getByLabelText('Hasta')).toHaveValue('2026-10-04');
    await waitFor(() => expect(reportsApi.getSalesByChannel).toHaveBeenLastCalledWith(
      { from: '2026-09-28', to: '2026-10-04', branchId: undefined }, expect.anything(),
    ));

    await userEvent.click(screen.getByRole('button', { name: 'Este mes' }));
    expect(screen.getByLabelText('Desde')).toHaveValue('2026-10-01');
    expect(screen.getByLabelText('Hasta')).toHaveValue('2026-10-11');
  });

  it('lists the orders of a platform in the period of the report, each with what it copied when it was registered', async () => {
    vi.mocked(reportsApi.getDeliveryPlatformOrders).mockResolvedValue(page([
      order({}),
      order({
        orderId: 'o2',
        orderNumber: 'ORD-000124',
        externalOrderNumber: '152391',
        status: OrderStatus.Cancelled,
        refundedAmount: 1000,
      }),
    ]));
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Ver pedidos de Rappi' }));
    const dialog = await screen.findByRole('dialog', { name: 'Pedidos de Rappi · 1 al 11 oct.' });

    expect(reportsApi.getDeliveryPlatformOrders).toHaveBeenCalledWith(
      { channel: OrderChannel.Rappi, from: '2026-10-01', to: '2026-10-11', branchId: undefined, pageNumber: 1, pageSize: 20 },
      expect.anything(),
    );
    expect(within(dialog).getByText('Cada pedido con la comisión que tenía Rappi al registrarlo.')).toBeInTheDocument();
    expect(within(dialog).getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Folio de la app', 'Pedido', 'Fecha', 'Cobró', 'Venta', 'Comisión e IVA', 'Retenciones', 'Neto esperado', 'Devuelto',
    ]);
    const first = within(dialog).getByRole('cell', { name: '152390' }).closest('tr') as HTMLElement;
    expect(first).toHaveTextContent('ORD-000123');
    expect(first).toHaveTextContent('Rappi');
    expect(first).toHaveTextContent('$1,000.00$348.00$90.52$561.48—');
    const voided = within(dialog).getByRole('cell', { name: '152391' }).closest('tr') as HTMLElement;
    expect(within(voided).getByText('Cancelada')).toHaveClass('ta-chip', 'ta-chip-cancelled');
    expect(voided).toHaveTextContent('$561.48$1,000.00');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('searches a folio of the statement on the server, over every page of the platform', async () => {
    vi.mocked(reportsApi.getDeliveryPlatformOrders).mockImplementation((async (query: { search?: string }) => (query.search
      ? page([order({ orderId: 'o9', orderNumber: 'ORD-000099', externalOrderNumber: 'BC953' })])
      : { ...page([order({})]), totalCount: 57 })) as never);
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Ver pedidos de Rappi' }));
    const dialog = await screen.findByRole('dialog', { name: 'Pedidos de Rappi · 1 al 11 oct.' });
    await within(dialog).findByRole('cell', { name: '152390' });
    await userEvent.type(within(dialog).getByRole('searchbox'), '#bc 953');

    // Not only in the page on screen: the server finds it wherever it is, from the first page.
    await waitFor(() => expect(reportsApi.getDeliveryPlatformOrders).toHaveBeenLastCalledWith(
      expect.objectContaining({ channel: OrderChannel.Rappi, search: '#bc 953', pageNumber: 1 }), expect.anything(),
    ));
    expect(await within(dialog).findByRole('cell', { name: 'BC953' })).toBeInTheDocument();
  });

  it('names who charged each order and says when a platform had none in the period', async () => {
    vi.mocked(reportsApi.getDeliveryPlatformOrders).mockResolvedValueOnce(page([
      order({ collectedBy: PlatformCollectedBy.Business, withheld: 0, commissionAndTax: 417.6, orderTotal: 1200, expectedSettlement: -417.6 }),
    ]));
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Ver pedidos de DiDi Food' }));
    let dialog = await screen.findByRole('dialog', { name: 'Pedidos de DiDi Food · 1 al 11 oct.' });
    const row = within(dialog).getByRole('cell', { name: '152390' }).closest('tr') as HTMLElement;
    expect(row).toHaveTextContent('El negocio');
    expect(row).toHaveTextContent('-$417.60');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    vi.mocked(reportsApi.getDeliveryPlatformOrders).mockResolvedValueOnce(page([]));
    await userEvent.click(screen.getByRole('button', { name: 'Ver pedidos de Rappi' }));
    dialog = await screen.findByRole('dialog', { name: 'Pedidos de Rappi · 1 al 11 oct.' });
    expect(await within(dialog).findByText('Sin pedidos de Rappi en el periodo.')).toBeInTheDocument();
  });

  it('writes a period across months with both of them', async () => {
    vi.mocked(reportsApi.getSalesByChannel).mockResolvedValue(report({ from: '2026-09-28', to: '2026-10-04' }));
    vi.mocked(reportsApi.getDeliveryPlatformOrders).mockResolvedValue(page([]));
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Ver pedidos de Rappi' }));

    expect(await screen.findByRole('dialog', { name: /^Pedidos de Rappi · 28 sept?\. al 4 oct\.$/ })).toBeInTheDocument();
  });
});
