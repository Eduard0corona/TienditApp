import { describe, expect, it } from 'vitest';
import { OrderChannel, PlatformCommissionBase } from '../../types';
import {
  bodyOf,
  describeTerms,
  draftOf,
  draftProblem,
  formatPercent,
  parsePercent,
  savedMessage,
  type PlatformSetting,
} from './deliveryPlatformTerms';

const rappi: PlatformSetting = {
  channel: OrderChannel.Rappi,
  isConfigured: true,
  isActive: true,
  commissionPercent: 30,
  commissionBase: PlatformCommissionBase.TotalWithTax,
  commissionVatPercent: 16,
  isrWithholdingPercent: 2.5,
  vatWithholdingPercent: 50,
  revision: 2,
};

describe('delivery platform terms', () => {
  it('writes a percentage as the API keeps it, never split from its sign', () => {
    expect(formatPercent(30)).toBe('30\u00a0%');
    expect(formatPercent(2.5)).toBe('2.5\u00a0%');
    expect(formatPercent(27.25)).toBe('27.25\u00a0%');
  });

  it('reads what a person types as a percentage, and nothing else', () => {
    expect(parsePercent('30')).toBe(30);
    expect(parsePercent(' 2.5 % ')).toBe(2.5);
    expect(parsePercent('2,5')).toBe(2.5);
    expect(parsePercent('16.')).toBe(16);
    expect(parsePercent('')).toBeNull();
    expect(parsePercent('-1')).toBeNull();
    expect(parsePercent('2.555')).toBeNull();
    expect(parsePercent('treinta')).toBeNull();
  });

  it('sums the terms up the way the row shows them', () => {
    expect(describeTerms(rappi).replace(/\u00a0/g, ' ')).toBe('Comisión 30 % sobre el total con IVA + IVA 16 % · Retiene ISR 2.5 % e IVA 50 %');
    expect(describeTerms({ ...rappi, commissionBase: PlatformCommissionBase.SubTotalWithoutTax, commissionPercent: 25 }).replace(/\u00a0/g, ' '))
      .toBe('Comisión 25 % sobre el subtotal sin IVA + IVA 16 % · Retiene ISR 2.5 % e IVA 50 %');
  });

  it('starts a platform never saved on, without a commission, and saves it with revision 0', () => {
    const never: PlatformSetting = { ...rappi, isConfigured: false, isActive: false, commissionPercent: null, revision: 0 };
    const draft = draftOf(never);

    expect(draft).toEqual({
      isActive: true,
      commissionPercent: '',
      commissionBase: PlatformCommissionBase.TotalWithTax,
      commissionVatPercent: '16',
      isrWithholdingPercent: '2.5',
      vatWithholdingPercent: '50',
    });
    expect(draftProblem(draft)).toEqual({ field: 'commission', message: 'Escribe la comisión de tu contrato.' });
    expect(bodyOf(never, { ...draft, commissionPercent: '30' })).toEqual({
      revision: 0,
      isActive: true,
      commissionPercent: 30,
      commissionBase: PlatformCommissionBase.TotalWithTax,
      commissionVatPercent: 16,
      isrWithholdingPercent: 2.5,
      vatWithholdingPercent: 50,
    });
  });

  it('says what saving changed for Vender', () => {
    const draft = draftOf(rappi);

    expect(savedMessage(rappi, draft)).toBe('Rappi guardada.');
    expect(savedMessage(rappi, { ...draft, isActive: false })).toBe('Rappi ya no aparece en Vender.');
    expect(savedMessage({ ...rappi, isActive: false }, draft)).toBe('Rappi quedó activa.');
  });
});
