import { describe, expect, it } from 'vitest';
import { UnitType } from '../../types';
import { formatDifference, parseCountedQuantity } from './countEntry';

describe('parseCountedQuantity', () => {
  it('takes zero, whole pieces and up to three decimals of what is weighed', () => {
    expect(parseCountedQuantity('0', UnitType.Unit)).toEqual({ value: 0 });
    expect(parseCountedQuantity(' 24 ', UnitType.Pack)).toEqual({ value: 24 });
    expect(parseCountedQuantity('1,255', UnitType.Kg)).toEqual({ value: 1.255 });
    expect(parseCountedQuantity('.5', UnitType.Liter)).toEqual({ value: 0.5 });
    expect(parseCountedQuantity('9999999', UnitType.Unit)).toEqual({ value: 9_999_999 });
  });

  it('leaves the item uncounted when the box is empty', () => {
    expect(parseCountedQuantity('   ', UnitType.Unit)).toEqual({ value: null });
  });

  it('says why it does not take a quantity, as the API does', () => {
    const pieces = { error: 'La cantidad contada va de 0 a 9,999,999, en piezas enteras.' };
    const weighed = { error: 'La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales.' };
    expect(parseCountedQuantity('2.5', UnitType.Unit)).toEqual(pieces);
    expect(parseCountedQuantity('-1', UnitType.Unit)).toEqual(pieces);
    expect(parseCountedQuantity('diez', UnitType.Unit)).toEqual(pieces);
    expect(parseCountedQuantity('10000000', UnitType.Kg)).toEqual(weighed);
    expect(parseCountedQuantity('1.2345', UnitType.Kg)).toEqual(weighed);
  });
});

describe('formatDifference', () => {
  it('signs what was found over or missing from what the system had', () => {
    expect(formatDifference(0.5, UnitType.Kg)).toBe('+0.5 kg');
    expect(formatDifference(-2, UnitType.Unit)).toBe('−2');
    expect(formatDifference(0, UnitType.Unit)).toBe('0');
  });
});
