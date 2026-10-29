import { describe, expect, it } from 'vitest';
import { OrderWriteOffReason } from '../types';
import { WRITE_OFF_REASONS, getWriteOffReasonLabel } from './writeOffReasons';

describe('write-off reasons', () => {
  it('names every reason of the contract, the discount included (Fase 28J)', () => {
    const reasons = Object.values(OrderWriteOffReason).filter((value): value is OrderWriteOffReason => typeof value === 'number');
    expect(WRITE_OFF_REASONS.map((option) => option.value).sort()).toEqual([...reasons].sort());
    expect(getWriteOffReasonLabel(OrderWriteOffReason.Discount)).toBe('Descuento o bonificación');
    expect(getWriteOffReasonLabel(OrderWriteOffReason.Courtesy)).toBe('Cortesía');
  });
});
