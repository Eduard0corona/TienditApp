import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { productsApi } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import type { ProductDto, CategoryDto, PaginatedList } from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';

const COLUMNS = [
  { header: 'Imagen', width: 60 },
  { header: 'Nombre' },
  { header: 'Categoría' },
  { header: 'Precio', className: 'text-end' },
  { header: 'Stock', className: 'text-center' },
  { header: 'Horario', className: 'text-center' },
  { header: 'Disponible', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

export default function ProductsList() {
  const { confirm } = useConfirm();
  const [data, setData] = useState<PaginatedList<ProductDto>>({
    items: [],
    pageNumber: 1,
    totalPages: 0,
    totalCount: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Filters & pagination
  const [filterCategory, setFilterCategory] = useState('');
  const [filterAvailability, setFilterAvailability] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const loadCategories = useCallback(async () => {
    try {
      const { data } = await categoriesApi.getAll();
      setCategories(data);
    } catch {
      // non-critical
    }
  }, []);

  const loadProducts = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params: Record<string, unknown> = { pageNumber, pageSize };
      params.itemType = 'Product';
      if (search) params.search = search;
      if (filterCategory) params.categoryId = filterCategory;
      if (filterAvailability) params.isAvailable = filterAvailability === 'true';
      const { data } = await productsApi.getAll(params as Parameters<typeof productsApi.getAll>[0]);
      setData(data);
    } catch {
      setError('Error al cargar los productos.');
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, filterCategory, filterAvailability, search]);

  useEffect(() => {
    loadCategories();
  }, [loadCategories]);

  useEffect(() => {
    loadProducts();
  }, [loadProducts]);

  const handleToggleAvailability = async (product: ProductDto) => {
    try {
      await productsApi.toggleAvailability(product.id, !product.isAvailable);
      await loadProducts();
    } catch {
      setError('Error al cambiar la disponibilidad.');
    }
  };

  const handleDelete = async (id: string) => {
    const product = data.items.find((item) => item.id === id);
    const accepted = await confirm({
      title: 'Eliminar producto',
      message: `Se eliminará ${product ? `“${product.name}”` : 'el producto'} del catálogo. Las ventas anteriores no cambian.`,
      confirmLabel: 'Eliminar',
      danger: true,
    });
    if (!accepted) return;
    try {
      await productsApi.delete(id);
      await loadProducts();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al eliminar el producto.');
      setError(msg);
    }
  };

  const getCategoryName = (categoryId: string) =>
    categories.find((c) => c.id === categoryId)?.name || '-';

  return (
    <>
      <PageTitle title="Productos" subtitle="Catálogo" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              {/* Header: action button + filters */}
              <div className="row mb-2">
                <div className="col-sm-4">
                  <Link to="/products/new" className="btn btn-primary mb-2">
                    <i className="ri-add-line me-1"></i> Nuevo Producto
                  </Link>
                </div>
                <div className="col-sm-8">
                  <div className="d-flex gap-2 justify-content-sm-end flex-wrap">
                    <select
                      className="form-select form-select-sm"
                      style={{ width: 'auto' }}
                      value={filterCategory}
                      onChange={(e) => {
                        setFilterCategory(e.target.value);
                        setPageNumber(1);
                      }}
                    >
                      <option value="">Todas las categorías</option>
                      {categories.map((c) => (
                        <option key={c.id} value={c.id}>
                          {c.name}
                        </option>
                      ))}
                    </select>
                    <select
                      className="form-select form-select-sm"
                      style={{ width: 'auto' }}
                      value={filterAvailability}
                      onChange={(e) => {
                        setFilterAvailability(e.target.value);
                        setPageNumber(1);
                      }}
                    >
                      <option value="">Todos los estados</option>
                      <option value="true">Disponible</option>
                      <option value="false">No disponible</option>
                    </select>
                  </div>
                </div>
              </div>

              {error && (
                <div className="alert alert-danger alert-dismissible fade show">
                  {error}
                  <button type="button" className="btn-close" onClick={() => setError('')}></button>
                </div>
              )}

              <DataTable
                data={data.items}
                columns={COLUMNS}
                keyExtractor={(p) => p.id}
                loading={loading}
                emptyMessage="No hay productos registrados."
                searchPlaceholder="Nombre, SKU o código de barras"
                serverSide={{
                  totalCount: data.totalCount,
                  page: pageNumber,
                  pageSize,
                  onPageChange: setPageNumber,
                  onPageSizeChange: (size) => {
                    setPageSize(size);
                    setPageNumber(1);
                  },
                  onSearchChange: (term) => {
                    setSearch(term);
                    setPageNumber(1);
                  },
                }}
                renderRow={(product) => (
                  <>
                    <td>
                      <img
                        src={product.images?.thumbnail || '/images/placeholders/product.svg'}
                        alt={product.name}
                        className="rounded"
                        height="48"
                        width="48"
                        style={{ objectFit: 'cover' }}
                      />
                    </td>
                    <td>
                      <h5 className="m-0 fw-semibold">
                        <Link to={`/products/${product.id}/edit`} className="text-dark">
                          {product.name}
                        </Link>
                      </h5>
                      {product.sku && (
                        <span className="text-muted font-12">SKU: {product.sku}</span>
                      )}
                    </td>
                    <td>{getCategoryName(product.categoryId)}</td>
                    <td className="text-end">
                      {product.hasDiscount && product.compareAtPrice && (
                        <span className="text-muted text-decoration-line-through me-1">
                          ${product.compareAtPrice.toFixed(2)}
                        </span>
                      )}
                      <span className="fw-semibold">
                        ${product.priceAmount.toFixed(2)} {product.priceCurrency}
                      </span>
                    </td>
                    <td className="text-center">
                      {product.stockQuantity != null ? (
                        <span
                          className={`badge ${product.isLowStock ? 'bg-warning' : 'bg-success'}`}
                        >
                          {product.stockQuantity}
                        </span>
                      ) : (
                        <span className="text-muted">-</span>
                      )}
                    </td>
                    <td className="text-center">
                      {product.availableFromTime && product.availableToTime
                        ? `${product.availableFromTime.slice(0, 5)} - ${product.availableToTime.slice(0, 5)}`
                        : 'Todo el día'}
                    </td>
                    <td className="text-center">
                      <div className="form-check form-switch d-inline-block">
                        <input
                          type="checkbox"
                          className="form-check-input"
                          checked={product.isAvailable}
                          onChange={() => handleToggleAvailability(product)}
                        />
                      </div>
                    </td>
                    <td className="text-center">
                      <Link
                        to={`/products/${product.id}/edit`}
                        className="btn btn-sm btn-soft-primary me-1"
                        title="Editar"
                      >
                        <i className="ri-pencil-line"></i>
                      </Link>
                      <button
                        className="btn btn-sm btn-soft-danger"
                        title="Eliminar"
                        onClick={() => handleDelete(product.id)}
                      >
                        <i className="ri-delete-bin-line"></i>
                      </button>
                    </td>
                  </>
                )}
              />
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
