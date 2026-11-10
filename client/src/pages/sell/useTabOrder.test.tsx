import { renderHook, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import type { OrderDto } from '../../types';
import { useTabOrder } from './useTabOrder';

vi.mock('../../api/ordersApi', () => ({ ordersApi: { getById: vi.fn() } }));

const round = { kind: 'round' as const, orderId: 'cuenta-1', label: 'Mesa 2' };

describe('useTabOrder', () => {
  beforeEach(() => vi.mocked(ordersApi.getById).mockClear());

  it('reads what the open tab of a round has, once, whatever the callback', async () => {
    vi.mocked(ordersApi.getById).mockResolvedValue({ data: { id: 'cuenta-1' } as OrderDto } as AxiosResponse<OrderDto>);
    const { result, rerender } = renderHook(({ onError }) => useTabOrder(round, onError), { initialProps: { onError: vi.fn() } });

    await waitFor(() => expect(result.current?.id).toBe('cuenta-1'));
    rerender({ onError: vi.fn() });
    expect(ordersApi.getById).toHaveBeenCalledTimes(1);
    expect(ordersApi.getById).toHaveBeenCalledWith('cuenta-1');
  });

  it('reads nothing for a new tab or a counter sale', () => {
    renderHook(() => useTabOrder({ kind: 'new', label: 'Mesa 2' }, vi.fn()));
    renderHook(() => useTabOrder(null, vi.fn()));
    expect(ordersApi.getById).not.toHaveBeenCalled();
  });

  it('says a tab that cannot be read, unless the screen left first', async () => {
    const notFound = () => new AxiosError('Not found', 'ERR_BAD_REQUEST', undefined, undefined, {
      status: 404, data: { detail: 'No encontramos esa cuenta.' }, headers: {}, statusText: '', config: { headers: new AxiosHeaders() },
    } as AxiosResponse);

    let refuse: (reason: unknown) => void = () => undefined;
    vi.mocked(ordersApi.getById).mockReturnValueOnce(new Promise((_, reject) => { refuse = reject; }));
    const ofTheScreenThatLeft = vi.fn();
    renderHook(() => useTabOrder(round, ofTheScreenThatLeft)).unmount();
    refuse(notFound());
    // Long enough for the answer to arrive.
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(ofTheScreenThatLeft).not.toHaveBeenCalled();

    vi.mocked(ordersApi.getById).mockRejectedValueOnce(notFound());
    const onError = vi.fn();
    renderHook(() => useTabOrder(round, onError));
    await waitFor(() => expect(onError).toHaveBeenCalledWith('No encontramos esa cuenta.'));
  });
});
