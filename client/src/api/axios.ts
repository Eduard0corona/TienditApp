import axios from 'axios';

const api = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});

// Access token in memory only (never localStorage)
let accessToken: string | null = null;

export const setAccessToken = (token: string | null) => {
  accessToken = token;
};

export const getAccessToken = () => accessToken;

export type SessionEvent = 'expired' | 'tenant-changed';

const sessionListeners = new Set<(event: SessionEvent) => void>();

/** Notifies when the session can no longer be refreshed, or a refresh moved it to another tenant. */
export function onSessionEvent(listener: (event: SessionEvent) => void) {
  sessionListeners.add(listener);
  return () => {
    sessionListeners.delete(listener);
  };
}

const emitSessionEvent = (event: SessionEvent) => {
  sessionListeners.forEach((listener) => listener(event));
};

interface TokenClaims {
  exp?: number;
  tenant_id?: string;
}

// Reads the payload without validating it: only used to schedule refreshes and detect tenant changes.
function readTokenClaims(token: string | null): TokenClaims | null {
  if (!token) return null;
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    return JSON.parse(atob(padded)) as TokenClaims;
  } catch {
    return null;
  }
}

// Refresh tokens rotate on every use: two tabs refreshing with the same cookie made one of them fail and
// end the session. The Web Locks API serializes refreshes across the tabs of this origin.
const REFRESH_LOCK_NAME = 'fpd-refresh-token';
let refreshInFlight: Promise<string> | null = null;

async function requestNewAccessToken(): Promise<string> {
  const { data } = await axios.post(
    '/api/v1/auth/refresh-token',
    {},
    {
      withCredentials: true,
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    }
  );
  return data.accessToken as string;
}

/** Single-flight refresh shared by every request, hub connection and tab. */
export function refreshAccessToken(): Promise<string> {
  if (!refreshInFlight) {
    const previousTenant = readTokenClaims(accessToken)?.tenant_id;
    const locked: Promise<string> = typeof navigator !== 'undefined' && navigator.locks
      ? navigator.locks.request(REFRESH_LOCK_NAME, () => requestNewAccessToken()).then((token) => token)
      : requestNewAccessToken();

    refreshInFlight = locked
      .then((token) => {
        accessToken = token;
        const currentTenant = readTokenClaims(token)?.tenant_id;
        // Another tab switched tenant (the refresh follows the default tenant): this tab's data is stale.
        if (previousTenant && currentTenant && previousTenant !== currentTenant) {
          emitSessionEvent('tenant-changed');
        }
        return token;
      })
      .catch((error: unknown) => {
        // A rejected refresh ends the session; a network error keeps it so the next call can retry.
        if (axios.isAxiosError(error) && error.response && error.response.status < 500) {
          accessToken = null;
          emitSessionEvent('expired');
        }
        throw error;
      })
      .finally(() => {
        refreshInFlight = null;
      });
  }

  return refreshInFlight;
}

/** Current token, refreshed first when it expires within 30 seconds (SignalR (re)connections). */
export async function getFreshAccessToken(): Promise<string | null> {
  const expiresAt = readTokenClaims(accessToken)?.exp;
  if (accessToken && expiresAt && expiresAt * 1000 - Date.now() > 30_000) {
    return accessToken;
  }

  try {
    return await refreshAccessToken();
  } catch {
    return null;
  }
}

api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;
    const url = originalRequest?.url ?? '';

    // Never auto-refresh for auth endpoints themselves (avoids loops)
    const isAuthEndpoint =
      url.includes('/auth/refresh-token') ||
      url.includes('/auth/login') ||
      url.includes('/twofactor/login');

    if (error.response?.status === 401 && originalRequest && !originalRequest._retry && !isAuthEndpoint) {
      originalRequest._retry = true;
      const newToken = await refreshAccessToken();
      originalRequest.headers.Authorization = `Bearer ${newToken}`;
      return api(originalRequest);
    }

    return Promise.reject(error);
  }
);

export default api;
