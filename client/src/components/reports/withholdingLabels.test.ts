import { describe, expect, it } from 'vitest';
import { describeCertificatePeriod, formatRate, monthLabel, shortUuid, withholdingYears } from './withholdingLabels';

describe('withholdingLabels', () => {
  it('writes a period as the server does, of one month or several', () => {
    expect(describeCertificatePeriod({ monthFrom: 9, monthTo: 9, year: 2026 })).toBe('septiembre de 2026');
    expect(describeCertificatePeriod({ monthFrom: 7, monthTo: 9, year: 2026 })).toBe('julio a septiembre de 2026');
    expect(monthLabel(1)).toBe('Enero');
    expect(monthLabel(12)).toBe('Diciembre');
  });

  it('offers the years from this one back to 2019', () => {
    expect(withholdingYears(2021)).toEqual([2021, 2020, 2019]);
    expect(withholdingYears(2019)).toEqual([2019]);
  });

  it('writes a rate with two decimals and a folio by its first characters', () => {
    expect(formatRate(2.5)).toBe('2.50 %');
    expect(formatRate(null)).toBe('');
    expect(shortUuid('7874d580-da5b-558f-b8ec-d92c57197242')).toBe('7874D580');
  });
});
