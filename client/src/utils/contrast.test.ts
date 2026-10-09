import { describe, expect, it } from 'vitest';
import { contrastRatio, relativeLuminance } from './contrast';

describe('contrastRatio', () => {
  it('measures as WCAG does, in either order', () => {
    expect(contrastRatio('#ffffff', '#000000')).toBeCloseTo(21, 5);
    expect(contrastRatio('#777777', '#777777')).toBe(1);
    expect(contrastRatio('#ffffff', '#0e7a5c')).toBeCloseTo(contrastRatio('#0e7a5c', '#ffffff'), 10);
    // Hyper's success green with white text, the pair that started Fase 16.
    expect(contrastRatio('#ffffff', '#0acf97')).toBeCloseTo(2.02, 2);
    expect(relativeLuminance('#ffffff')).toBe(1);
  });
});
