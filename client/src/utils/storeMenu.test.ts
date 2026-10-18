import { describe, expect, it } from 'vitest';
import { comparableText, discountPercent, featuredProducts, MAX_FEATURED, searchMenu } from './storeMenu';

const categories = [
  {
    products: [
      { name: 'Café americano', description: 'Café de Chiapas, 12 oz', isFeatured: false },
      { name: 'Latte', description: 'Espresso con leche vaporizada', isFeatured: true },
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

  it('features what the business marked, in the order of the menu and up to its limit', () => {
    expect(featuredProducts(categories).map((product) => product.name)).toEqual(['Latte', 'Chilaquiles verdes']);
    const many = [{ products: Array.from({ length: 15 }, (_, index) => ({ name: `P${index}`, isFeatured: true })) }];
    expect(featuredProducts(many)).toHaveLength(MAX_FEATURED);
  });

  it('says how much less a discounted product costs, only when it really costs less', () => {
    expect(discountPercent(65, 75)).toBe(13);
    expect(discountPercent(50, 100)).toBe(50);
    expect(discountPercent(75, 75)).toBeNull();
    expect(discountPercent(80, 75)).toBeNull();
    expect(discountPercent(99.6, 100)).toBeNull();
    expect(discountPercent(55, null)).toBeNull();
  });
});
