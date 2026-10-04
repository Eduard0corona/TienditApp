import type { CategoryDto, ProductDto } from '../types';
import { OFFLINE_STORES, offlineDb } from './offlineDb';

export interface CatalogSnapshot {
  products: ProductDto[];
  categories: CategoryDto[];
  savedAt: string;
}

const key = (tenantId: string) => `catalog:${tenantId}`;

/** The last catalog Vender loaded for the business, to keep selling when the connection drops. */
export async function saveCatalogSnapshot(tenantId: string, products: ProductDto[], categories: CategoryDto[]): Promise<void> {
  try {
    await offlineDb.put(OFFLINE_STORES.catalog, { products, categories, savedAt: new Date().toISOString() } satisfies CatalogSnapshot, key(tenantId));
  } catch {
    // Without IndexedDB the catalog is just not kept for offline use.
  }
}

export async function loadCatalogSnapshot(tenantId: string): Promise<CatalogSnapshot | null> {
  try {
    return (await offlineDb.get<CatalogSnapshot>(OFFLINE_STORES.catalog, key(tenantId))) ?? null;
  } catch {
    return null;
  }
}
