import { describe, expect, it } from 'vitest';
import { GIROS_WITH_OPTIONS, giroUsesOption, type GiroOption } from './giroOptions';
import { HOME_DELIVERY_INDUSTRY, HOME_TRADES, INDUSTRY_OPTIONS } from './industries';

const uses = (industry: string | null | undefined) =>
  (['deliveryPlatforms', 'onlineBooking', 'homeVisits'] as GiroOption[]).filter((option) => giroUsesOption(industry, option));

describe('giroUsesOption', () => {
  it('says what every giro of the list uses, except «Otro»', () => {
    expect([...GIROS_WITH_OPTIONS].sort()).toEqual(INDUSTRY_OPTIONS.filter((giro) => giro !== 'Otro').sort());
  });

  it('gives a kitchen without premises its delivery apps, not the agenda', () => {
    expect(uses(HOME_DELIVERY_INDUSTRY)).toEqual(['deliveryPlatforms']);
    expect(uses('Alimentos y bebidas')).toEqual(['deliveryPlatforms']);
  });

  it('gives the delivery apps to grocery stores and pharmacies, and nothing extra to other shops', () => {
    expect(uses('Abarrotes')).toEqual(['deliveryPlatforms']);
    expect(uses('Farmacia')).toEqual(['deliveryPlatforms']);
    expect(uses('Ferretería')).toEqual([]);
    expect(uses('Ropa y accesorios')).toEqual([]);
  });

  it('gives appointments online to a business that serves at its premises, without visits or delivery apps', () => {
    for (const giro of ['Consultorio', 'Consultorio dental', 'Estética y spa', 'Gimnasio', 'Lavandería', 'Taller mecánico']) {
      expect(uses(giro)).toEqual(['onlineBooking']);
    }
  });

  it('gives the visits to quote to every trade that goes to homes', () => {
    for (const giro of HOME_TRADES) {
      expect(uses(giro)).toEqual(['homeVisits']);
    }
  });

  it('reads a giro saved with other capitals or without accents', () => {
    expect(uses('Alimentos y Bebidas')).toEqual(['deliveryPlatforms']);
    expect(uses('plomeria')).toEqual(['homeVisits']);
  });

  it('keeps every option for a giro that says nothing', () => {
    const all = ['deliveryPlatforms', 'onlineBooking', 'homeVisits'];
    expect(uses('Otro')).toEqual(all);
    expect(uses('Tortillería')).toEqual(all);
    expect(uses('')).toEqual(all);
    expect(uses(null)).toEqual(all);
  });
});
