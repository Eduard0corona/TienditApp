import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { useConfirm } from '../../hooks/useConfirm';
import { OrderChannel, OrderStatus, OrderType, PlatformCollectedBy, type OrderDto } from '../../types';
import PlatformOrderIdEditor from './PlatformOrderIdEditor';

vi.mock('../../api/ordersApi', () => ({ ordersApi: { setPlatformOrderId: vi.fn() } }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));

const api = vi.mocked(ordersApi);
const confirm = vi.fn();

const order = (externalOrderId: string | null = null): OrderDto => ({
  id: 'o1',
  createdByBusiness: true,
  orderNumber: 'ORD-000042',
  businessType: 0 as OrderDto['businessType'],
  orderType: OrderType.Retail,
  status: OrderStatus.Delivered,
  canCancel: false,
  allowedTransitions: [],
  customerName: 'Juan',
  pricesIncludeTax: true,
  subTotal: 862.07,
  tax: 137.93,
  total: 1000,
  taxBreakdown: [],
  paidAmount: 1000,
  balance: 0,
  createdAt: '2026-10-22T19:00:00Z',
  items: [],
  statusHistory: [],
  servicePieces: [],
  payments: [],
  channel: OrderChannel.UberEats,
  platformOrder: { externalOrderNumber: 'BC953', externalOrderId, collectedBy: PlatformCollectedBy.Platform },
});

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
    status, data: { code, detail }, statusText: '', headers, config: { headers },
  } as AxiosResponse);
}

describe('PlatformOrderIdEditor', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useConfirm).mockReturnValue({ confirm } as unknown as ReturnType<typeof useConfirm>);
  });

  it('adds the long id of a delivered order, saying where to find it and refusing one that is not', async () => {
    api.setPlatformOrderId.mockResolvedValue({} as never);
    const onSaved = vi.fn();
    render(<PlatformOrderIdEditor order={order()} platform={OrderChannel.UberEats} canManage onSaved={onSaved} />);

    expect(screen.getByText('sin capturar')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Agregar el ID largo del pedido ORD-000042' }));
    const field = screen.getByLabelText('ID largo del pedido');
    expect(field).toHaveAccessibleDescription('El ID del pedido (con guiones) del estado de cuenta de Uber Eats.');

    await userEvent.type(field, 'BC953');
    expect(field).toHaveAccessibleDescription('El ID largo del pedido lleva de 6 a 64 letras, números o guiones.');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    expect(api.setPlatformOrderId).not.toHaveBeenCalled();

    await userEvent.clear(field);
    await userEvent.type(field, '3f9c2b1e-8d4a-4c6f-9b2e-1a7d5e0c3b84');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(api.setPlatformOrderId).toHaveBeenCalledWith('o1', { externalOrderId: '3f9c2b1e-8d4a-4c6f-9b2e-1a7d5e0c3b84' });
    expect(onSaved).toHaveBeenCalledWith(false);
    expect(screen.queryByLabelText('ID largo del pedido')).not.toBeInTheDocument();
  });

  it('asks before saving a long id another order has, and clears it when left empty', async () => {
    api.setPlatformOrderId
      .mockRejectedValueOnce(apiError(409, 'possible_duplicate_order', 'Otro pedido de esta plataforma ya tiene este ID largo.'))
      .mockResolvedValue({} as never);
    confirm.mockResolvedValueOnce(true);
    const onSaved = vi.fn();
    const current = '3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B84';
    const page = render(<PlatformOrderIdEditor order={order(current)} platform={OrderChannel.UberEats} canManage onSaved={onSaved} />);

    expect(screen.getByText(current)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Corregir el ID largo del pedido ORD-000042' }));
    const field = screen.getByLabelText('ID largo del pedido');
    expect(field).toHaveValue(current);
    expect(field).toHaveAccessibleDescription(
      'El ID del pedido (con guiones) del estado de cuenta de Uber Eats. Déjalo vacío para quitarlo.',
    );
    await userEvent.clear(field);
    await userEvent.type(field, 'RAPPI-000555');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
      title: '¿Es el ID correcto?',
      message: 'Otro pedido de Uber Eats ya tiene el ID largo RAPPI-000555. '
        + 'Los IDs largos no se repiten: revisa que no sea el mismo pedido capturado dos veces.',
    }));
    expect(api.setPlatformOrderId).toHaveBeenLastCalledWith('o1', { externalOrderId: 'RAPPI-000555', confirmPossibleDuplicate: true });
    expect(onSaved).toHaveBeenLastCalledWith(false);
    page.unmount();

    render(<PlatformOrderIdEditor order={order(current)} platform={OrderChannel.UberEats} canManage onSaved={onSaved} />);
    await userEvent.click(screen.getByRole('button', { name: 'Corregir el ID largo del pedido ORD-000042' }));
    await userEvent.clear(screen.getByLabelText('ID largo del pedido'));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    expect(api.setPlatformOrderId).toHaveBeenLastCalledWith('o1', { externalOrderId: '' });
    expect(onSaved).toHaveBeenLastCalledWith(true);
  });

  it('keeps the field open with the server\'s reason, and lets whoever cannot take orders only read it', async () => {
    api.setPlatformOrderId.mockRejectedValue(apiError(400, 'business_rule', 'Solo un pedido de Rappi, Uber Eats o DiDi Food lleva ID largo.'));
    const page = render(<PlatformOrderIdEditor order={order()} platform={OrderChannel.UberEats} canManage onSaved={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Agregar el ID largo del pedido ORD-000042' }));
    await userEvent.type(screen.getByLabelText('ID largo del pedido'), 'RAPPI-000555');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    expect(await screen.findByText('Solo un pedido de Rappi, Uber Eats o DiDi Food lleva ID largo.')).toBeInTheDocument();
    expect(confirm).not.toHaveBeenCalled();
    page.unmount();

    render(<PlatformOrderIdEditor order={order('RAPPI-000555')} platform={OrderChannel.UberEats} canManage={false} onSaved={vi.fn()} />);
    expect(screen.getByText('RAPPI-000555')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
