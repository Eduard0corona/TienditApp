import { afterEach, describe, expect, it, vi } from 'vitest';
import { goToExternalPage } from './externalNavigation';

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
