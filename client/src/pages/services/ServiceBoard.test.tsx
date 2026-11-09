import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { serviceBoardApi } from '../../api/serviceBoardApi';
import { ServiceOrderPieceStatus, type ServiceBoardPieceDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import ServiceBoard from './ServiceBoard';

const signalR = vi.hoisted(() => ({ handlers: new Map<string, Set<() => void>>() }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../components/services/ServicePieceDetail', () => ({
  default: ({ piece }: { piece: { itemName: string } | null }) => (piece ? <div role="dialog" aria-label={`Detalle de ${piece.itemName}`} /> : null),
}));
vi.mock('../../api/serviceBoardApi', () => ({ serviceBoardApi: { getPieces: vi.fn(), assignToMe: vi.fn(), release: vi.fn() } }));
vi.mock('../../api/ordersApi', () => ({ ordersApi: { updateServicePieceStatus: vi.fn() } }));
vi.mock('../../hooks/useOrderSignalR', () => {
  const subscribe = (event: string) => (handler: () => void) => {
    const set = signalR.handlers.get(event) ?? new Set();
    set.add(handler);
    signalR.handlers.set(event, set);
    return () => set.delete(handler);
  };
  const api = {
    onOrderCreated: subscribe('created'), onOrderStatusChanged: subscribe('status'),
    onOrderCancelled: subscribe('cancelled'), onServiceBoardPieceUpdated: subscribe('piece'),
  };
  return { useOrderSignalR: () => api };
});

const emit = (event: string) => signalR.handlers.get(event)?.forEach((handler) => handler());

function piece(patch: Partial<ServiceBoardPieceDto>): ServiceBoardPieceDto {
  return {
    pieceId: 'p', orderId: 'o1', orderNumber: 'ORD-20261009-0007', customerName: 'Marta Ruiz', orderCreatedAt: '2026-10-09T15:00:00Z',
    // The priorities the intake saves: 1 low, 3 normal, 5 urgent.
    priority: 3, itemName: 'Pieza', notifyCustomer: false, status: ServiceOrderPieceStatus.Accepted, isOverdue: false,
    ...patch,
  };
}

/**
 * A repair shop: two coffee machines and a scale waiting, a grinder late in the bench, and what is already done or
 * cancelled. As the server sends them: a late piece has its delay and no minutes to its promise.
 */
const pieces = [
  piece({ pieceId: 'gaggia', itemName: 'Cafetera Gaggia', requestedService: 'Mantenimiento', priority: 5, minutesToPromise: 45, notifyCustomer: true, customerPhone: '55 1234 5678' }),
  piece({ pieceId: 'delonghi', orderId: 'o2', orderNumber: 'ORD-20261009-0008', itemName: 'Cafetera DeLonghi', priority: 1, minutesToPromise: 180 }),
  piece({
    pieceId: 'grinder', orderId: 'o3', itemName: 'Molino Baratza', status: ServiceOrderPieceStatus.InProgress, isOverdue: true, delayMinutes: 90,
    assignedToUserId: 'u-luis', assignedToName: 'Luis', internalNotes: 'Cambiar fresas', promisedAt: '2026-10-09T20:00:00Z',
  }),
  // Without a promised date: nothing to count down, so it is not due within the hour.
  // Saved as 4 through the API, which takes 1 to 5: from 4 up it is urgent.
  piece({ pieceId: 'scale', orderId: 'o4', itemName: 'Báscula', requestedService: 'Calibración', priority: 4 }),
  // The server reads the delay only of what is still open.
  // Saved as 2: only 1 is low.
  piece({ pieceId: 'kettle', itemName: 'Hervidor', status: ServiceOrderPieceStatus.Completed, assignedToUserId: 'u-ana', assignedToName: 'Ana', priority: 2 }),
  piece({ pieceId: 'press', itemName: 'Prensa francesa', status: ServiceOrderPieceStatus.Cancelled }),
];

const board = (items: ServiceBoardPieceDto[], totalCount = items.length) =>
  ({ data: { items, pageNumber: 1, totalPages: 1, totalCount, hasPreviousPage: false, hasNextPage: false } }) as never;

const column = (title: string) => screen.getByText(title, { selector: 'span' }).closest('.card') as HTMLElement;
const pieceCard = (name: string) => screen.getByRole('button', { name }).closest('.border') as HTMLElement;
/** The figure under the title of a card, whole: «Por vencer (<1h)» already has a 1 in it. */
const kpi = (title: string) => screen.getByText(title).nextElementSibling?.textContent;

describe('ServiceBoard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    signalR.handlers.clear();
    // Seven trackable pieces, six of them on this page: the total is the server's.
    vi.mocked(serviceBoardApi.getPieces).mockResolvedValue(board(pieces, 7));
  });

  afterEach(() => setBusinessTimeZone(null));

  it('puts each piece in the column of its status and counts what is active, late or due within the hour', async () => {
    render(<ServiceBoard />);

    expect(await screen.findByRole('button', { name: 'Cafetera Gaggia' })).toBeInTheDocument();
    expect(serviceBoardApi.getPieces).toHaveBeenCalledWith({
      search: undefined, includeCompleted: false, includeCancelled: false, onlyMine: false, pageNumber: 1, pageSize: 300,
    });
    expect(kpi('Piezas rastreables')).toBe('7');
    expect(kpi('Activas')).toBe('4');
    expect(kpi('Atrasadas')).toBe('1');
    expect(kpi('Por vencer (<1h)')).toBe('1');

    const names = (title: string) => within(column(title)).queryAllByRole('button', { name: /^(?!Tomar|Liberar|En progreso|Completar|Cancelar)/ })
      .map((button) => button.textContent);
    expect(within(column('Aceptadas')).getByText('3', { selector: '.badge' })).toBeInTheDocument();
    expect(names('Aceptadas')).toEqual(['Cafetera Gaggia', 'Cafetera DeLonghi', 'Báscula']);
    expect(names('En progreso')).toEqual(['Molino Baratza']);
    expect(names('Completadas')).toEqual(['Hervidor']);
    expect(names('Canceladas')).toEqual(['Prensa francesa']);
  });

  it('says on each piece its priority, when it came in, its promise, how long until it or how late it is, and who has it', async () => {
    // Not the zone of the team's machines: a date read in the device's zone fails here too.
    setBusinessTimeZone('America/Cancun');
    render(<ServiceBoard />);
    await screen.findByRole('button', { name: 'Cafetera Gaggia' });

    const gaggia = pieceCard('Cafetera Gaggia');
    expect(gaggia).toHaveTextContent('Mantenimiento');
    expect(gaggia).toHaveTextContent('Urgente');
    expect(gaggia).toHaveTextContent('Entrega en 45 min');
    expect(gaggia).toHaveTextContent('Tel: 55 1234 5678');
    expect(gaggia).toHaveTextContent('Notifica al cliente');
    expect(gaggia).toHaveTextContent('Sin asignar');
    // Cancún is UTC-5 all year; the time reads with its a.m. or p.m., as the device's ICU writes it.
    expect(gaggia).toHaveTextContent(/Ingreso: 09\/10\/2026, 10:00/);
    expect(gaggia).not.toHaveTextContent('Promesa:');

    const delonghi = pieceCard('Cafetera DeLonghi');
    expect(delonghi).toHaveTextContent('Baja');
    expect(delonghi).toHaveTextContent('Entrega en 3 h');
    expect(delonghi).not.toHaveTextContent('Urgente');

    const grinder = pieceCard('Molino Baratza');
    expect(grinder).toHaveTextContent('Atrasada 1 h 30 min');
    expect(grinder).toHaveTextContent(/Promesa: 09\/10\/2026, 03:00/);
    expect(grinder).toHaveTextContent('Asignada: Luis');
    expect(grinder).toHaveTextContent('Nota interna: Cambiar fresas');
    expect(grinder).toHaveTextContent('Sin notificación');
    // Normal priority has no badge.
    expect(grinder).not.toHaveTextContent('Baja');
    expect(grinder).not.toHaveTextContent('Urgente');
    expect(pieceCard('Báscula')).not.toHaveTextContent('Entrega en');
    expect(pieceCard('Báscula')).toHaveTextContent('Urgente');
    expect(pieceCard('Hervidor')).not.toHaveTextContent('Baja');
  });

  it('moves a piece only to the next status it can take, and reads the board again', async () => {
    const user = userEvent.setup();
    vi.mocked(ordersApi.updateServicePieceStatus).mockResolvedValue({ data: {} } as never);
    render(<ServiceBoard />);
    await screen.findByRole('button', { name: 'Cafetera Gaggia' });

    const actions = (name: string) => within(pieceCard(name)).getAllByRole('button').slice(1).map((button) => button.textContent);
    expect(actions('Cafetera Gaggia')).toEqual(['Tomar servicio', 'En progreso', 'Cancelar']);
    expect(actions('Molino Baratza')).toEqual(['Liberar', 'Completar', 'Cancelar']);
    // A completed or cancelled piece is neither taken nor let go: the server refuses it, and who worked it stays.
    expect(actions('Hervidor')).toEqual([]);
    expect(pieceCard('Hervidor')).toHaveTextContent('Asignada: Ana');
    expect(actions('Prensa francesa')).toEqual([]);

    await user.click(within(pieceCard('Molino Baratza')).getByRole('button', { name: 'Completar' }));
    expect(ordersApi.updateServicePieceStatus).toHaveBeenCalledWith('o3', 'grinder', ServiceOrderPieceStatus.Completed);
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenCalledTimes(2));

    await user.click(within(pieceCard('Cafetera Gaggia')).getByRole('button', { name: 'En progreso' }));
    expect(ordersApi.updateServicePieceStatus).toHaveBeenLastCalledWith('o1', 'gaggia', ServiceOrderPieceStatus.InProgress);
  });

  it('lets whoever is free take a piece and whoever has it let it go', async () => {
    const user = userEvent.setup();
    vi.mocked(serviceBoardApi.assignToMe).mockResolvedValue({ data: {} } as never);
    vi.mocked(serviceBoardApi.release).mockResolvedValue({ data: {} } as never);
    render(<ServiceBoard />);
    await screen.findByRole('button', { name: 'Cafetera Gaggia' });

    await user.click(within(pieceCard('Cafetera Gaggia')).getByRole('button', { name: 'Tomar servicio' }));
    expect(serviceBoardApi.assignToMe).toHaveBeenCalledWith('gaggia');
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenCalledTimes(2));

    await user.click(within(pieceCard('Molino Baratza')).getByRole('button', { name: 'Liberar' }));
    expect(serviceBoardApi.release).toHaveBeenCalledWith('grinder');
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenCalledTimes(3));
  });

  it('asks the server for what is searched and the pieces of the filters', async () => {
    const user = userEvent.setup();
    render(<ServiceBoard />);
    await screen.findByRole('button', { name: 'Cafetera Gaggia' });

    await user.type(screen.getByPlaceholderText('Buscar por orden, cliente, pieza o teléfono'), '  gaggia ');
    await user.click(screen.getByLabelText('Mis servicios'));
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenLastCalledWith({
      search: 'gaggia', includeCompleted: false, includeCancelled: false, onlyMine: true, pageNumber: 1, pageSize: 300,
    }));
    await user.click(screen.getByLabelText('Mostrar completadas'));
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenLastCalledWith({
      search: 'gaggia', includeCompleted: true, includeCancelled: false, onlyMine: true, pageNumber: 1, pageSize: 300,
    }));

    await user.click(screen.getByLabelText('Mostrar canceladas'));
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenLastCalledWith({
      search: 'gaggia', includeCompleted: true, includeCancelled: true, onlyMine: true, pageNumber: 1, pageSize: 300,
    }));
  });

  it('reads the board again, once, when pieces or orders change elsewhere', async () => {
    render(<ServiceBoard />);
    await screen.findByRole('button', { name: 'Cafetera Gaggia' });
    expect(serviceBoardApi.getPieces).toHaveBeenCalledTimes(1);

    emit('piece');
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenCalledTimes(2));
    // Several changes at once are one read.
    emit('status');
    emit('created');
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenCalledTimes(3));

    // The read that follows a change does not blank the board while it waits.
    let answer: (value: unknown) => void = () => undefined;
    vi.mocked(serviceBoardApi.getPieces).mockReturnValueOnce(new Promise((resolve) => { answer = resolve; }) as never);
    emit('cancelled');
    await waitFor(() => expect(serviceBoardApi.getPieces).toHaveBeenCalledTimes(4));
    expect(screen.getByRole('button', { name: 'Cafetera Gaggia' })).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    answer(board(pieces.slice(1), 6));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Cafetera Gaggia' })).not.toBeInTheDocument());
  });

  it('opens the detail of a piece', async () => {
    const user = userEvent.setup();
    render(<ServiceBoard />);

    await user.click(await screen.findByRole('button', { name: 'Molino Baratza' }));
    expect(screen.getByRole('dialog', { name: 'Detalle de Molino Baratza' })).toBeInTheDocument();
  });

  it('says why a piece could not move, and when the board could not be read or is empty', async () => {
    const user = userEvent.setup();
    vi.mocked(ordersApi.updateServicePieceStatus).mockRejectedValue({
      isAxiosError: true, response: { status: 409, data: { error: 'La orden ORD-20261009-0007 está cancelada.' } },
    });
    vi.mocked(serviceBoardApi.getPieces)
      .mockResolvedValueOnce(board(pieces, 7))
      .mockRejectedValueOnce(new Error('Network Error'))
      .mockResolvedValueOnce(board([]));
    render(<ServiceBoard />);
    await screen.findByRole('button', { name: 'Cafetera Gaggia' });

    await user.click(within(pieceCard('Cafetera Gaggia')).getByRole('button', { name: 'Cancelar' }));
    expect(await screen.findByText('La orden ORD-20261009-0007 está cancelada.')).toBeInTheDocument();
    expect(within(pieceCard('Cafetera Gaggia')).getByRole('button', { name: 'Cancelar' })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: 'Recargar' }));
    expect(await screen.findByText('No se pudieron cargar los servicios para el tablero.')).toBeInTheDocument();
    expect(screen.queryByText('La orden ORD-20261009-0007 está cancelada.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Recargar' }));
    expect(await screen.findByText('No hay servicios rastreables para mostrar con los filtros actuales.')).toBeInTheDocument();
    expect(screen.queryByText('No se pudieron cargar los servicios para el tablero.')).not.toBeInTheDocument();
  });
});
