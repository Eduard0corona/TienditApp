import { BusinessCategory } from '../api/generated/enums';
import type { DirectoryBusiness } from '../api/publicDirectoryApi';

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
  return { tone: closed ? 'closed' : 'menu', text: hours ? `Solo menú · ${hours}` : 'Solo menú' };
}
