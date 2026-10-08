import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CfdiTaxObject, UnitType, type ProductDto } from '../../types';
import { MAX_LINES, MAX_QUANTITY_PER_LINE } from './sellModel';
import { useSellCart } from './useSellCart';

function product(id: string, overrides: Partial<ProductDto> = {}): ProductDto {
  return {
    id,
    categoryId: 'c1',
    name: `Producto ${id}`,
    sortOrder: 1,
    priceAmount: 25,
    priceCurrency: 'MXN',
    isTaxExempt: false,
    hasDiscount: false,
    taxObject: CfdiTaxObject.Subject,
    isAvailable: true,
    isFeatured: false,
    tracksStock: false,
    isLowStock: false,
    isTrackable: false,
    notifyCustomer: false,
    unit: UnitType.Unit,
    ...overrides,
  };
}

describe('useSellCart', () => {
  it('keeps one line per product and options, up to the quantity the API accepts', () => {
    const { result } = renderHook(() => useSellCart(16, vi.fn()));
    const latte = product('latte', { priceAmount: 55, optionGroups: [{ id: 'g', name: 'Tamaño', minSelections: 1, maxSelections: 1, options: [
      { id: 'small', name: 'Chico', priceDelta: -5, isAvailable: true },
      { id: 'large', name: 'Grande', priceDelta: 12, isAvailable: true },
    ] }] });

    act(() => result.current.addChosen(latte, ['large'], 1));
    act(() => result.current.addChosen(latte, ['large'], 2));
    act(() => result.current.addChosen(latte, ['small'], 1));

    expect(result.current.cart.map((line) => [line.optionsLabel, line.quantity, line.unitPrice])).toEqual([
      ['Grande', 3, 67],
      ['Chico', 1, 50],
    ]);

    act(() => result.current.changeQuantity(result.current.cart[0].key, MAX_QUANTITY_PER_LINE + 5));
    expect(result.current.cart[0].quantity).toBe(MAX_QUANTITY_PER_LINE);

    // Zero removes the line.
    act(() => result.current.changeQuantity(result.current.cart[1].key, 0));
    expect(result.current.cart).toHaveLength(1);
  });

  it('replaces what was weighed, and takes a typed quantity only once it is valid', () => {
    const { result } = renderHook(() => useSellCart(16, vi.fn()));
    const steak = product('steak', { unit: UnitType.Kg, priceAmount: 389.9 });

    act(() => result.current.setMeasured(steak, 0.5));
    act(() => result.current.setMeasured(steak, 1.255));
    expect(result.current.cart.map((line) => [line.quantity, line.fractional])).toEqual([[1.255, true]]);

    const line = result.current.cart[0];
    act(() => result.current.typeQuantity(line, '0.'));
    expect(result.current.quantityDrafts[line.key]).toBe('0.');
    expect(result.current.cart[0].quantity).toBe(1.255);
    act(() => result.current.typeQuantity(line, '0.75'));
    expect(result.current.cart[0].quantity).toBe(0.75);
    act(() => result.current.finishTypingQuantity(line.key));
    expect(result.current.quantityDrafts).toEqual({});
  });

  it('says so instead of adding a line past the limit of a sale', () => {
    const tooMany = vi.fn();
    const { result } = renderHook(() => useSellCart(16, tooMany));

    act(() => {
      for (let index = 0; index <= MAX_LINES; index += 1) result.current.addOne(product(`p${index}`));
    });

    expect(result.current.cart).toHaveLength(MAX_LINES);
    expect(tooMany).toHaveBeenCalled();
  });
});
