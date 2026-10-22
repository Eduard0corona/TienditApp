import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PlatformSettlementDetail, PlatformSettlementOrder } from '../../api/platformSettlements';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import {
  OrderChannel,
  OrderStatus,
  PlatformCollectedBy,
  PlatformSettlementAdjustmentKind,
  PlatformSettlementStatus,
  PlatformStatementMatchStatus,
  TenantCapability,
  type TenantProfileDto,
} from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import PlatformSettlementPage from './PlatformSettlementPage';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

type Handler = (options: { path?: Record<string, string>; query?: Record<string, unknown>; body?: unknown }) => Promise<unknown>;

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const confirm = vi.fn();
const prompt = vi.fn();

/** An order of Rappi of $1,000: 561.48 when Rappi charged it, -348 (its commission) when the courier paid the business. */
function order(orderId: string, folio: string, registeredOn: string, patch: Partial<PlatformSettlementOrder> = {}): PlatformSettlementOrder {
  return {
    orderId,
    orderNumber: `ORD-${orderId}`,
    externalOrderNumber: folio,
    registeredAt: `${registeredOn}T20:00:00Z`,
    registeredOn,
    inPeriod: true,
    status: OrderStatus.Delivered,
    collectedBy: PlatformCollectedBy.Platform,
    orderTotal: 1000,
    refundedAmount: 0,
    expectedNet: 561.48,
    currentExpectedNet: 561.48,
    ...patch,
  };
}

const EARLIER = order('a', '152001', '2026-10-03');
const IN_PERIOD = order('b', '152390', '2026-10-06');
const COLLECTED = order('c', '152777', '2026-10-09', { collectedBy: PlatformCollectedBy.Business, expectedNet: -348, currentExpectedNet: -348 });

function detail(patch: Partial<PlatformSettlementDetail['settlement']> = {}, rest: Partial<PlatformSettlementDetail> = {}): PlatformSettlementDetail {
  return {
    settlement: {
      id: 's1',
      channel: OrderChannel.Rappi,
      status: PlatformSettlementStatus.Open,
      periodFrom: '2026-10-05',
      periodTo: '2026-10-11',
      depositedOn: '2026-10-14',
      depositedAmount: 163.48,
      reference: null,
      notes: null,
      ordersTotal: 213.48,
      adjustmentsTotal: -50,
      expectedDeposit: 163.48,
      difference: 0,
      orderCount: 2,
      revision: 1,
      createdAt: '2026-10-14T18:00:00Z',
      ...patch,
    },
    orders: [IN_PERIOD, COLLECTED],
    adjustments: [{ kind: PlatformSettlementAdjustmentKind.Other, description: 'Diferencia sin identificar', amount: -50 }],
    candidates: [EARLIER],
    candidatesTruncated: false,
    ...rest,
  };
}

/** Answers each call of the API by its method and path; one without an answer fails the test. */
function serve(handlers: Record<string, Handler>) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options: Parameters<Handler>[0] = {}) => {
    const handler = handlers[`${method} ${path}`];
    return handler ? handler(options) : Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
}

function calls(method: string, path: string) {
  return vi.mocked(apiRequest).mock.calls.filter(([calledMethod, calledPath]) => calledMethod === method && calledPath === path);
}

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function signIn(role: string, permissions: string[] = []) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions } } as unknown as ReturnType<typeof useAuth>);
}

function renderAt(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MemoryRouter initialEntries={[path]}>
      <QueryClientProvider client={queryClient}>
        <Routes>
          <Route path="/reports/settlements" element={<p>Lista de liquidaciones</p>} />
          <Route path="/reports/settlements/:id" element={<PlatformSettlementPage />} />
        </Routes>
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

/** The figure of a line of the accounts, by its label. */
function account(label: string): string {
  const accounts = within(screen.getByRole('region', { name: 'Cuentas' }));
  return accounts.getByText(label).nextElementSibling?.textContent ?? '';
}

describe('PlatformSettlementPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // Wednesday the 14th of October at noon in Mexico City: last week is the 5th to the 11th.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-14T18:00:00Z'));
    setBusinessTimeZone('America/Mexico_City');
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm, prompt });
    confirm.mockResolvedValue(true);
    signIn('TenantAdmin');
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita', timeZone: 'America/Mexico_City' } as TenantProfileDto,
      capabilities: TenantCapability.Food,
      isLoading: false,
      refreshTenant: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    setBusinessTimeZone(null);
  });

  it('proposes the orders of last week, explains the difference with an adjustment and saves the settlement', async () => {
    const user = userEvent.setup();
    serve({
      'get /api/v1/platform-settlements/pending-orders': () => Promise.resolve({ orders: [EARLIER, IN_PERIOD, COLLECTED], truncated: false }),
      'post /api/v1/platform-settlements': () => Promise.resolve(detail()),
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()),
    });
    renderAt('/reports/settlements/new?channel=3');

    expect(screen.getByRole('heading', { name: 'Nueva liquidación de Rappi' })).toBeInTheDocument();
    expect(screen.getByLabelText('Desde')).toHaveValue('2026-10-05');
    expect(screen.getByLabelText('Hasta')).toHaveValue('2026-10-11');
    expect(screen.getByLabelText('Fecha del depósito')).toHaveValue('2026-10-14');
    // The orders of the period go in; a pending one of before waits for the person.
    expect(await screen.findByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152777 (ORD-c)' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152001 (ORD-a)' })).not.toBeChecked();
    expect(screen.getByText('De antes del periodo')).toBeInTheDocument();
    expect(calls('get', '/api/v1/platform-settlements/pending-orders')[0][2]).toMatchObject({
      query: { channel: OrderChannel.Rappi, from: '2026-10-05', to: '2026-10-11' },
    });
    expect(account('2 pedidos')).toBe('$213.48');

    await user.type(screen.getByLabelText('Lo que depositó'), '163.48');
    expect(account('Diferencia')).toBe('-$50.00');
    expect(screen.getByText(/Llegó \$50\.00 menos de lo esperado/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Registrar la diferencia como ajuste' }));
    expect(screen.getByLabelText('Motivo del ajuste 1')).toHaveValue(String(PlatformSettlementAdjustmentKind.Other));
    expect(screen.getByLabelText('Importe del ajuste 1')).toHaveValue('-50.00');
    expect(account('Diferencia')).toBe('$0.00');
    expect(screen.getByText('Cuadra')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(calls('post', '/api/v1/platform-settlements')).toHaveLength(1));
    expect(calls('post', '/api/v1/platform-settlements')[0][2]).toEqual({
      body: {
        channel: OrderChannel.Rappi,
        periodFrom: '2026-10-05',
        periodTo: '2026-10-11',
        depositedOn: '2026-10-14',
        depositedAmount: 163.48,
        reference: null,
        notes: null,
        orderIds: ['b', 'c'],
        adjustments: [{ kind: PlatformSettlementAdjustmentKind.Other, description: 'Diferencia sin identificar', amount: -50 }],
      },
    });
    expect(toast.success).toHaveBeenCalledWith('Liquidación de Rappi guardada.');
    // The saved settlement opens in its own address, ready to close.
    expect(await screen.findByRole('heading', { name: 'Liquidación de Rappi' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cerrar liquidación' })).toBeEnabled();
  });

  it('imports a statement: includes the orders it brings, leaves out the rest and explains what they do not with adjustments', async () => {
    const user = userEvent.setup();
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()),
      'post /api/v1/platform-settlements/statement-matches': () => Promise.resolve({
        folios: [
          { folio: '152390', rows: [2], statementAmount: 561.48, status: PlatformStatementMatchStatus.InThisSettlement, orders: [IN_PERIOD], expectedNet: 561.48, difference: 0 },
          { folio: '152001', rows: [3], statementAmount: 541.48, status: PlatformStatementMatchStatus.Pending, orders: [EARLIER], expectedNet: 561.48, difference: -20 },
          { folio: '152999', rows: [4], statementAmount: 80, status: PlatformStatementMatchStatus.Unknown, orders: [] },
        ],
        concepts: [{ concept: 'Publicidad', rows: [5], amount: -100 }],
        unreadable: [],
        statementTotal: 1082.96,
        matchedTotal: 1102.96,
        expectedTotal: 1122.96,
        conceptsTotal: -100,
        unmatchedTotal: 80,
      }),
    });
    renderAt('/reports/settlements/s1');

    await user.click(await screen.findByRole('button', { name: 'Importar estado de cuenta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Importar estado de cuenta' });
    const csv = 'ID del pedido;Concepto;Monto neto\n152390;Pedido;561.48\n152001;Pedido;541.48\n152999;Pedido;80\n;Publicidad;-100.00\nTotal;;1082.96\n';
    await user.upload(within(dialog).getByLabelText('Archivo CSV'), new File([csv], 'rappi-octubre.csv', { type: 'text/csv' }));

    // The columns are guessed by their headers, and the first lines say how they read.
    expect(await within(dialog).findByLabelText('Folio o ID del pedido')).toHaveDisplayValue('ID del pedido');
    expect(within(dialog).getByLabelText('Importe')).toHaveDisplayValue('Monto neto');
    expect(within(dialog).getByLabelText('Concepto (opcional)')).toHaveDisplayValue('Concepto');
    expect(within(dialog).getByText('Línea 2: folio 152390 · $561.48 · Pedido')).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Comparar con mis pedidos' }));

    expect(await within(dialog).findByText(/El estado de cuenta suma/)).toHaveTextContent('El estado de cuenta suma $1,082.96 en 4 renglones.');
    // The row of totals is not sent: it would count everything twice.
    expect(calls('post', '/api/v1/platform-settlements/statement-matches')[0][2]).toEqual({
      body: {
        channel: OrderChannel.Rappi,
        settlementId: 's1',
        periodFrom: '2026-10-05',
        periodTo: '2026-10-11',
        lines: [
          { row: 2, folio: '152390', amount: 561.48, concept: 'Pedido' },
          { row: 3, folio: '152001', amount: 541.48, concept: 'Pedido' },
          { row: 4, folio: '152999', amount: 80, concept: 'Pedido' },
          { row: 5, folio: null, amount: -100, concept: 'Publicidad' },
        ],
      },
    });
    expect(within(dialog).getByText('Un renglón no se tomó')).toBeInTheDocument();
    expect(within(dialog).getByText(/152001: paga \$541\.48, se esperaba \$561\.48/)).toBeInTheDocument();
    expect(within(dialog).getByText('No son pedidos de la plataforma en TienditApp: 152999')).toBeInTheDocument();
    expect(within(dialog).getByText(/Sale 1 pedido incluido que el estado de cuenta no trae/)).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Aplicar a la liquidación' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152001 (ORD-a)' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152777 (ORD-c)' })).not.toBeChecked();
    // The adjustment written by hand stays, and the statement's follow it.
    expect(screen.getAllByLabelText(/^Descripción del ajuste \d/).map((box) => (box as HTMLInputElement).value)).toEqual([
      'Diferencia sin identificar',
      'Estado de cuenta: diferencia en 1 pedido',
      'Estado de cuenta: folios que no entran en esta liquidación (152999)',
      'Estado de cuenta: Publicidad',
    ]);
    expect(screen.getByLabelText('Importe del ajuste 4')).toHaveValue('-100.00');
    expect(screen.getByLabelText('Motivo del ajuste 4')).toHaveValue(String(PlatformSettlementAdjustmentKind.Fee));
    expect(screen.getByLabelText('Notas (opcional)')).toHaveValue('Estado de cuenta: rappi-octubre.csv, 4 renglones.');
    // 561.48 + 561.48 − 50 − 20 + 80 − 100 against the deposit already written.
    expect(account('Diferencia')).toBe('-$869.48');
    expect(toast.success).toHaveBeenCalledWith('Estado de cuenta aplicado. Revisa los ajustes y guarda la liquidación.');
  });

  it('leaves a folio with several orders for the person to pick, keeps the one saved, and saves what was applied', async () => {
    const user = userEvent.setup();
    // Rappi gave 152390 again to an order of Thursday that the screen did not offer.
    const TWIN = order('t', '152390', '2026-10-08');
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()),
      'post /api/v1/platform-settlements/statement-matches': () => Promise.resolve({
        folios: [
          { folio: '152390', rows: [2], statementAmount: 561.48, status: PlatformStatementMatchStatus.Ambiguous, orders: [TWIN, IN_PERIOD] },
          { folio: '152777', rows: [3], statementAmount: -348, status: PlatformStatementMatchStatus.InThisSettlement, orders: [COLLECTED], expectedNet: -348, difference: 0 },
          { folio: '152001', rows: [4], statementAmount: 561.48, status: PlatformStatementMatchStatus.Pending, orders: [EARLIER], expectedNet: 561.48, difference: 0 },
        ],
        concepts: [],
        unreadable: [],
        statementTotal: 774.96,
        matchedTotal: 213.48,
        expectedTotal: 213.48,
        conceptsTotal: 0,
        unmatchedTotal: 561.48,
      }),
      'put /api/v1/platform-settlements/{id}': () => Promise.reject(apiError(409, 'conflict', 'Otra liquidación ya tiene el pedido 152001.')),
    });
    renderAt('/reports/settlements/s1');

    await user.click(await screen.findByRole('button', { name: 'Importar estado de cuenta' }));
    const dialog = await screen.findByRole('dialog', { name: 'Importar estado de cuenta' });
    const csv = 'Folio,Importe\n152390,561.48\n152777,-348.00\n152001,561.48\n';
    await user.upload(within(dialog).getByLabelText('Archivo CSV'), new File([csv], 'rappi.csv', { type: 'text/csv' }));
    await user.click(await within(dialog).findByRole('button', { name: 'Comparar con mis pedidos' }));

    // The summary takes the focus from the button that left; the folio with two orders is the person's to pick.
    await waitFor(() => expect(within(dialog).getByText(/El estado de cuenta suma/)).toHaveFocus());
    expect(within(dialog).getByText(/Al aplicar se incluyen los 2 pedidos que entran\./)).toHaveTextContent('Un folio tiene varios pedidos: elige en la lista cuál incluir.');
    expect(within(dialog).queryByText(/Sale 1 pedido incluido/)).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Aplicar a la liquidación' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-t)' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152001 (ORD-a)' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Incluir el pedido 152777 (ORD-c)' })).toBeChecked();
    await user.click(screen.getByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-t)' }));

    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    expect(calls('put', '/api/v1/platform-settlements/{id}')[0][2]).toMatchObject({
      path: { id: 's1' },
      body: {
        revision: 1,
        depositedAmount: 163.48,
        notes: 'Estado de cuenta: rappi.csv, 3 renglones.',
        orderIds: ['a', 'b', 't', 'c'],
        adjustments: [{ kind: PlatformSettlementAdjustmentKind.Other, description: 'Diferencia sin identificar', amount: -50 }],
      },
    });
    // Another settlement took an order meanwhile: the list read again is the one to trust, without the statement's.
    expect(await screen.findByText(/Otra liquidación tomó uno de los pedidos/)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-t)' })).not.toBeInTheDocument());
  });

  it('marks what the API would refuse and does not save until it is fixed', async () => {
    const user = userEvent.setup();
    serve({
      'get /api/v1/platform-settlements/pending-orders': () => Promise.resolve({ orders: [IN_PERIOD], truncated: false }),
    });
    renderAt('/reports/settlements/new?channel=3');
    await screen.findByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' });

    // A comma only groups thousands: «32,50» is not read as $3,250.
    await user.type(screen.getByLabelText('Lo que depositó'), '32,50');
    expect(screen.getByText('Lo depositado va de -$10,000,000 a $10,000,000, con dos decimales.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Lo que depositó'));
    await user.type(screen.getByLabelText('Lo que depositó'), '561.48');
    await user.click(screen.getByRole('button', { name: /Agregar ajuste/ }));
    // A new row is not red before it is written.
    expect(screen.queryByText('Elige el motivo de cada ajuste.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(screen.getAllByText('Elige el motivo de cada ajuste.').length).toBeGreaterThan(0);
    expect(calls('post', '/api/v1/platform-settlements')).toHaveLength(0);
  });

  it('does not open a second settlement of a platform that has one open', async () => {
    serve({
      'get /api/v1/platform-settlements/pending-orders': () => Promise.resolve({ orders: [IN_PERIOD], truncated: false, openSettlementId: 's9' }),
    });
    renderAt('/reports/settlements/new?channel=3');

    expect(await screen.findByText(/Ya hay una liquidación abierta de Rappi: termínala o anúlala/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver la liquidación abierta' })).toHaveAttribute('href', '/reports/settlements/s9');
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
  });

  it('closes a balanced settlement after asking, and then only offers to void it', async () => {
    const user = userEvent.setup();
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()),
      'post /api/v1/platform-settlements/{id}/close': () => Promise.resolve(detail(
        { status: PlatformSettlementStatus.Closed, revision: 2, closedAt: '2026-10-14T18:05:00Z' },
        { candidates: [] },
      )),
    });
    renderAt('/reports/settlements/s1');

    expect(await screen.findByText('Cuadra: ya puedes cerrarla.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Guardar' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cerrar liquidación' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Cerrar liquidación' }));
    await waitFor(() => expect(calls('post', '/api/v1/platform-settlements/{id}/close')).toHaveLength(1));
    expect(calls('post', '/api/v1/platform-settlements/{id}/close')[0][2]).toMatchObject({ path: { id: 's1' }, body: { revision: 1 } });
    expect(toast.success).toHaveBeenCalledWith('Liquidación de Rappi cerrada.');
    expect(await screen.findByText('Cerrada')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cerrar liquidación' })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Anular' })).toBeInTheDocument();
  });

  it('reads the settlement again when closing finds that what an order brings changed', async () => {
    const user = userEvent.setup();
    const recounted = detail(
      { revision: 2, ordersTotal: -67.26, expectedDeposit: -117.26, difference: 280.74 },
      { orders: [order('b', '152390', '2026-10-06', { refundedAmount: 500, expectedNet: 280.74, currentExpectedNet: 280.74 }), COLLECTED] },
    );
    let reads = 0;
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(++reads === 1 ? detail() : recounted),
      // The server saved the new figures and says why, with a code of its own: nobody else saved.
      'post /api/v1/platform-settlements/{id}/close': () => Promise.reject(apiError(
        409,
        'conflict',
        'Cambió lo que se espera de un pedido desde que guardaste (por una devolución, por ejemplo). Revisa la liquidación y ciérrala otra vez.',
      )),
    });
    renderAt('/reports/settlements/s1');

    await user.click(await screen.findByRole('button', { name: 'Cerrar liquidación' }));

    expect(await screen.findByText(/Revisa la liquidación y ciérrala otra vez/)).toBeInTheDocument();
    await waitFor(() => expect(account('Diferencia')).toBe('$280.74'));
    expect(screen.getByRole('button', { name: 'Cerrar liquidación' })).toBeDisabled();
    expect(screen.getByText('Explica la diferencia con un ajuste para cerrar.')).toBeInTheDocument();
  });

  it('asks to save first when what a saved order brings changed since the save', async () => {
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail({}, {
        orders: [order('b', '152390', '2026-10-06', { refundedAmount: 500, currentExpectedNet: 280.74 }), COLLECTED],
      })),
    });
    renderAt('/reports/settlements/s1');

    expect(await screen.findByText(/Cambió lo que se espera de un pedido desde que guardaste/)).toBeInTheDocument();
    expect(screen.getByText('Guardado:')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Cerrar liquidación' })).not.toBeInTheDocument();
    // The accounts already count what the order brings now.
    expect(account('2 pedidos')).toBe('-$67.26');
  });

  it('says when someone else saved meanwhile and shows their version on request', async () => {
    const user = userEvent.setup();
    let reads = 0;
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(++reads === 1 ? detail() : detail({ revision: 2, reference: 'SPEI 9921' })),
      'put /api/v1/platform-settlements/{id}': () => Promise.reject(apiError(
        409,
        'concurrency_conflict',
        'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.',
      )),
    });
    renderAt('/reports/settlements/s1');

    await user.type(await screen.findByLabelText('Referencia (opcional)'), 'SPEI 4471');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(calls('put', '/api/v1/platform-settlements/{id}')[0][2]).toMatchObject({ path: { id: 's1' }, body: { revision: 1, reference: 'SPEI 4471' } });
    expect(await screen.findByText(/Alguien más modificó este registro/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Ver la versión guardada' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Ver la versión guardada' }));
    await waitFor(() => expect(screen.getByLabelText('Referencia (opcional)')).toHaveValue('SPEI 9921'));
  });

  it('waits for the orders of another period before saving it, and saves only that period\'s', async () => {
    const user = userEvent.setup();
    const LATER = order('d', '152801', '2026-10-12');
    let release: (orders: unknown) => void = () => undefined;
    serve({
      'get /api/v1/platform-settlements/pending-orders': ({ query }) => (query?.to === '2026-10-11'
        ? Promise.resolve({ orders: [IN_PERIOD, COLLECTED], truncated: false })
        : new Promise((resolve) => { release = resolve; })),
      'post /api/v1/platform-settlements': () => Promise.resolve(detail()),
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()),
    });
    renderAt('/reports/settlements/new?channel=3');
    await screen.findByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' });
    await user.type(screen.getByLabelText('Lo que depositó'), '500');

    // Up to the 12th: until its orders arrive, those of the period before are neither shown nor saved.
    fireEvent.change(screen.getByLabelText('Hasta'), { target: { value: '2026-10-12' } });

    expect(await screen.findByText('Cargando los pedidos del periodo…')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Registrar la diferencia como ajuste' })).not.toBeInTheDocument();

    await act(async () => release({ orders: [IN_PERIOD, COLLECTED, LATER], truncated: false }));
    expect(await screen.findByRole('checkbox', { name: 'Incluir el pedido 152801 (ORD-d)' })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(calls('post', '/api/v1/platform-settlements')).toHaveLength(1));
    expect(calls('post', '/api/v1/platform-settlements')[0][2]).toMatchObject({
      body: { periodTo: '2026-10-12', orderIds: ['b', 'c', 'd'] },
    });
  });

  it('keeps what was written when another settlement took one of its orders meanwhile, and reads the orders again', async () => {
    const user = userEvent.setup();
    let reads = 0;
    let posts = 0;
    serve({
      'get /api/v1/platform-settlements/pending-orders': () => Promise.resolve(++reads === 1
        ? { orders: [IN_PERIOD, COLLECTED], truncated: false }
        : { orders: [COLLECTED], truncated: false }),
      'post /api/v1/platform-settlements': () => (++posts === 1
        ? Promise.reject(apiError(409, 'conflict', 'Otra liquidación ya tiene uno de los pedidos. Recarga e intenta de nuevo.'))
        : Promise.resolve(detail())),
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()),
    });
    renderAt('/reports/settlements/new?channel=3');
    await screen.findByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' });
    await user.type(screen.getByLabelText('Lo que depositó'), '-348');

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByText(/Otra liquidación tomó uno de los pedidos mientras escribías/)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' })).not.toBeInTheDocument());
    expect(screen.getByLabelText('Lo que depositó')).toHaveValue('-348');
    expect(screen.queryByRole('button', { name: 'Recargar' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    await waitFor(() => expect(calls('post', '/api/v1/platform-settlements')).toHaveLength(2));
    expect(calls('post', '/api/v1/platform-settlements')[1][2]).toMatchObject({ body: { orderIds: ['c'] } });
  });

  it('brings the open settlement of the platform when saving a new one finds it', async () => {
    const user = userEvent.setup();
    let reads = 0;
    serve({
      'get /api/v1/platform-settlements/pending-orders': () => Promise.resolve(++reads === 1
        ? { orders: [IN_PERIOD], truncated: false }
        : { orders: [IN_PERIOD], truncated: false, openSettlementId: 's9' }),
      'post /api/v1/platform-settlements': () => Promise.reject(apiError(
        400,
        'business_rule',
        'Ya hay una liquidación abierta de Rappi: termínala o anúlala antes de abrir otra.',
      )),
    });
    renderAt('/reports/settlements/new?channel=3');
    await screen.findByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' });
    await user.type(screen.getByLabelText('Lo que depositó'), '561.48');

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByRole('link', { name: 'Ver la liquidación abierta' })).toHaveAttribute('href', '/reports/settlements/s9');
    expect(screen.getAllByText(/Ya hay una liquidación abierta de Rappi: termínala o anúlala/)).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
  });

  it('asks before discarding what was changed, and before a closed tab would lose it', async () => {
    const user = userEvent.setup();
    serve({ 'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()) });
    renderAt('/reports/settlements/s1');
    const reference = await screen.findByLabelText('Referencia (opcional)');
    const leaving = () => {
      const event = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(event);
      return event.defaultPrevented;
    };
    expect(leaving()).toBe(false);

    await user.type(reference, 'SPEI 4471');
    expect(leaving()).toBe(true);
    await user.click(screen.getByRole('button', { name: 'Descartar' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Descartar cambios' }));
    expect(reference).toHaveValue('');
    expect(leaving()).toBe(false);
    expect(screen.getByRole('button', { name: 'Cerrar liquidación' })).toBeEnabled();
  });

  it('asks before leaving a new settlement with something written', async () => {
    const user = userEvent.setup();
    serve({ 'get /api/v1/platform-settlements/pending-orders': () => Promise.resolve({ orders: [IN_PERIOD], truncated: false }) });
    renderAt('/reports/settlements/new?channel=3');
    await screen.findByRole('checkbox', { name: 'Incluir el pedido 152390 (ORD-b)' });
    await user.type(screen.getByLabelText('Lo que depositó'), '100');
    confirm.mockResolvedValueOnce(false);

    await user.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Descartar liquidación' }));
    expect(screen.getByLabelText('Lo que depositó')).toHaveValue('100');

    await user.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(await screen.findByText('Lista de liquidaciones')).toBeInTheDocument();
  });

  it('shows the version someone else saved when closing over an older one', async () => {
    const user = userEvent.setup();
    let reads = 0;
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(++reads === 1 ? detail() : detail({ revision: 2, reference: 'SPEI 9921' })),
      'post /api/v1/platform-settlements/{id}/close': () => Promise.reject(apiError(
        409,
        'concurrency_conflict',
        'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.',
      )),
    });
    renderAt('/reports/settlements/s1');

    await user.click(await screen.findByRole('button', { name: 'Cerrar liquidación' }));

    expect(await screen.findByText(
      'Alguien más guardó esta liquidación mientras la veías: esta es la versión que guardó. Revísala y vuelve a intentarlo.',
    )).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText('Referencia (opcional)')).toHaveValue('SPEI 9921'));
    expect(screen.queryByText(/Recarga e intenta de nuevo/)).not.toBeInTheDocument();
  });

  it('voids a closed settlement with the reason the person writes', async () => {
    const user = userEvent.setup();
    const closed = detail({ status: PlatformSettlementStatus.Closed, revision: 2, closedAt: '2026-10-14T18:05:00Z' }, { candidates: [] });
    serve({
      'get /api/v1/platform-settlements/{id}': () => Promise.resolve(closed),
      'post /api/v1/platform-settlements/{id}/void': () => Promise.resolve(detail(
        { status: PlatformSettlementStatus.Voided, revision: 3, voidedAt: '2026-10-14T18:10:00Z', voidReason: 'Capturé el depósito de otra semana' },
        { candidates: [] },
      )),
    });
    prompt.mockResolvedValue('Capturé el depósito de otra semana');
    renderAt('/reports/settlements/s1');

    await user.click(await screen.findByRole('button', { name: 'Anular' }));

    expect(prompt).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Anular liquidación',
      message: 'Sus 2 pedidos vuelven a quedar pendientes. La liquidación se queda como historia: no se borra.',
      required: true,
      maxLength: 500,
    }));
    await waitFor(() => expect(calls('post', '/api/v1/platform-settlements/{id}/void')).toHaveLength(1));
    expect(calls('post', '/api/v1/platform-settlements/{id}/void')[0][2]).toMatchObject({
      path: { id: 's1' },
      body: { revision: 2, reason: 'Capturé el depósito de otra semana' },
    });
    expect(await screen.findByText('Anulada')).toBeInTheDocument();
    expect(screen.getByText('Capturé el depósito de otra semana')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Anular' })).not.toBeInTheDocument();
  });

  it('shows an open settlement as it is to whoever only reads the reports', async () => {
    signIn('Staff', ['ViewReports']);
    serve({ 'get /api/v1/platform-settlements/{id}': () => Promise.resolve(detail()) });
    renderAt('/reports/settlements/s1');

    expect(await screen.findByText('Solo el dueño o un administrador del negocio puede cambiarla y cerrarla.')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Guardar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cerrar liquidación' })).not.toBeInTheDocument();
    // Its own orders only, not the pending ones it could add.
    expect(screen.getByText('152390')).toBeInTheDocument();
    expect(screen.queryByText('152001')).not.toBeInTheDocument();
    expect(account('Diferencia')).toBe('$0.00');
  });

  it('does not let whoever only reads the reports register a deposit', () => {
    signIn('Staff', ['ViewReports']);
    serve({});
    renderAt('/reports/settlements/new?channel=3');

    expect(screen.getByText(/Solo el dueño o un administrador del negocio puede registrar los depósitos/)).toBeInTheDocument();
    expect(apiRequest).not.toHaveBeenCalled();
  });

  it('says when the settlement does not exist', async () => {
    serve({ 'get /api/v1/platform-settlements/{id}': () => Promise.reject(apiError(404, 'not_found', 'No existe.')) });
    renderAt('/reports/settlements/s404');

    expect(await screen.findByText('No encontramos esta liquidación.')).toBeInTheDocument();
  });
});
