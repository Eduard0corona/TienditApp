import { BusinessCategory } from '../api/generated/enums';
import type { DirectoryBusiness } from '../api/publicDirectoryApi';
import { formatMoney } from './money';

export interface BusinessCategoryInfo {
  value: BusinessCategory;
  /** What a customer reads on the chip and the page of the category. */
  label: string;
  /** The path of its page: /explorar/{slug}. */
  slug: string;
  icon: string;
}

/** The categories of Explorar, in the order its chips show them (ADR 0021). Otros goes last. */
export const BUSINESS_CATEGORIES: BusinessCategoryInfo[] = [
  { value: BusinessCategory.Food, label: 'Comida', slug: 'comida', icon: 'ri-restaurant-2-line' },
  { value: BusinessCategory.Groceries, label: 'Abarrotes', slug: 'abarrotes', icon: 'ri-shopping-basket-2-line' },
  { value: BusinessCategory.Health, label: 'Farmacia y salud', slug: 'salud', icon: 'ri-capsule-line' },
  { value: BusinessCategory.Apparel, label: 'Ropa y accesorios', slug: 'ropa', icon: 'ri-t-shirt-2-line' },
  { value: BusinessCategory.Electronics, label: 'Electrónica', slug: 'electronica', icon: 'ri-smartphone-line' },
  { value: BusinessCategory.Hardware, label: 'Ferretería y hogar', slug: 'ferreteria', icon: 'ri-hammer-line' },
  { value: BusinessCategory.Stationery, label: 'Papelería', slug: 'papeleria', icon: 'ri-pencil-ruler-2-line' },
  { value: BusinessCategory.Beauty, label: 'Belleza y bienestar', slug: 'belleza', icon: 'ri-scissors-cut-line' },
  { value: BusinessCategory.Automotive, label: 'Autos', slug: 'autos', icon: 'ri-car-line' },
  { value: BusinessCategory.HomeRepairs, label: 'Hogar y reparaciones', slug: 'hogar-y-reparaciones', icon: 'ri-tools-line' },
  { value: BusinessCategory.Services, label: 'Servicios', slug: 'servicios', icon: 'ri-service-line' },
  { value: BusinessCategory.Other, label: 'Otros', slug: 'otros', icon: 'ri-store-2-line' },
];

export function categoryInfo(value: BusinessCategory | null | undefined): BusinessCategoryInfo | undefined {
  return BUSINESS_CATEGORIES.find((category) => category.value === value);
}

export function categoryBySlug(slug: string | null | undefined): BusinessCategoryInfo | undefined {
  const wanted = (slug ?? '').trim().toLowerCase();
  return BUSINESS_CATEGORIES.find((category) => category.slug === wanted);
}

/**
 * What a card says about delivery (Fase 24B): with the customer's postal code, whether the business delivers there and
 * what it costs (written like its prices: «más IVA» when they do not include it); without it, only that it delivers.
 * Null for a business that takes no orders to an address.
 */
export function deliveryToPostalCode(business: DirectoryBusiness): { reaches: boolean; text: string } | null {
  if (!business.deliveryAvailable) return null;
  if (business.deliversToPostalCode === false) return { reaches: false, text: 'No llega a tu código postal' };
  if (business.deliversToPostalCode !== true) return { reaches: true, text: 'Envío a domicilio' };
  const fee = business.deliveryFee ?? 0;
  return { reaches: true, text: fee > 0 ? `Envío ${formatMoney(fee)}${business.pricesIncludeTax ? '' : ' más IVA'}` : 'Envío gratis' };
}

/** What the card says about orders: the answer its menu gives, in the words of the menu. */
export function orderStatus(business: DirectoryBusiness): { tone: 'taking' | 'paused' | 'closed' | 'menu'; text: string } {
  const { closed } = business;
  const hours = business.statusLabel
    ? [business.statusLabel, business.statusDetail].filter(Boolean).join(' · ')
    : null;
  if (business.acceptsOrders) return { tone: 'taking', text: 'Pide en línea' };
  if (business.ordersPaused) {
    return closed && hours
      ? { tone: 'closed', text: hours }
      : { tone: 'paused', text: 'Pedidos en línea en pausa' };
  }
  // A business that books appointments online (Fase 24D) has more than a menu: its own badge says so, and this one its hours.
  if (business.booksOnline && hours) return { tone: closed ? 'closed' : 'menu', text: hours };
  return { tone: closed ? 'closed' : 'menu', text: hours ? `Solo menú · ${hours}` : 'Solo menú' };
}
