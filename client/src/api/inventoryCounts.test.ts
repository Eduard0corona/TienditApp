import { act, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { InventoryCountStatus } from './generated/enums';
import { countKeys, useAddInventoryCountItems, useCancelInventoryCount, type InventoryCount } from './inventoryCounts';
import { apiRequest } from './typedClient';

vi.mock('./typedClient', () => ({ apiRequest: vi.fn() }));

const COUNT = {
  id: 'c1', countNumber: 'CNT-20261021-0001', inventoryLocationId: 'l1', inventoryLocationName: 'Bodega',
  status: InventoryCountStatus.InProgress, startedAt: '2026-10-21T18:00:00Z', startedBy: 'dueno', categoryName: null,
  lineCount: 3, countedLineCount: 2, differenceLineCount: 1, surplusValue: 0, shortageValue: 24, revision: 7,
} as InventoryCount;

function setup(cached: InventoryCount) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  client.setQueryData(countKeys.count('c1'), cached);
  const wrapper = ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
  return { client, wrapper };
}

describe('the answers of a count', () => {
  beforeEach(() => {
    vi.mocked(apiRequest).mockReset();
  });

  it('shows what adding answered, unless a save of what was counted already took the count further', async () => {
    const { client, wrapper } = setup(COUNT);
    const add = renderHook(() => useAddInventoryCountItems('c1'), { wrapper }).result;

    // The add was done before the save that brought revision 7, but its answer arrives after it.
    vi.mocked(apiRequest).mockResolvedValueOnce({ ...COUNT, lineCount: 4, revision: 6 } as never);
    await act(() => add.current.mutateAsync(['s9']));
    expect(client.getQueryData<InventoryCount>(countKeys.count('c1'))).toMatchObject({ revision: 7, lineCount: 3 });

    vi.mocked(apiRequest).mockResolvedValueOnce({ ...COUNT, lineCount: 4, revision: 8 } as never);
    await act(() => add.current.mutateAsync(['s9']));
    expect(client.getQueryData<InventoryCount>(countKeys.count('c1'))).toMatchObject({ revision: 8, lineCount: 4 });
  });

  it('shows the count cancelled with the answer, unless it is older than the one shown', async () => {
    const { client, wrapper } = setup(COUNT);
    const cancel = renderHook(() => useCancelInventoryCount('c1'), { wrapper }).result;

    vi.mocked(apiRequest).mockResolvedValueOnce({ ...COUNT, status: InventoryCountStatus.Cancelled, revision: 6 } as never);
    await act(() => cancel.current.mutateAsync(''));
    expect(client.getQueryData<InventoryCount>(countKeys.count('c1'))).toMatchObject({ revision: 7, status: InventoryCountStatus.InProgress });

    vi.mocked(apiRequest).mockResolvedValueOnce({ ...COUNT, status: InventoryCountStatus.Cancelled, revision: 8 } as never);
    await act(() => cancel.current.mutateAsync(''));
    expect(client.getQueryData<InventoryCount>(countKeys.count('c1'))).toMatchObject({ revision: 8, status: InventoryCountStatus.Cancelled });
  });
});
