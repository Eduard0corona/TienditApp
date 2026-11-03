import { useEffect } from 'react';
import { onSessionEvent } from '../api/axios';

const CHANNEL_NAME = 'fpd-session';

type SessionMessage = { type: 'logout' } | { type: 'tenant-switched' };

function openChannel(): BroadcastChannel | null {
  return typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(CHANNEL_NAME);
}

/** Tells the other tabs of this browser about a logout or a tenant switch. */
export function broadcastSessionChange(message: SessionMessage) {
  const channel = openChannel();
  channel?.postMessage(message);
  channel?.close();
}

/**
 * Where a tenant change starts over: the start of the job (App.tsx). Not /, which shows the page for businesses while
 * the session is checked.
 */
export const TENANT_CHANGE_START_PATH = '/inicio';

/** Starts over from the start of the job: loaded lists, hub groups and drafts belong to the previous tenant. */
export function reloadForTenantChange() {
  window.location.assign(TENANT_CHANGE_START_PATH);
}

/**
 * Keeps the UI in sync with the session: a rejected refresh logs out (instead of leaving a UI that
 * looks logged in while every call fails) and a tenant change in this or another tab reloads.
 */
export function useSessionSync(clearSession: () => void) {
  useEffect(() => {
    const unsubscribe = onSessionEvent((event) => {
      if (event === 'expired') clearSession();
      else reloadForTenantChange();
    });

    const channel = openChannel();
    if (channel) {
      channel.onmessage = (message: MessageEvent<SessionMessage>) => {
        if (message.data?.type === 'logout') clearSession();
        if (message.data?.type === 'tenant-switched') reloadForTenantChange();
      };
    }

    return () => {
      unsubscribe();
      channel?.close();
    };
  }, [clearSession]);
}
