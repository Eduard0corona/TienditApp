import { useCallback, useEffect, useRef, useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { inventoryApi } from '../../api/inventoryApi';
import { productsApi } from '../../api/productsApi';
import type { ProductDto } from '../../types';
import {
  buildEmptyRecipeLineForm,
  toRecipeLines,
  toRecipeRequest,
  type RecipeLineFormState,
} from './inventoryForms';

interface RecipeEditorOptions {
  /** The tab of the recipes is open: the products are read the first time it opens, not with the screen. */
  enabled: boolean;
  setError: (message: string) => void;
  setSuccess: (message: string) => void;
  /** After a recipe is saved: what depends on recipes (alerts, kardex) reads again, behind. */
  onSaved: () => Promise<unknown>;
}

export type RecipeEditor = ReturnType<typeof useRecipeEditor>;

/**
 * The supplies a product or service takes. The first item opens when the tab first opens, and unsaved changes stay while
 * another tab is open.
 */
export function useRecipeEditor({ enabled, setError, setSuccess, onSaved }: RecipeEditorOptions) {
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [productId, setProductId] = useState('');
  const [productName, setProductName] = useState('');
  const [productIsAvailable, setProductIsAvailable] = useState(false);
  const [lines, setLines] = useState<RecipeLineFormState[]>([]);
  const [productsLoading, setProductsLoading] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  // The recipe of the item could not be read: saving the empty editor would erase the one saved.
  const [readFailed, setReadFailed] = useState(false);
  // The products were asked for once: opening the tab again keeps them and the recipe being edited.
  const productsAsked = useRef(false);
  // The last recipe asked for: one of another item that arrives later does not replace it.
  const latestRead = useRef(0);

  const loadProducts = useCallback(async () => {
    setProductsLoading(true);
    try {
      const [productsResponse, servicesResponse] = await Promise.all([
        productsApi.getAll({ itemType: 'Product', pageNumber: 1, pageSize: 500 }),
        productsApi.getAll({ itemType: 'Service', pageNumber: 1, pageSize: 500 }),
      ]);

      const merged = [...productsResponse.data.items, ...servicesResponse.data.items];
      const deduped = Array.from(new Map(merged.map((item) => [item.id, item])).values())
        .sort((a, b) => a.name.localeCompare(b.name));

      setProducts(deduped);
      setProductId((current) => current || (deduped[0]?.id ?? ''));
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar los productos y servicios.'));
    } finally {
      setProductsLoading(false);
    }
  }, [setError]);

  useEffect(() => {
    if (!enabled || productsAsked.current) return;
    productsAsked.current = true;
    void loadProducts();
  }, [enabled, loadProducts]);

  const loadRecipe = useCallback(async (id: string) => {
    const read = ++latestRead.current;
    if (!id) {
      setLines([]);
      setProductName('');
      setProductIsAvailable(false);
      setReadFailed(false);
      setLoading(false);
      return;
    }

    setLoading(true);
    try {
      const { data } = await inventoryApi.getProductRecipe(id);
      if (read !== latestRead.current) return;
      setReadFailed(false);
      setProductName(data.productName);
      setProductIsAvailable(data.productIsAvailable);
      setLines(toRecipeLines(data));
    } catch (err: unknown) {
      if (read !== latestRead.current) return;
      setError(getApiErrorMessage(err, 'Error al cargar la receta del producto.'));
      setReadFailed(true);
      setLines([]);
    } finally {
      if (read === latestRead.current) setLoading(false);
    }
  }, [setError]);

  useEffect(() => {
    void loadRecipe(productId);
  }, [loadRecipe, productId]);

  const selectProduct = (id: string) => {
    setProductId(id);
    setLines([]);
    setSuccess('');
    setError('');
  };

  // Another supply chosen in the line is not the one the recipe saved: the saved name no longer applies to it.
  const updateLine = (index: number, patch: Partial<RecipeLineFormState>) => {
    setLines((current) => current.map((line, lineIndex) => (lineIndex === index
      ? {
        ...line,
        ...patch,
        ...('supplyItemId' in patch && { savedSupplyLabel: undefined }),
      }
      : line)));
  };

  const addLine = () => {
    setLines((current) => [...current, buildEmptyRecipeLineForm()]);
  };

  const removeLine = (index: number) => {
    setLines((current) => current.filter((_, lineIndex) => lineIndex !== index));
  };

  const save = async () => {
    setError('');
    setSuccess('');

    if (!productId) {
      setError('Selecciona un producto o servicio.');
      return;
    }

    const result = toRecipeRequest(lines);
    if (result.error !== undefined) {
      setError(result.error);
      return;
    }

    try {
      setSaving(true);
      const { data } = await inventoryApi.upsertProductRecipe(productId, result.request);
      setProductName(data.productName);
      setProductIsAvailable(data.productIsAvailable);
      setLines(toRecipeLines(data));
      setSuccess('Receta guardada correctamente.');
      // The alerts and the kardex read again behind: the recipe is already saved.
      void onSaved();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al guardar la receta.'));
    } finally {
      setSaving(false);
    }
  };

  return {
    products,
    productsLoading,
    productId,
    productName,
    productIsAvailable,
    lines,
    loading,
    saving,
    readFailed,
    selectProduct,
    /** The recipe of the item again; with no item (the products could not be read), the products. */
    reload: () => {
      setError('');
      if (productId) void loadRecipe(productId);
      else void loadProducts();
    },
    save,
    addLine,
    updateLine,
    removeLine,
  };
}
