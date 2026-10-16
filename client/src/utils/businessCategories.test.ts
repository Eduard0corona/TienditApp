import { describe, expect, it } from 'vitest';
import type { DirectoryBusiness } from '../api/publicDirectoryApi';
import { BusinessCategory, DirectoryVisibility } from '../api/generated/enums';
import { BUSINESS_CATEGORIES, DIRECTORY_VISIBILITY, categoryBySlug, categoryInfo, orderStatus } from './businessCategories';

const business = (overrides: Partial<DirectoryBusiness> = {}): DirectoryBusiness => ({
  name: 'Café Luna',
  slug: 'cafe-luna',
  category: BusinessCategory.Food,
  theme: 0,
  deliveryOnly: false,
  deliveryAvailable: false,
  acceptsOrders: false,
  ordersPaused: false,
  ...overrides,
});

describe('businessCategories', () => {
  it('names every category of the API once, with its own page', () => {
    expect(BUSINESS_CATEGORIES.map((item) => item.value).sort((a, b) => a - b))
      .toEqual(Object.values(BusinessCategory).sort((a, b) => a - b));
    expect(new Set(BUSINESS_CATEGORIES.map((item) => item.slug)).size).toBe(BUSINESS_CATEGORIES.length);
    expect(BUSINESS_CATEGORIES.at(-1)?.value).toBe(BusinessCategory.Other);
  });

  it('writes out the numbers of the API', () => {
    expect(BUSINESS_CATEGORIES.map((item) => item.value)).toEqual([
      BusinessCategory.Food, BusinessCategory.Groceries, BusinessCategory.Health, BusinessCategory.Apparel,
      BusinessCategory.Electronics, BusinessCategory.Hardware, BusinessCategory.Stationery, BusinessCategory.Beauty,
      BusinessCategory.Automotive, BusinessCategory.Services, BusinessCategory.Other,
    ]);
    expect(DIRECTORY_VISIBILITY).toEqual(DirectoryVisibility);
  });

  it('finds a category by its page and by its value', () => {
    expect(categoryBySlug(' Comida ')?.value).toBe(BusinessCategory.Food);
    expect(categoryBySlug('ferreteria')?.label).toBe('Ferretería y hogar');
    expect(categoryBySlug('no-existe')).toBeUndefined();
    expect(categoryInfo(BusinessCategory.Beauty)?.slug).toBe('belleza');
  });

  it('words whether a business takes orders as its menu does', () => {
    expect(orderStatus(business({ acceptsOrders: true }))).toEqual({ tone: 'taking', text: 'Pide en línea' });
    expect(orderStatus(business({ ordersPaused: true, statusLabel: 'Abierto', statusDetail: 'cierra a las 20:00' })))
      .toEqual({ tone: 'paused', text: 'Pedidos en línea en pausa' });
    expect(orderStatus(business({ ordersPaused: true, statusLabel: 'Cerrado', statusDetail: 'abre mañana a las 9:00' })))
      .toEqual({ tone: 'closed', text: 'Cerrado · abre mañana a las 9:00' });
    expect(orderStatus(business())).toEqual({ tone: 'menu', text: 'Solo menú' });
    expect(orderStatus(business({ statusLabel: 'Cerrado', statusDetail: 'abre el lunes a las 9:00' })))
      .toEqual({ tone: 'closed', text: 'Solo menú · Cerrado · abre el lunes a las 9:00' });
  });
});
