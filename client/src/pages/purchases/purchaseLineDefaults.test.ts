import { describe, expect, it } from 'vitest';
import { getSupplyLineDefaults } from './purchaseLineDefaults';

describe('purchase line defaults', () => {
  it('starts with the last price paid and the tax of the supply', () => {
    expect(getSupplyLineDefaults({ unitCost: '0', taxPercent: '0' }, { lastCost: 360, defaultCost: 380, taxRate: 16 }))
      .toEqual({ unitCost: '360', taxPercent: '16' });
  });

  it('uses the default cost of a supply never bought', () => {
    expect(getSupplyLineDefaults({ unitCost: '', taxPercent: '0' }, { lastCost: 0, defaultCost: 92.5, taxRate: undefined }))
      .toEqual({ unitCost: '92.5' });
  });

  it('keeps what the person already typed', () => {
    expect(getSupplyLineDefaults({ unitCost: '350', taxPercent: '8' }, { lastCost: 360, defaultCost: 380, taxRate: 16 }))
      .toEqual({});
  });
});
