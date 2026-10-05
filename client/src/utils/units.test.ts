import { describe, expect, it } from 'vitest';
import { UnitType } from '../types';
import { allowsFractions, formatQuantity, formatStockQuantity, lineAmount, parseQuantity } from './units';

describe('units', () => {
  it('sells weight with up to three decimals and pieces whole', () => {
    expect(allowsFractions(UnitType.Kg)).toBe(true);
    expect(allowsFractions(UnitType.Unit)).toBe(false);
    expect(parseQuantity('1,25', UnitType.Kg)).toBe(1.25);
    expect(parseQuantity('1.2345', UnitType.Kg)).toBeNull();
    expect(parseQuantity('1.5', UnitType.Unit)).toBeNull();
    expect(parseQuantity('0', UnitType.Unit)).toBeNull();
  });

  it('shows quantities with their unit', () => {
    expect(formatQuantity(1.255, UnitType.Kg)).toBe('1.255 kg');
    expect(formatQuantity(3, UnitType.Unit)).toBe('3');
    expect(formatStockQuantity(0.0125, UnitType.Kg)).toBe('0.0125 kg');
    expect(formatStockQuantity(1250.5)).toBe('1,250.5');
  });

  it('rounds a weighed line once to cents, half to even like the API', () => {
    // 0.125 kg at $10.00 is $1.25 exactly; 0.105 kg at $25.00 is $2.625, which rounds to $2.62.
    expect(lineAmount(10, 0.125)).toBe(1.25);
    expect(lineAmount(25, 0.105)).toBe(2.62);
  });
});
