// Variants and extras of a product (ProductOptionGroup in the API): editing them, choosing them in a sale
// and pricing the line the same way the API does (Product.ResolveOptions and Product.PriceWith).
import type { ProductOptionGroupInput } from '../api/productsApi';
import type { ProductDto, ProductOptionGroupDto } from '../types';

/** Validation messages the API would give, shown before saving. */
export function getOptionGroupsError(groups: ProductOptionGroupInput[]): string | null {
  const names = new Set<string>();
  for (const group of groups) {
    const name = group.name.trim();
    if (!name) return 'Cada grupo de opciones necesita un nombre.';
    if (names.has(name.toLowerCase())) return `El grupo "${name}" está repetido.`;
    names.add(name.toLowerCase());
    if (group.options.length === 0) return `El grupo "${name}" necesita al menos una opción.`;
    if (group.maxSelections > group.options.length) return `En "${name}" no se pueden elegir más opciones de las que hay.`;
    const optionNames = new Set<string>();
    for (const option of group.options) {
      const optionName = option.name.trim();
      if (!optionName) return `Cada opción de "${name}" necesita un nombre.`;
      if (optionNames.has(optionName.toLowerCase())) return `La opción "${optionName}" está repetida en "${name}".`;
      optionNames.add(optionName.toLowerCase());
      if (!Number.isFinite(option.priceDelta) || Math.round(option.priceDelta * 100) / 100 !== option.priceDelta) {
        return `El precio de "${optionName}" admite dos decimales.`;
      }
    }
  }
  return null;
}


/** The product's groups as the editor keeps them. */
export function toOptionGroupInputs(groups: ProductOptionGroupDto[] | undefined): ProductOptionGroupInput[] {
  return (groups ?? []).map((group) => ({
    id: group.id,
    name: group.name,
    minSelections: group.minSelections,
    maxSelections: group.maxSelections,
    options: group.options.map((option) => ({
      id: option.id,
      name: option.name,
      priceDelta: option.priceDelta,
      isAvailable: option.isAvailable,
    })),
  }));
}

export function hasOptions(product: Pick<ProductDto, 'optionGroups'>): boolean {
  return (product.optionGroups?.length ?? 0) > 0;
}

/** Identity of a cart line: the same product with other options is another line. */
export function getLineKey(productId: string, optionIds: readonly string[] = []): string {
  return optionIds.length === 0 ? productId : `${productId}|${[...optionIds].sort().join(',')}`;
}

/** Why the chosen options cannot be sold (same rules as the API), or null. */
export function getSelectionError(groups: ProductOptionGroupDto[] | undefined, optionIds: readonly string[]): string | null {
  for (const group of groups ?? []) {
    const picked = group.options.filter((option) => optionIds.includes(option.id));
    if (picked.some((option) => !option.isAvailable)) return `Una opción de ${group.name} ya no está disponible.`;
    if (picked.length < group.minSelections) {
      return group.minSelections === 1 ? `Elige ${group.name.toLowerCase()}.` : `Elige al menos ${group.minSelections} de ${group.name.toLowerCase()}.`;
    }
    if (picked.length > group.maxSelections) return `Elige como máximo ${group.maxSelections} de ${group.name.toLowerCase()}.`;
  }
  return null;
}

/** Unit price with the chosen options (in cents to avoid floating point drift). */
export function getPriceWithOptions(price: number, groups: ProductOptionGroupDto[] | undefined, optionIds: readonly string[]): number {
  const cents = (groups ?? [])
    .flatMap((group) => group.options)
    .filter((option) => optionIds.includes(option.id))
    .reduce((sum, option) => sum + Math.round(option.priceDelta * 100), Math.round(price * 100));
  return Math.max(0, cents) / 100;
}

/** "Grande, Almendra": the chosen options in the order of the product's groups. */
export function getOptionsLabel(groups: ProductOptionGroupDto[] | undefined, optionIds: readonly string[]): string {
  return (groups ?? [])
    .flatMap((group) => group.options)
    .filter((option) => optionIds.includes(option.id))
    .map((option) => option.name)
    .join(', ');
}

/** "Latte (Grande, Almendra)" for a sold line. */
export function describeItem(item: { productName: string; options?: Array<{ optionName: string }> }): string {
  return item.options && item.options.length > 0
    ? `${item.productName} (${item.options.map((option) => option.optionName).join(', ')})`
    : item.productName;
}
