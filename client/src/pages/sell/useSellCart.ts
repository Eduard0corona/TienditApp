import { useCallback, useState } from 'react';
import type { ProductDto } from '../../types';
import { resolveProductVat } from '../../utils/orderTotals';
import { getLineKey, getOptionsLabel, getPriceWithOptions } from '../../utils/productOptions';
import { allowsFractions, parseQuantity, roundQuantity } from '../../utils/units';
import { isServiceProduct, MAX_LINES, MAX_QUANTITY_PER_LINE, type CartLine } from './sellModel';

/**
 * The lines of the ticket and the rules to change them: the same product with the same options is one line, a line
 * keeps at most the API's quantity, and a sale has at most its number of lines (`onTooManyLines` says so). Prices
 * are the catalog's; the server computes the final total.
 */
export function useSellCart(defaultTaxRate: number, onTooManyLines: () => void) {
  const [cart, setCart] = useState<CartLine[]>([]);
  // What the cashier is typing in a quantity box ("1." on the way to "1.5"), until the box loses focus.
  const [quantityDrafts, setQuantityDrafts] = useState<Record<string, string>>({});

  const toCartLine = useCallback(
    (product: ProductDto, quantity: number, notes = '', optionIds: string[] = []): CartLine => {
      const vat = resolveProductVat(product, defaultTaxRate);
      return {
        key: getLineKey(product.id, optionIds),
        productId: product.id,
        optionIds,
        optionsLabel: getOptionsLabel(product.optionGroups, optionIds),
        name: product.name,
        unitPrice: getPriceWithOptions(product.priceAmount, product.optionGroups, optionIds),
        quantity,
        notes,
        showNotes: notes.length > 0,
        isService: isServiceProduct(product),
        ratePercent: vat.ratePercent,
        isExempt: vat.isExempt,
        unit: product.unit,
        fractional: allowsFractions(product.unit),
      };
    },
    [defaultTaxRate],
  );

  /** One more piece of a product without options (a new line the first time). */
  const addOne = useCallback((product: ProductDto) => {
    setCart((current) => {
      const existing = current.find((line) => line.key === product.id);
      if (existing) {
        return current.map((line) => (line.key === product.id
          ? { ...line, quantity: Math.min(line.quantity + 1, MAX_QUANTITY_PER_LINE) }
          : line));
      }
      if (current.length >= MAX_LINES) {
        onTooManyLines();
        return current;
      }
      return [...current, toCartLine(product, 1)];
    });
  }, [onTooManyLines, toCartLine]);

  /** A product with the options chosen: added to its line, or a new one. */
  const addChosen = (product: ProductDto, optionIds: string[], quantity: number) => {
    const key = getLineKey(product.id, optionIds);
    setCart((current) => {
      if (current.some((line) => line.key === key)) {
        return current.map((line) => (line.key === key
          ? { ...line, quantity: Math.min(roundQuantity(line.quantity + quantity), MAX_QUANTITY_PER_LINE) }
          : line));
      }
      if (current.length >= MAX_LINES) {
        onTooManyLines();
        return current;
      }
      return [...current, toCartLine(product, Math.min(quantity, MAX_QUANTITY_PER_LINE), '', optionIds)];
    });
  };

  /** What was weighed or measured: it replaces the quantity of its line. */
  const setMeasured = (product: ProductDto, quantity: number) => {
    setCart((current) => {
      if (current.some((line) => line.key === product.id)) {
        return current.map((line) => (line.key === product.id ? { ...line, quantity } : line));
      }
      if (current.length >= MAX_LINES) {
        onTooManyLines();
        return current;
      }
      return [...current, toCartLine(product, quantity)];
    });
  };

  /** Zero or less removes the line. */
  const changeQuantity = (key: string, quantity: number) => {
    const rounded = roundQuantity(quantity);
    setCart((current) => (rounded <= 0
      ? current.filter((line) => line.key !== key)
      : current.map((line) => (line.key === key
        ? { ...line, quantity: Math.min(rounded, MAX_QUANTITY_PER_LINE) }
        : line))));
  };

  const typeQuantity = (line: CartLine, text: string) => {
    setQuantityDrafts((current) => ({ ...current, [line.key]: text }));
    const parsed = parseQuantity(text, line.unit);
    if (parsed !== null) changeQuantity(line.key, parsed);
  };

  const finishTypingQuantity = (key: string) => {
    setQuantityDrafts((current) => {
      const next = { ...current };
      delete next[key];
      return next;
    });
  };

  const updateLine = (key: string, changes: Partial<CartLine>) => {
    setCart((current) => current.map((line) => (line.key === key ? { ...line, ...changes } : line)));
  };

  return {
    cart,
    setCart,
    toCartLine,
    addOne,
    addChosen,
    setMeasured,
    changeQuantity,
    quantityDrafts,
    typeQuantity,
    finishTypingQuantity,
    updateLine,
  };
}
