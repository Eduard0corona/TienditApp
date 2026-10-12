import { useCallback, useEffect, useRef, useState } from 'react';
import { categoriesApi } from '../../api/categoriesApi';
import { getApiErrorMessage } from '../../api/errors';
import { productsApi } from '../../api/productsApi';
import { loadCatalogSnapshot, saveCatalogSnapshot } from '../../offline/catalogCache';
import { isNetworkError } from '../../offline/saleQueue';
import type { CategoryDto, ProductDto } from '../../types';
import { CATALOG_PAGE_SIZE, CATALOG_RETRY_MS } from './sellModel';

export interface SellCatalogOptions {
  /**
   * An order of a delivery platform: its app already took it, whatever the hours of each product here, so the catalog
   * brings the available products out of their hours too. It is not saved for selling offline (that one keeps the hours).
   */
  includeOutOfSchedule?: boolean;
}

/**
 * The catalog Vender sells from: the products available now (up to a page of the API) and their categories. Without
 * connection it is the one this device saved the last time, replaced by the current one when the server answers again.
 * Each load hands the products to `onLoaded`, which keeps the ticket in line with them. Changing the options loads it again.
 */
export function useSellCatalog(
  tenantId: string,
  onLoaded: (products: ProductDto[]) => void,
  { includeOutOfSchedule = false }: SellCatalogOptions = {},
) {
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [catalogTruncated, setCatalogTruncated] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  // When the catalog on screen is the one saved on the device (the connection dropped while loading).
  const [offlineCatalogAt, setOfflineCatalogAt] = useState<string | null>(null);
  // A slower, older load must not replace a newer one (the cashier went to a platform and back meanwhile).
  const latestLoad = useRef(0);

  const reload = useCallback(async () => {
    if (!tenantId) return;
    const load = ++latestLoad.current;
    try {
      let loaded: ProductDto[];
      try {
        const [productsResponse, categoriesResponse] = await Promise.all([
          productsApi.getAll({
            isAvailable: true,
            onlyAvailableNow: !includeOutOfSchedule,
            pageNumber: 1,
            pageSize: CATALOG_PAGE_SIZE,
          }),
          categoriesApi.getAll(),
        ]);
        if (load !== latestLoad.current) return;
        loaded = productsResponse.data.items;
        setCategories(categoriesResponse.data);
        setCatalogTruncated(productsResponse.data.totalCount > loaded.length);
        setOfflineCatalogAt(null);
        if (!includeOutOfSchedule) void saveCatalogSnapshot(tenantId, loaded, categoriesResponse.data);
      } catch (err: unknown) {
        // Without connection, keep selling with the catalog this device saved the last time. Not for an order of a
        // platform: the saved one keeps the hours, and the ticket would silently lose what the app sold out of them;
        // the load fails instead, keeping the products and the ticket on screen until it is retried.
        const snapshot = isNetworkError(err) && !includeOutOfSchedule ? await loadCatalogSnapshot(tenantId) : null;
        if (load !== latestLoad.current) return;
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
      if (load !== latestLoad.current) return;
      setLoadError(getApiErrorMessage(err, 'No se pudo cargar el catálogo. Revisa tu conexión e intenta de nuevo.'));
    } finally {
      if (load === latestLoad.current) setLoading(false);
    }
  }, [includeOutOfSchedule, onLoaded, tenantId]);

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
