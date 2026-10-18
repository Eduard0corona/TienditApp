import { describe, expect, it } from 'vitest';
import { comparableText, discountPercent, featuredProducts, MAX_FEATURED, searchMenu, storePageTitle } from './storeMenu';

const categories = [
  {
    products: [
      { name: 'Café americano', description: 'Café de Chiapas, 12 oz', isFeatured: false },
      { name: 'Latte', description: 'Espresso con leche vaporizada', isFeatured: true, imageUrl: '/latte.webp' },
    ],
  },
  {
    products: [
      { name: 'Chilaquiles verdes', description: 'Con pollo, crema y queso fresco', isFeatured: true },
      { name: 'Pan de elote', description: null, isFeatured: false },
    ],
  },
];

describe('storeMenu', () => {
  it('compares without accents, capitals or extra spaces', () => {
    expect(comparableText('  Café   CON Leche ')).toBe('cafe con leche');
  });

  it('finds the products with every word of the search, in their name or description, in the order of the menu', () => {
    expect(searchMenu(categories, 'cafe').map((product) => product.name)).toEqual(['Café americano']);
    expect(searchMenu(categories, 'LECHE espresso').map((product) => product.name)).toEqual(['Latte']);
    expect(searchMenu(categories, 'pollo queso').map((product) => product.name)).toEqual(['Chilaquiles verdes']);
    expect(searchMenu(categories, 'tacos')).toEqual([]);
    expect(searchMenu(categories, '   ')).toEqual([]);
  });

  it('features what the business marked, in the order of the menu and up to its limit, once one has a photo', () => {
    expect(featuredProducts(categories).map((product) => product.name)).toEqual(['Latte', 'Chilaquiles verdes']);
    const many = [{ products: Array.from({ length: 15 }, (_, index) => ({ name: `P${index}`, isFeatured: true, imageUrl: '/p.webp' })) }];
    expect(featuredProducts(many)).toHaveLength(MAX_FEATURED);
    // Big tiles of initials say nothing: without photos the featured products stay first in their categories.
    expect(featuredProducts([{ products: [{ name: 'Latte', isFeatured: true, imageUrl: null }] }])).toEqual([]);
  });

  it('says how much less a discounted product costs, only when it really costs less', () => {
    expect(discountPercent(65, 75)).toBe(13);
    expect(discountPercent(50, 100)).toBe(50);
    expect(discountPercent(75, 75)).toBeNull();
    expect(discountPercent(80, 75)).toBeNull();
    expect(discountPercent(99.6, 100)).toBeNull();
    expect(discountPercent(55, null)).toBeNull();
  });

  it('titles a store by what the customer comes to do, as the server does for search engines', () => {
    const store = { businessName: 'Café Luna', acceptsOrders: false, ordersPaused: false, booksOnline: false };

    expect(storePageTitle({ ...store, acceptsOrders: true })).toBe('Café Luna: pide en línea · TienditApp');
    // Closed now, it still takes orders: the same page.
    expect(storePageTitle({ ...store, ordersPaused: true, booksOnline: true })).toBe('Café Luna: pide en línea · TienditApp');
    expect(storePageTitle({ ...store, booksOnline: true })).toBe('Café Luna: agenda tu cita · TienditApp');
    expect(storePageTitle(store)).toBe('Café Luna: menú en línea · TienditApp');
  });
});
