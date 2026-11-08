import { act, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ToastProvider from '../components/feedback/ToastProvider';
import { useAttention } from '../api/queries';
import { describeWaiting, useOnlineOrdersWaitingLive } from './useOnlineOrdersWaiting';

vi.mock('../api/queries', () => ({ useAttention: vi.fn() }));
const chime = vi.hoisted(() => ({ play: vi.fn() }));
vi.mock('./useArrivalChime', () => ({ useArrivalChime: () => ({ play: chime.play }) }));

function withWaiting(urgentCount: number | undefined) {
  const items = urgentCount === undefined ? [] : [{ key: 'onlineOrdersToConfirm', count: urgentCount + 1, urgentCount }];
  vi.mocked(useAttention).mockReturnValue({ data: { items } } as unknown as ReturnType<typeof useAttention>);
}

function Live({ enabled = true }: { enabled?: boolean }) {
  useOnlineOrdersWaitingLive(enabled);
  return null;
}

const renderLive = () => render(<ToastProvider><Live /></ToastProvider>);

describe('the online orders waiting 15 minutes, on any screen (Fase 29X)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('tells nothing of those already waiting when the screen opens', () => {
    withWaiting(2);
    renderLive();

    expect(screen.queryByText(/sin confirmar/)).not.toBeInTheDocument();
    expect(chime.play).not.toHaveBeenCalled();
  });

  it('tells and rings when one more reaches 15 minutes, and not when one is answered', () => {
    withWaiting(0);
    const { rerender } = renderLive();

    withWaiting(1);
    act(() => rerender(<ToastProvider><Live /></ToastProvider>));
    expect(screen.getByText(describeWaiting(1).message)).toBeInTheDocument();
    expect(chime.play).toHaveBeenCalledWith('arrival');

    chime.play.mockClear();
    withWaiting(0);
    act(() => rerender(<ToastProvider><Live /></ToastProvider>));
    expect(chime.play).not.toHaveBeenCalled();

    // Gone from the bell (none waiting) and back: a new one, told again.
    withWaiting(undefined);
    act(() => rerender(<ToastProvider><Live /></ToastProvider>));
    withWaiting(3);
    act(() => rerender(<ToastProvider><Live /></ToastProvider>));
    expect(screen.getByText(describeWaiting(3).message)).toBeInTheDocument();
  });

  it('says how many wait, and when they count among the slowest', () => {
    expect(describeWaiting(1).message)
      .toBe('Un pedido en línea lleva 15 minutos sin confirmar. A la media hora cuenta entre los más lentos en Explorar.');
    expect(describeWaiting(2).message)
      .toBe('2 pedidos en línea llevan 15 minutos o más sin confirmar. A la media hora cuentan entre los más lentos en Explorar.');
  });
});
