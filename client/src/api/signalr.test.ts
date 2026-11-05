import { HubConnectionState, type HubConnection } from '@microsoft/signalr';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { STABLE_CONNECTION_MS, keepHubConnected } from './signalr';

/** A connection the server accepts each time, and closes when the test says. */
function fakeConnection() {
  let state = HubConnectionState.Disconnected;
  let onClose: (() => void) | undefined;
  const starts: number[] = [];
  const connection = {
    get state() {
      return state;
    },
    start: vi.fn(async () => {
      starts.push(Date.now());
      state = HubConnectionState.Connected;
    }),
    stop: vi.fn(async () => {
      state = HubConnectionState.Disconnected;
    }),
    onclose: (callback: () => void) => {
      onClose = callback;
    },
    onreconnected: vi.fn(),
  };
  return {
    connection: connection as unknown as HubConnection,
    starts,
    /** The server closes it (as it does when it refuses it once connected). */
    close: () => {
      state = HubConnectionState.Disconnected;
      onClose?.();
    },
  };
}

describe('keepHubConnected (Fase 29N)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-11-05T17:00:00Z'));
  });

  afterEach(() => vi.useRealTimers());

  it('backs off when the server closes each connection right after accepting it, and starts over after one that stayed up', async () => {
    const hub = fakeConnection();
    const connected = vi.fn();
    const stop = keepHubConnected(hub.connection, 'Prueba', () => false, connected);

    await vi.advanceTimersByTimeAsync(0);
    const waits: number[] = [];
    for (let refused = 0; refused < 4; refused++) {
      const before = hub.starts.length;
      const closedAt = Date.now();
      hub.close();
      // The next start comes after a wait that doubles each time.
      while (hub.starts.length === before) await vi.advanceTimersByTimeAsync(250);
      waits.push(hub.starts[hub.starts.length - 1] - closedAt);
    }
    expect(waits).toEqual([1_000, 2_000, 4_000, 8_000]);

    // This one stays up: closing it starts over from the shortest wait.
    await vi.advanceTimersByTimeAsync(STABLE_CONNECTION_MS);
    const closedAt = Date.now();
    const before = hub.starts.length;
    hub.close();
    while (hub.starts.length === before) await vi.advanceTimersByTimeAsync(250);
    expect(hub.starts[hub.starts.length - 1] - closedAt).toBe(1_000);
    expect(connected).toHaveBeenCalledTimes(6);

    stop();
  });

  it('stops trying once the screen goes', async () => {
    const hub = fakeConnection();
    let disposed = false;
    const stop = keepHubConnected(hub.connection, 'Prueba', () => disposed);
    await vi.advanceTimersByTimeAsync(0);

    disposed = true;
    stop();
    hub.close();
    await vi.advanceTimersByTimeAsync(60_000);

    expect(hub.starts).toHaveLength(1);
  });
});
