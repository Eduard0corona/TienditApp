import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection, type IRetryPolicy } from '@microsoft/signalr';
import { getFreshAccessToken } from './axios';

// Retries forever with a capped backoff. The default policy gives up after ~42 seconds and the boards
// stayed without realtime updates until the page was reloaded.
const retryForever: IRetryPolicy = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
    Math.min(30_000, 1_000 * 2 ** Math.min(previousRetryCount, 5)),
};

export function createHubConnection(url: string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(url, {
      // Every (re)connection gets a valid token: the server closes hubs when the token expires.
      accessTokenFactory: async () => (await getFreshAccessToken()) ?? '',
    })
    .withAutomaticReconnect(retryForever)
    .configureLogging(LogLevel.Warning)
    .build();
}

/**
 * Starts the connection and keeps it alive until <paramref name="isDisposed"/> returns true: the first
 * start is retried (automatic reconnect only covers connections that were established) and a closed
 * connection (e.g. the server ended it because the token expired) is started again.
 *
 * <paramref name="onConnected"/> runs every time the connection is (re)established, including automatic
 * reconnects: events sent while it was down are lost, so screens use it to reload their data.
 */
export function keepHubConnected(
  connection: HubConnection,
  hubName: string,
  isDisposed: () => boolean,
  onConnected?: () => void,
) {
  let attempt = 0;
  let retryTimeoutId: number | undefined;

  const notifyConnected = () => {
    if (!isDisposed()) onConnected?.();
  };

  const start = async () => {
    if (isDisposed() || connection.state !== HubConnectionState.Disconnected) return;
    try {
      await connection.start();
      attempt = 0;
      notifyConnected();
    } catch (err) {
      if (isDisposed()) return;
      const message = err instanceof Error ? err.message : String(err);
      // In React StrictMode the effect is mounted/unmounted twice in dev; ignore that teardown.
      if (!message.toLowerCase().includes('stopped during negotiation')) {
        console.warn(`${hubName} connection failed, retrying.`, err);
      }
      scheduleStart();
    }
  };

  const scheduleStart = () => {
    const delay = retryForever.nextRetryDelayInMilliseconds({
      previousRetryCount: attempt++,
      elapsedMilliseconds: 0,
      retryReason: new Error('start'),
    }) ?? 30_000;
    retryTimeoutId = window.setTimeout(() => void start(), delay);
  };

  connection.onclose(() => {
    if (!isDisposed()) scheduleStart();
  });
  connection.onreconnected(notifyConnected);

  // Delay start one tick to avoid StrictMode mount/unmount negotiation races in dev.
  retryTimeoutId = window.setTimeout(() => void start(), 0);

  return () => {
    if (retryTimeoutId !== undefined) window.clearTimeout(retryTimeoutId);
    void connection.stop().catch(() => undefined);
  };
}
