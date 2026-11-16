import { afterEach, describe, expect, it, vi } from 'vitest';
import { goToExternalPage, returnToAssistant } from './externalNavigation';

describe('goToExternalPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('leaves only for an https page', () => {
    const assign = vi.fn();
    vi.stubGlobal('location', { assign });

    goToExternalPage('https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123');
    expect(assign).toHaveBeenCalledWith('https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123');

    for (const url of ['javascript:alert(1)', 'http://www.mercadopago.com.mx/checkout', '/m/cafe-luna', ''])
      expect(() => goToExternalPage(url)).toThrow('La página de pago no es segura.');
    expect(assign).toHaveBeenCalledTimes(1);
  });
});

describe('returnToAssistant (Fase 32E)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('goes back to an https page or a desktop app on this device, and nowhere else', () => {
    const assign = vi.fn();
    vi.stubGlobal('location', { assign });

    for (const url of [
      'https://claude.ai/api/mcp/auth_callback?code=c0de&state=xyz',
      'http://localhost:6274/oauth/callback?code=c0de',
      'http://127.0.0.1:33418/callback?error=access_denied',
      'http://[::1]:8080/callback?code=c0de',
    ]) {
      returnToAssistant(url);
      expect(assign).toHaveBeenLastCalledWith(url);
    }

    assign.mockClear();
    for (const url of ['javascript:alert(1)', 'http://evil.example/callback', 'data:text/html,hola', 'no es una dirección']) {
      expect(() => returnToAssistant(url)).toThrow();
    }
    expect(assign).not.toHaveBeenCalled();
  });
});
