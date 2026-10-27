import { describe, expect, it } from 'vitest';
import { HOME_DELIVERY_INDUSTRY, INDUSTRY_OPTIONS, matchIndustry } from './industries';

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

  it('offers the service giros that have their own templates, written as the server reads them', () => {
    // Same texts as the giros of StarterCatalogTemplates and CategoryTemplateCatalog.
    for (const giro of ['Consultorio', 'Consultorio dental', 'Carpintería', 'Herrería y soldadura', 'Electricista', 'Plomería', 'Aire acondicionado']) {
      expect(INDUSTRY_OPTIONS).toContain(giro);
    }
  });

  it('offers the giro of a kitchen that only delivers, written as the server reads it', () => {
    expect(HOME_DELIVERY_INDUSTRY).toBe('Comida a domicilio');
    expect(INDUSTRY_OPTIONS).toContain(HOME_DELIVERY_INDUSTRY);
    expect(matchIndustry('comida a domicilio')).toBe(HOME_DELIVERY_INDUSTRY);
  });
});
