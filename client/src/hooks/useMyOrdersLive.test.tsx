import { render, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useMyOrdersLive } from './useMyOrdersLive';

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (payload: unknown) => void>(),
  onConnected: undefined as (() => void) | undefined,
  stop: vi.fn(),
  urls: [] as string[],
}));

vi.mock('../api/signalr', () => ({
  createHubConnection: (url: string) => {
    hub.urls.push(url);
    return { on: (name: string, handler: (payload: unknown) => void) => hub.handlers.set(name, handler) };
  },
  keepHubConnected: (_connection: unknown, _name: string, _isDisposed: () => boolean, onConnected?: () => void) => {
    hub.onConnected = onConnected;
    return hub.stop;
  },
}));

function Live() {
  useMyOrdersLive();
  return null;
}

describe('«Mis pedidos» live (Fase 31A)', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    hub.handlers.clear();
    hub.onConnected = undefined;
    hub.urls = [];
    hub.stop.mockReset();
    queryClient = new QueryClient();
  });

  const renderLive = () => render(<QueryClientProvider client={queryClient}><Live /></QueryClientProvider>);

  /** SignalR loads apart: the connection exists once its module arrives. */
  const connected = () => waitFor(() => expect(hub.handlers.has('MyOrderChanged')).toBe(true));

  /** What the screens read: the account's orders, and what a business reads, each read once. */
  const readAll = async () => {
    const queries = [
      queryClient.getQueryCache().build(queryClient, { queryKey: ['me', 'orders', 'detail', 'o1'], queryFn: async () => 'detalle' }),
      queryClient.getQueryCache().build(queryClient, { queryKey: ['me', 'orders', 'in-progress'], queryFn: async () => 'en curso' }),
      queryClient.getQueryCache().build(queryClient, { queryKey: ['orders', 'list'], queryFn: async () => 'pedidos' }),
    ];
    await Promise.all(queries.map((query) => query.fetch()));
  };
  const invalidated = (queryKey: unknown[]) => queryClient.getQueryState(queryKey)?.isInvalidated;

  it('reads the orders of the account again when one of them changes, and nothing of a business', async () => {
    await readAll();
    renderLive();
    await connected();
    expect(hub.urls).toEqual(['/hubs/my-orders']);

    hub.handlers.get('MyOrderChanged')?.({ orderId: 'o1' });

    expect(invalidated(['me', 'orders', 'detail', 'o1'])).toBe(true);
    expect(invalidated(['me', 'orders', 'in-progress'])).toBe(true);
    // What a business reads is not the account's.
    expect(invalidated(['orders', 'list'])).toBe(false);
  });

  it('reads them again when it connects, for what changed while it was down', async () => {
    await readAll();
    renderLive();
    await connected();
    expect(invalidated(['me', 'orders', 'in-progress'])).toBe(false);

    hub.onConnected?.();

    expect(invalidated(['me', 'orders', 'in-progress'])).toBe(true);
    expect(invalidated(['orders', 'list'])).toBe(false);
  });

  it('does not connect when the screen went away before SignalR arrived', async () => {
    const page = renderLive();
    page.unmount();

    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(hub.urls).toEqual([]);
  });

  it('stops listening when the screen goes away', async () => {
    const page = renderLive();
    await connected();

    page.unmount();

    expect(hub.stop).toHaveBeenCalledTimes(1);
  });
});
