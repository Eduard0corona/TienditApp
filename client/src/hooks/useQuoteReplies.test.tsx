import { act, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ToastProvider from '../components/feedback/ToastProvider';
import { queryKeys } from '../api/queries';
import { describeAnswer, useQuoteRepliesLive, type QuoteAnsweredPayload } from './useQuoteReplies';

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

const answer = (overrides: Partial<QuoteAnsweredPayload> = {}): QuoteAnsweredPayload => ({
  quoteId: 'q1',
  number: 'COT-000007',
  revision: 1,
  accepted: true,
  ...overrides,
});

function Live({ enabled = true, tenantId = 't1' }: { enabled?: boolean; tenantId?: string }) {
  useQuoteRepliesLive(enabled, tenantId);
  return null;
}

describe('the answers to quotes, live (Fase 29R)', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    hub.handlers.clear();
    hub.onConnected = undefined;
    hub.urls = [];
    hub.stop.mockReset();
    queryClient = new QueryClient();
  });

  const renderLive = (props?: { enabled?: boolean; tenantId?: string }) =>
    render(
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <Live {...props} />
        </ToastProvider>
      </QueryClientProvider>,
    );

  /** SignalR loads apart: the connection exists once its module arrives. */
  const connected = () => waitFor(() => expect(hub.handlers.has('QuoteAnswered')).toBe(true));

  it('tells an accepted quote, and the bell, the list and the quote on screen read it at once, not its settings', async () => {
    const list = vi.fn(async () => 'lista');
    const detail = vi.fn(async () => 'detalle');
    const settings = vi.fn(async () => 'ajustes');
    // Three screens that read the quotes, each observed as a mounted page would.
    const observers = [
      queryClient.getQueryCache().build(queryClient, { queryKey: ['quotes', 'all', '', 1], queryFn: list }),
      queryClient.getQueryCache().build(queryClient, { queryKey: ['quotes', 'detail', 'q1'], queryFn: detail }),
      queryClient.getQueryCache().build(queryClient, { queryKey: ['quotes', 'settings'], queryFn: settings }),
    ];
    await Promise.all(observers.map((query) => query.fetch()));
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    renderLive();
    await connected();

    act(() => hub.handlers.get('QuoteAnswered')?.(answer()));

    expect(hub.urls).toEqual(['/hubs/quotes']);
    expect(screen.getByText('Cotización aceptada')).toBeInTheDocument();
    expect(screen.getByText('Te aceptaron la cotización COT-000007. Ábrela en Cotizaciones para seguir.')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.attention });
    expect(queryClient.getQueryState(['quotes', 'all', '', 1])?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(['quotes', 'detail', 'q1'])?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(['quotes', 'settings'])?.isInvalidated).toBe(false);
  });

  it('tells a rejected quote with its version, and reads again when the connection comes back', async () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    renderLive();
    await connected();

    act(() => hub.handlers.get('QuoteAnswered')?.(answer({ accepted: false, revision: 2 })));
    act(() => hub.onConnected?.());

    expect(screen.getByText('Cotización rechazada')).toBeInTheDocument();
    expect(
      screen.getByText('Te rechazaron la cotización COT-000007 · versión 2. Puedes corregirla y mandarla otra vez.'),
    ).toBeInTheDocument();
    // The bell and the quotes, once with the answer and once when it came back.
    expect(invalidate).toHaveBeenCalledTimes(4);
  });

  it('does not connect for whoever does not work them, nor without a business, and stops when it goes', async () => {
    renderLive({ enabled: false });
    renderLive({ tenantId: '' });

    const { unmount } = renderLive();
    await connected();
    unmount();

    expect(hub.urls).toEqual(['/hubs/quotes']);
    expect(hub.stop).toHaveBeenCalledTimes(1);
  });

  it('never connects when it goes before SignalR arrives', async () => {
    const { unmount } = renderLive();
    unmount();
    await import('../api/signalr');
    await Promise.resolve();

    expect(hub.urls).toEqual([]);
    expect(hub.stop).not.toHaveBeenCalled();
  });

  it('says the folio as the quotes do', () => {
    expect(describeAnswer(answer()).title).toBe('Cotización aceptada');
    expect(describeAnswer(answer({ revision: 3 })).message).toContain('COT-000007 · versión 3');
  });
});
