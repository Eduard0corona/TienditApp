import { useCallback, useEffect, useState } from 'react';
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
  setError: (message: string) => void;
  setSuccess: (message: string) => void;
  /** After a recipe is saved: what depends on recipes (alerts, kardex) reads again. */
  onSaved: () => Promise<unknown>;
}

export type RecipeEditor = ReturnType<typeof useRecipeEditor>;

/**
 * The supplies a product or service takes. The first item opens with the screen, and unsaved changes stay while
 * another tab is open.
 */
export function useRecipeEditor({ setError, setSuccess, onSaved }: RecipeEditorOptions) {
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [productId, setProductId] = useState('');
  const [productName, setProductName] = useState('');
  const [productIsAvailable, setProductIsAvailable] = useState(false);
  const [lines, setLines] = useState<RecipeLineFormState[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const loadProducts = async () => {
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
      } catch {
        setProducts([]);
      }
    };
    loadProducts();
  }, []);

  const loadRecipe = useCallback(async (id: string) => {
    if (!id) {
      setLines([]);
      setProductName('');
      setProductIsAvailable(false);
      return;
    }

    setLoading(true);
    try {
      const { data } = await inventoryApi.getProductRecipe(id);
      setProductName(data.productName);
      setProductIsAvailable(data.productIsAvailable);
      setLines(toRecipeLines(data));
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al cargar la receta del producto.'));
      setLines([]);
    } finally {
      setLoading(false);
    }
  }, [setError]);

  useEffect(() => {
    loadRecipe(productId);
  }, [loadRecipe, productId]);

  const selectProduct = (id: string) => {
    setProductId(id);
    setLines([]);
    setSuccess('');
    setError('');
  };

  const updateLine = (index: number, patch: Partial<RecipeLineFormState>) => {
    setLines((current) => current.map((line, lineIndex) => (lineIndex === index ? { ...line, ...patch } : line)));
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
      await onSaved();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'Error al guardar la receta.'));
    } finally {
      setSaving(false);
    }
  };

  return {
    products,
    productId,
    productName,
    productIsAvailable,
    lines,
    loading,
    saving,
    selectProduct,
    reload: () => {
      if (productId) loadRecipe(productId);
    },
    save,
    addLine,
    updateLine,
    removeLine,
  };
}
