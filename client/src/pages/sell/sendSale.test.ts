import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import type { CreateOrderRequest, OrderDto } from '../../types';
import { OrderChannel } from '../../types';
import { sendSale } from './sendSale';

vi.mock('../../api/ordersApi', () => ({ ordersApi: { create: vi.fn() } }));

function refusal(code: string, detail: string): AxiosError {
  const response = { status: 409, data: { code, detail }, headers: {}, statusText: '', config: { headers: new AxiosHeaders() } };
  return new AxiosError(detail, 'ERR_BAD_REQUEST', undefined, undefined, response as AxiosResponse);
}

const order = { id: 'o1', orderNumber: 'ORD-000123' } as OrderDto;
const rappi: CreateOrderRequest = {
  items: [{ productId: 'taco', quantity: 1 }], channel: OrderChannel.Rappi, externalOrderNumber: 'BC953', idempotencyKey: 'k1',
} as CreateOrderRequest;

describe('sendSale', () => {
  beforeEach(() => vi.mocked(ordersApi.create).mockReset());

  it('records the sale as it is', async () => {
    vi.mocked(ordersApi.create).mockResolvedValue({ data: order } as AxiosResponse<OrderDto>);
    const confirm = vi.fn();

    await expect(sendSale(rappi, confirm)).resolves.toBe(order);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('asks about a folio of a platform already registered today, and sends it again confirmed with the same key', async () => {
    vi.mocked(ordersApi.create)
      .mockRejectedValueOnce(refusal('possible_duplicate_order', 'Ya registraste ese folio hoy.'))
      .mockResolvedValueOnce({ data: order } as AxiosResponse<OrderDto>);
    const confirm = vi.fn().mockResolvedValue(true);

    await expect(sendSale(rappi, confirm)).resolves.toBe(order);
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
      title: '¿Es otro pedido?',
      message: 'Hoy ya registraste un pedido de Rappi con el folio BC953. '
        + 'Los folios cortos se repiten: si es otro pedido, regístralo; si no, búscalo en Pedidos.',
      confirmLabel: 'Registrar de todos modos',
      cancelLabel: 'Revisar',
    }));
    expect(ordersApi.create).toHaveBeenLastCalledWith({ ...rappi, confirmPossibleDuplicate: true });
  });

  it('says what the server says when the long id may repeat too', async () => {
    vi.mocked(ordersApi.create).mockRejectedValueOnce(refusal('possible_duplicate_order', 'Ya registraste un pedido con ese ID largo.'));
    const confirm = vi.fn().mockResolvedValue(false);

    await expect(sendSale({ ...rappi, externalOrderId: '9C0B' }, confirm)).resolves.toBeNull();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'Ya registraste un pedido con ese ID largo. Si no, búscalo en Pedidos.' }));
    // The cashier goes to check: nothing else is sent.
    expect(ordersApi.create).toHaveBeenCalledTimes(1);
  });

  it('lets any other refusal through, and the one of a sale already confirmed', async () => {
    const other = refusal('price_changed', 'Cambió un precio.');
    vi.mocked(ordersApi.create).mockRejectedValueOnce(other);
    await expect(sendSale(rappi, vi.fn())).rejects.toBe(other);

    const again = refusal('possible_duplicate_order', 'Ya registraste ese folio hoy.');
    vi.mocked(ordersApi.create).mockRejectedValueOnce(again);
    const confirm = vi.fn();
    await expect(sendSale({ ...rappi, confirmPossibleDuplicate: true }, confirm)).rejects.toBe(again);
    expect(confirm).not.toHaveBeenCalled();
  });
});
