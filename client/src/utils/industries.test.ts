import { describe, expect, it } from 'vitest';
import { INDUSTRY_OPTIONS, matchIndustry } from './industries';

describe('industries', () => {
  it('finds the option of a giro saved with capitals on every word', () => {
    expect(matchIndustry('Alimentos y Bebidas')).toBe('Alimentos y bebidas');
    expect(matchIndustry('Taller Mecánico')).toBe('Taller mecánico');
  });

  it('finds the option of a giro saved without accents', () => {
    expect(matchIndustry('Lavanderia')).toBe('Lavandería');
    expect(matchIndustry('Estetica y Spa')).toBe('Estética y spa');
  });

  it('keeps a giro that is not among the options, so saving the form does not erase it', () => {
    expect(matchIndustry('Tortillería')).toBe('Tortillería');
  });

  it('gives an empty giro when there is none', () => {
    expect(matchIndustry(null)).toBe('');
    expect(matchIndustry(undefined)).toBe('');
    expect(matchIndustry('')).toBe('');
  });

  it('writes every option with a capital only at the start', () => {
    for (const option of INDUSTRY_OPTIONS) {
      expect(option.slice(1)).toBe(option.slice(1).toLowerCase());
    }
  });
});
