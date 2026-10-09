import { useCallback, useEffect, useState } from 'react';
import { categoriesApi } from '../../api/categoriesApi';
import { getApiErrorMessage } from '../../api/errors';
import { productsApi } from '../../api/productsApi';
import { loadCatalogSnapshot, saveCatalogSnapshot } from '../../offline/catalogCache';
import { isNetworkError } from '../../offline/saleQueue';
import type { CategoryDto, ProductDto } from '../../types';
import { CATALOG_PAGE_SIZE, CATALOG_RETRY_MS } from './sellModel';

/**
 * The catalog Vender sells from: the products available now (up to a page of the API) and their categories. Without
 * connection it is the one this device saved the last time, replaced by the current one when the server answers again.
 * Each load hands the products to `onLoaded`, which keeps the ticket in line with them.
 */
export function useSellCatalog(tenantId: string, onLoaded: (products: ProductDto[]) => void) {
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [catalogTruncated, setCatalogTruncated] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  // When the catalog on screen is the one saved on the device (the connection dropped while loading).
  const [offlineCatalogAt, setOfflineCatalogAt] = useState<string | null>(null);

  const reload = useCallback(async () => {
    if (!tenantId) return;
    try {
      let loaded: ProductDto[];
      try {
        const [productsResponse, categoriesResponse] = await Promise.all([
          productsApi.getAll({ isAvailable: true, onlyAvailableNow: true, pageNumber: 1, pageSize: CATALOG_PAGE_SIZE }),
          categoriesApi.getAll(),
        ]);
        loaded = productsResponse.data.items;
        setCategories(categoriesResponse.data);
        setCatalogTruncated(productsResponse.data.totalCount > loaded.length);
        setOfflineCatalogAt(null);
        void saveCatalogSnapshot(tenantId, loaded, categoriesResponse.data);
      } catch (err: unknown) {
        // Without connection, keep selling with the catalog this device saved the last time.
        const snapshot = isNetworkError(err) ? await loadCatalogSnapshot(tenantId) : null;
        if (!snapshot) throw err;
        loaded = snapshot.products;
        setCategories(snapshot.categories);
        setCatalogTruncated(false);
        setOfflineCatalogAt(snapshot.savedAt);
      }
      setProducts(loaded);
      setLoadError('');
      onLoaded(loaded);
    } catch (err: unknown) {
      setLoadError(getApiErrorMessage(err, 'No se pudo cargar el catálogo. Revisa tu conexión e intenta de nuevo.'));
    } finally {
      setLoading(false);
    }
  }, [onLoaded, tenantId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  // With the saved catalog on screen, or after a failed load, bring the current one (prices may have changed) when
  // the connection comes back; the browser does not notice when only the server was unreachable, so retry each minute.
  useEffect(() => {
    if (!offlineCatalogAt && !loadError) return;
    const retry = () => void reload();
    window.addEventListener('online', retry);
    const timer = offlineCatalogAt ? window.setInterval(retry, CATALOG_RETRY_MS) : undefined;
    return () => {
      window.removeEventListener('online', retry);
      window.clearInterval(timer);
    };
  }, [offlineCatalogAt, loadError, reload]);

  return { products, setProducts, categories, catalogTruncated, loading, loadError, offlineCatalogAt, reload };
}
