/**
 * What the store page of the online menu works out for itself (Fase 24E, ADR 0026): the products a search finds, the
 * ones the business features and how much less a product with a compare-at price costs. Nothing here is a price the
 * order pays: the server prices the order.
 */

import type { PublicMenu } from '../api/publicMenuApi';
import type { ProductOptionGroupDto } from '../types';
import { mapPointUrl, type MapPoint } from './geo';

interface SearchableProduct {
  name: string;
  description?: string | null;
}

/** Text as the menu search compares it: without accents, capitals or extra spaces. */
export function comparableText(value: string): string {
  return value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().replace(/\s+/g, ' ').trim();
}

/** The products whose name or description has every word of the search, in the order of the menu; none without one. */
export function searchMenu<P extends SearchableProduct>(categories: ReadonlyArray<{ products: P[] }>, query: string): P[] {
  const words = comparableText(query).split(' ').filter(Boolean);
  if (words.length === 0) return [];
  return categories
    .flatMap((category) => category.products)
    .filter((product) => {
      const text = comparableText(`${product.name} ${product.description ?? ''}`);
      return words.every((word) => text.includes(word));
    });
}

/** The carousel of «Destacados» shows at most this many. */
export const MAX_FEATURED = 10;

/**
 * The products the business marked as featured, in the order of the menu. The carousel shows large photos: while none
 * of them has one there is none (the server already puts them first in their categories).
 */
export function featuredProducts<P extends { isFeatured?: boolean; imageUrl?: string | null }>(
  categories: ReadonlyArray<{ products: P[] }>,
): P[] {
  const featured = categories.flatMap((category) => category.products).filter((product) => product.isFeatured).slice(0, MAX_FEATURED);
  return featured.some((product) => product.imageUrl) ? featured : [];
}

/** How much less a product costs than its compare-at price, in whole percent; null without a discount of at least 1%. */
export function discountPercent(price: number, compareAtPrice?: number | null): number | null {
  if (!compareAtPrice || compareAtPrice <= price || price < 0) return null;
  const percent = Math.round((1 - price / compareAtPrice) * 100);
  return percent >= 1 ? percent : null;
}

/**
 * «Cómo llegar»: the exact point of the branch when its owner put it on the map (Fase 24G), otherwise a search of its
 * address. Opens the maps app on a phone.
 */
export function mapsUrl(address: string, point?: MapPoint | null): string {
  return point ? mapPointUrl(point) : `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
}

/** The groups of options of a product of the menu, typed as the cart reads them. */
export function optionGroupsOf(product: { optionGroups?: unknown[] | null }): ProductOptionGroupDto[] {
  return (product.optionGroups ?? []) as ProductOptionGroupDto[];
}

/**
 * The title of a store's page, the one the server writes for search engines and link previews (PublicPages.StoreHead):
 * what the customer comes to do there.
 */
export function storePageTitle(menu: Pick<PublicMenu, 'businessName' | 'acceptsOrders' | 'ordersPaused' | 'booksOnline'>): string {
  const takesOrders = menu.acceptsOrders || menu.ordersPaused;
  const what = takesOrders ? 'pide en línea' : menu.booksOnline ? 'agenda tu cita' : 'menú en línea';
  return `${menu.businessName}: ${what} · TienditApp`;
}
