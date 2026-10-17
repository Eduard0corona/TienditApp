import { describe, expect, it } from 'vitest';
import { localPhone } from './phones';

describe('localPhone', () => {
  it('writes a Mexican number with its 10 digits and leaves anything else as it comes', () => {
    expect(localPhone('+525522107765')).toBe('5522107765');
    expect(localPhone('+5215522107765')).toBe('5522107765');
    expect(localPhone('+14155552671')).toBe('+14155552671');
    expect(localPhone(' 55 2210 7765 ')).toBe('55 2210 7765');
    expect(localPhone(null)).toBe('');
  });
});
