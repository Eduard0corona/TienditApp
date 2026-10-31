import { describe, expect, it } from 'vitest';
import type { DirectoryBusiness } from '../api/publicDirectoryApi';
import { BusinessCategory, HomeVisitChargePolicy } from '../api/generated/enums';
import {
  BUSINESS_CATEGORIES,
  categoryBySlug,
  categoryInfo,
  deliveryToPostalCode,
  orderStatus,
  visitPriceText,
  visitToPostalCode,
} from './businessCategories';

const business = (overrides: Partial<DirectoryBusiness> = {}): DirectoryBusiness => ({
  name: 'Café Luna',
  slug: 'cafe-luna',
  category: BusinessCategory.Food,
  theme: 0,
  deliveryOnly: false,
  deliveryAvailable: false,
  pricesIncludeTax: true,
  booksOnline: false,
  receivesVisits: false,
  acceptsOrders: false,
  ordersPaused: false,
  closed: false,
  ...overrides,
});

describe('businessCategories', () => {
  it('names every category of the API once, with its own page', () => {
    expect(BUSINESS_CATEGORIES.map((item) => item.value).sort((a, b) => a - b))
      .toEqual(Object.values(BusinessCategory).sort((a, b) => a - b));
    expect(new Set(BUSINESS_CATEGORIES.map((item) => item.slug)).size).toBe(BUSINESS_CATEGORIES.length);
    expect(BUSINESS_CATEGORIES.at(-1)?.value).toBe(BusinessCategory.Other);
  });

  it('finds a category by its page and by its value', () => {
    expect(categoryBySlug(' Comida ')?.value).toBe(BusinessCategory.Food);
    expect(categoryBySlug('ferreteria')?.label).toBe('Ferretería y hogar');
    expect(categoryBySlug('no-existe')).toBeUndefined();
    expect(categoryInfo(BusinessCategory.Beauty)?.slug).toBe('belleza');
  });

  it('says what the delivery to the customer\'s postal code costs, written like the prices of the business', () => {
    expect(deliveryToPostalCode(business())).toBeNull();
    expect(deliveryToPostalCode(business({ deliveryAvailable: true }))).toEqual({ reaches: true, text: 'Envío a domicilio' });
    expect(deliveryToPostalCode(business({ deliveryAvailable: true, deliversToPostalCode: true, deliveryFee: 25 })))
      .toEqual({ reaches: true, text: 'Envío $25.00' });
    expect(deliveryToPostalCode(business({ deliveryAvailable: true, deliversToPostalCode: true, deliveryFee: 25, pricesIncludeTax: false })))
      .toEqual({ reaches: true, text: 'Envío $25.00 más IVA' });
    expect(deliveryToPostalCode(business({ deliveryAvailable: true, deliversToPostalCode: true, deliveryFee: 0 })))
      .toEqual({ reaches: true, text: 'Envío gratis' });
    expect(deliveryToPostalCode(business({ deliveryAvailable: true, deliversToPostalCode: false })))
      .toEqual({ reaches: false, text: 'No llega a tu código postal' });
  });

  it('words whether a business takes orders as its menu does', () => {
    expect(orderStatus(business({ acceptsOrders: true }))).toEqual({ tone: 'taking', text: 'Pide en línea' });
    expect(orderStatus(business({ ordersPaused: true, statusLabel: 'Abierto', statusDetail: 'cierra a las 20:00' })))
      .toEqual({ tone: 'paused', text: 'Pedidos en línea en pausa' });
    expect(orderStatus(business({ ordersPaused: true, closed: true, statusLabel: 'Cerrado', statusDetail: 'abre mañana a las 9:00' })))
      .toEqual({ tone: 'closed', text: 'Cerrado · abre mañana a las 9:00' });
    expect(orderStatus(business())).toEqual({ tone: 'menu', text: 'Solo menú' });
    expect(orderStatus(business({ closed: true, statusLabel: 'Cerrado', statusDetail: 'abre el lunes a las 9:00' })))
      .toEqual({ tone: 'closed', text: 'Solo menú · Cerrado · abre el lunes a las 9:00' });
  });

  it('does not call «solo menú» a business that books appointments online: its hours are enough', () => {
    expect(orderStatus(business({ booksOnline: true, statusLabel: 'Abierto', statusDetail: 'cierra a las 20:00' })))
      .toEqual({ tone: 'menu', text: 'Abierto · cierra a las 20:00' });
    expect(orderStatus(business({ booksOnline: true, acceptsOrders: true }))).toEqual({ tone: 'taking', text: 'Pide en línea' });
  });

  it('finds the trades at their own page (Fase 29A)', () => {
    expect(categoryBySlug('hogar-y-reparaciones')).toMatchObject({ value: BusinessCategory.HomeRepairs, label: 'Hogar y reparaciones' });
  });

  it('never speaks of paused orders or a menu for a business that receives visits: only its hours, or nothing', () => {
    expect(orderStatus(business({ receivesVisits: true, ordersPaused: true }))).toEqual({ tone: 'menu', text: '' });
    expect(orderStatus(business({ receivesVisits: true, closed: true, statusLabel: 'Cerrado', statusDetail: 'abre a las 9:00' })))
      .toEqual({ tone: 'closed', text: 'Cerrado · abre a las 9:00' });
    expect(orderStatus(business({ receivesVisits: true, acceptsOrders: true }))).toEqual({ tone: 'taking', text: 'Pide en línea' });
  });

  it('words what a visit costs as the prices of the business, «desde» when it depends on the postal code', () => {
    expect(visitPriceText(null, HomeVisitChargePolicy.Charged, true)).toBeNull();
    expect(visitPriceText({ amount: 350, isStarting: false }, HomeVisitChargePolicy.Charged, true)).toBe('Visita $350.00');
    expect(visitPriceText({ amount: 350, isStarting: true }, HomeVisitChargePolicy.Charged, false)).toBe('Visita desde $350.00 más IVA');
    expect(visitPriceText({ amount: 350, isStarting: false }, HomeVisitChargePolicy.CreditedIfHired, true))
      .toBe('Visita $350.00, se descuenta si contratas');
    expect(visitPriceText({ amount: 0, isStarting: false }, HomeVisitChargePolicy.Free, false)).toBe('Visita sin costo');
  });

  it('says whether a visit goes to the customer\'s postal code and what it costs there', () => {
    const visits = { receivesVisits: true, visitChargePolicy: HomeVisitChargePolicy.Charged, visitPrice: { amount: 200, isStarting: true } };
    expect(visitToPostalCode(business())).toBeNull();
    expect(visitToPostalCode(business(visits))).toEqual({ reaches: true, text: 'Visita desde $200.00' });
    expect(visitToPostalCode(business({ ...visits, visitsPostalCode: true, visitPrice: { amount: 250, isStarting: false } })))
      .toEqual({ reaches: true, text: 'Va a tu código postal · Visita $250.00' });
    expect(visitToPostalCode(business({ ...visits, visitsPostalCode: false })))
      .toEqual({ reaches: false, text: 'No va a tu código postal' });
    expect(visitToPostalCode(business({ receivesVisits: true }))).toBeNull();
  });
});
