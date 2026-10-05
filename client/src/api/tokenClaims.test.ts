import { describe, expect, it } from 'vitest';
import { isFreshForConnection, readTokenClaims } from './tokenClaims';

const now = Date.UTC(2026, 9, 5, 12, 0, 0);

function tokenWith(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `eyJhbGciOiJIUzI1NiJ9.${payload}.firma`;
}

describe('readTokenClaims', () => {
  it('reads the claims of the payload', () => {
    const claims = readTokenClaims(tokenWith({ exp: 123, tenant_id: 'negocio', session_id: 'sesion' }));

    expect(claims).toEqual({ exp: 123, tenant_id: 'negocio', session_id: 'sesion' });
  });

  it('returns nothing for a value that is not a token', () => {
    expect(readTokenClaims(null)).toBeNull();
    expect(readTokenClaims('no-es-un-token')).toBeNull();
  });
});

describe('isFreshForConnection', () => {
  const inAnHour = now / 1000 + 3600;

  it('accepts a token of a session that lasts more than 30 seconds', () => {
    expect(isFreshForConnection(tokenWith({ exp: inAnHour, session_id: 'sesion' }), now)).toBe(true);
  });

  it('renews a token about to expire', () => {
    expect(isFreshForConnection(tokenWith({ exp: now / 1000 + 20, session_id: 'sesion' }), now)).toBe(false);
  });

  it('renews a token issued before sessions existed, which the API no longer accepts', () => {
    expect(isFreshForConnection(tokenWith({ exp: inAnHour }), now)).toBe(false);
  });

  it('renews when there is no token', () => {
    expect(isFreshForConnection(null, now)).toBe(false);
  });
});
