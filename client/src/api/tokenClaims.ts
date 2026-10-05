export interface TokenClaims {
  exp?: number;
  tenant_id?: string;
  /** The session (sign-in) of the token; the API stops accepting it when the session ends. */
  session_id?: string;
}

// Reads the payload without validating it: only used to schedule refreshes and detect tenant changes.
export function readTokenClaims(token: string | null): TokenClaims | null {
  if (!token) return null;
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    return JSON.parse(atob(padded)) as TokenClaims;
  } catch {
    return null;
  }
}

/**
 * Whether a token can open a live connection as it is: it belongs to a session (one issued before sessions existed is
 * renewed first, since the API no longer accepts it) and lasts at least 30 more seconds.
 */
export function isFreshForConnection(token: string | null, nowMs: number): boolean {
  const claims = readTokenClaims(token);
  return !!claims?.session_id && !!claims.exp && claims.exp * 1000 - nowMs > 30_000;
}
