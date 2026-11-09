import { describe, expect, it } from 'vitest';
import { countActiveFilters } from './countActiveFilters';

describe('countActiveFilters', () => {
  it('counts the filters with a value: not empty, null, undefined or a switch that is off', () => {
    expect(countActiveFilters('', null, undefined, false)).toBe(0);
    expect(countActiveFilters('2026-10-04', 'abc', true)).toBe(3);
  });

  it('counts zero, the first value of many enums (OrderStatus.Pending)', () => {
    expect(countActiveFilters(0, '')).toBe(1);
  });
});
