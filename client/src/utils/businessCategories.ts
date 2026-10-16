import type { BusinessCategory, DirectoryVisibility } from '../api/generated/enums';
import type { DirectoryBusiness } from '../api/publicDirectoryApi';

export interface BusinessCategoryInfo {
  value: BusinessCategory;
  /** What a customer reads on the chip and the page of the category. */
  label: string;
  /** The path of its page: /explorar/{slug}. */
  slug: string;
  icon: string;
}

/**
 * The categories of Explorar, in the order its chips show them (ADR 0021). Otros goes last. The values are the numbers of
 * BusinessCategory written out, like DIRECTORY_VISIBILITY below: the generated enums live in the first chunk every visit
 * downloads, and these are used only by lazy screens. businessCategories.test.ts checks they match.
 */
export const BUSINESS_CATEGORIES: BusinessCategoryInfo[] = [
  { value: 0, label: 'Comida', slug: 'comida', icon: 'ri-restaurant-2-line' },
  { value: 1, label: 'Abarrotes', slug: 'abarrotes', icon: 'ri-shopping-basket-2-line' },
  { value: 2, label: 'Farmacia y salud', slug: 'salud', icon: 'ri-capsule-line' },
  { value: 3, label: 'Ropa y accesorios', slug: 'ropa', icon: 'ri-t-shirt-2-line' },
  { value: 4, label: 'Electrónica', slug: 'electronica', icon: 'ri-smartphone-line' },
  { value: 5, label: 'Ferretería y hogar', slug: 'ferreteria', icon: 'ri-hammer-line' },
  { value: 6, label: 'Papelería', slug: 'papeleria', icon: 'ri-pencil-ruler-2-line' },
  { value: 7, label: 'Belleza y bienestar', slug: 'belleza', icon: 'ri-scissors-cut-line' },
  { value: 8, label: 'Autos', slug: 'autos', icon: 'ri-car-line' },
  { value: 9, label: 'Servicios', slug: 'servicios', icon: 'ri-service-line' },
  { value: 10, label: 'Otros', slug: 'otros', icon: 'ri-store-2-line' },
];

/** The numbers of DirectoryVisibility: whether Explorar lists the owner's business, or why not. */
export const DIRECTORY_VISIBILITY = {
  Shown: 0,
  HiddenByOwner: 1,
  MenuOff: 2,
  NoProducts: 3,
  Inactive: 4,
} as const satisfies Record<keyof typeof DirectoryVisibility, DirectoryVisibility>;

export function categoryInfo(value: BusinessCategory | null | undefined): BusinessCategoryInfo | undefined {
  return BUSINESS_CATEGORIES.find((category) => category.value === value);
}

export function categoryBySlug(slug: string | null | undefined): BusinessCategoryInfo | undefined {
  const wanted = (slug ?? '').trim().toLowerCase();
  return BUSINESS_CATEGORIES.find((category) => category.slug === wanted);
}

/** What the card says about orders: the answer its menu gives, in the words of the menu. */
export function orderStatus(business: DirectoryBusiness): { tone: 'taking' | 'paused' | 'closed' | 'menu'; text: string } {
  const closed = business.statusLabel === 'Cerrado';
  const hours = business.statusLabel
    ? [business.statusLabel, business.statusDetail].filter(Boolean).join(' · ')
    : null;
  if (business.acceptsOrders) return { tone: 'taking', text: 'Pide en línea' };
  if (business.ordersPaused) {
    return closed && hours
      ? { tone: 'closed', text: hours }
      : { tone: 'paused', text: 'Pedidos en línea en pausa' };
  }
  return { tone: closed ? 'closed' : 'menu', text: hours ? `Solo menú · ${hours}` : 'Solo menú' };
}
