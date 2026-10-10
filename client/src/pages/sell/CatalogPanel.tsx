import type { KeyboardEvent, RefObject } from 'react';
import { Link } from 'react-router-dom';
import PhotoOrInitial from '../../components/common/PhotoOrInitial';
import { useScrollFade } from '../../hooks/useScrollFade';
import type { CategoryDto, ProductDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { allowsFractions, formatQuantity, getUnitShortLabel, roundQuantity } from '../../utils/units';
import { CATALOG_PAGE_SIZE, isServiceProduct, type CartLine, type CategoryFilter } from './sellModel';

interface CatalogPanelProps {
  searchInputRef: RefObject<HTMLInputElement | null>;
  search: string;
  onSearchChange: (value: string) => void;
  /** Enter adds the exact match of a barcode, or the only product found. */
  onSearchKeyDown: (event: KeyboardEvent<HTMLInputElement>) => void;
  categoryFilter: CategoryFilter;
  onCategoryChange: (filter: CategoryFilter) => void;
  hasFeatured: boolean;
  /** Categories with something to sell, in their order. */
  categories: CategoryDto[];
  loadError: string;
  onRetry: () => void;
  catalogTruncated: boolean;
  loading: boolean;
  /** The business has products available now (before the search and the category filter). */
  hasProducts: boolean;
  visibleProducts: ProductDto[];
  cart: CartLine[];
  onAdd: (product: ProductDto) => void;
  canManageCatalog: boolean;
}

/** The touch catalog of Vender: search or scan, categories and a tile per product with what the ticket has of it. */
export default function CatalogPanel({
  searchInputRef,
  search,
  onSearchChange,
  onSearchKeyDown,
  categoryFilter,
  onCategoryChange,
  hasFeatured,
  categories,
  loadError,
  onRetry,
  catalogTruncated,
  loading,
  hasProducts,
  visibleProducts,
  cart,
  onAdd,
  canManageCatalog,
}: CatalogPanelProps) {
  // Once a product has a photo, the others show their initial so the names line up; without photos, only text.
  const withPhotos = visibleProducts.some((product) => product.images?.thumbnail);
  const { ref: chipsRef, fadeClass: chipsFade } = useScrollFade<HTMLDivElement>(`${categories.length}-${hasFeatured}`);

  return (
    <section className="sell-catalog" aria-label="Catálogo">
      <div className="sell-search">
        <i className="ri-search-line" aria-hidden="true"></i>
        <input
          ref={searchInputRef}
          type="search"
          className="form-control form-control-lg"
          placeholder="Buscar o escanear código"
          aria-label="Buscar producto o escanear código de barras"
          value={search}
          onChange={(event) => onSearchChange(event.target.value)}
          onKeyDown={onSearchKeyDown}
          autoComplete="off"
        />
      </div>

      <div ref={chipsRef} className={`sell-chips ta-scroll-fade ${chipsFade}`} role="tablist" aria-label="Categorías">
        <button
          type="button"
          role="tab"
          aria-selected={categoryFilter === 'all'}
          className={`btn btn-touch ${categoryFilter === 'all' ? 'btn-primary' : 'btn-light'}`}
          onClick={() => onCategoryChange('all')}
        >
          Todos
        </button>
        {hasFeatured && (
          <button
            type="button"
            role="tab"
            aria-selected={categoryFilter === 'featured'}
            className={`btn btn-touch ${categoryFilter === 'featured' ? 'btn-primary' : 'btn-light'}`}
            onClick={() => onCategoryChange('featured')}
          >
            <i className="ri-star-line me-1" aria-hidden="true"></i>Favoritos
          </button>
        )}
        {categories.map((category) => (
          <button
            key={category.id}
            type="button"
            role="tab"
            aria-selected={categoryFilter === category.id}
            className={`btn btn-touch ${categoryFilter === category.id ? 'btn-primary' : 'btn-light'}`}
            onClick={() => onCategoryChange(category.id)}
          >
            {category.name}
          </button>
        ))}
      </div>

      {loadError && (
        <div className="alert alert-danger d-flex justify-content-between align-items-center gap-2">
          <span>{loadError}</span>
          <button type="button" className="btn btn-sm btn-light" onClick={onRetry}>Reintentar</button>
        </div>
      )}
      {catalogTruncated && (
        <div className="alert alert-info py-2" role="status">
          Tu catálogo tiene más de {CATALOG_PAGE_SIZE} productos: escribe el nombre o escanea el código y presiona Enter
          para buscar en todo el catálogo.
        </div>
      )}

      {loading ? (
        <div className="sell-products" aria-busy="true">
          {Array.from({ length: 8 }, (_, index) => <div key={index} className="sell-product sell-product-skeleton" />)}
        </div>
      ) : !hasProducts && !loadError ? (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-store-2-line fs-1 text-muted" aria-hidden="true"></i>
            <p className="mt-2 mb-3">Todavía no hay productos disponibles para vender.</p>
            {canManageCatalog && <Link to="/products/new" className="btn btn-primary">Agregar producto</Link>}
          </div>
        </div>
      ) : visibleProducts.length === 0 ? (
        <p className="text-muted py-4 text-center">No hay productos que coincidan con la búsqueda.</p>
      ) : (
        <div className="sell-products">
          {visibleProducts.map((product) => {
            const inCart = roundQuantity(cart
              .filter((line) => line.productId === product.id)
              .reduce((sum, line) => sum + line.quantity, 0));
            const unitLabel = allowsFractions(product.unit) ? getUnitShortLabel(product.unit) : '';
            const price = unitLabel ? `${formatMoney(product.priceAmount)}/${unitLabel}` : formatMoney(product.priceAmount);
            const inCartLabel = unitLabel ? formatQuantity(inCart, product.unit) : String(inCart);
            // The badge shows the number only: the tile already says the unit.
            const badge = inCart.toLocaleString('es-MX', { maximumFractionDigits: 3 });
            return (
              <button
                key={product.id}
                type="button"
                className={`sell-product ${inCart > 0 ? 'in-cart' : ''} ${unitLabel ? 'measured' : ''}`}
                onClick={() => onAdd(product)}
                aria-label={`Agregar ${product.name}, ${price}${inCart > 0 ? `, ${inCartLabel} en el ticket` : ''}`}
              >
                {withPhotos && (
                  <PhotoOrInitial src={product.images?.thumbnail} name={product.name} className="sell-product-image" />
                )}
                <span className="sell-product-name">{product.name}</span>
                <span className="sell-product-price ta-money">{price}</span>
                {isServiceProduct(product) && <span className="sell-product-tag">Servicio</span>}
                {unitLabel && !isServiceProduct(product) && <span className="sell-product-tag">Por {unitLabel}</span>}
                {product.tracksStock && product.onHand != null && (product.onHand <= 0 || product.isLowStock) && (
                  <span className={`sell-product-stock ${product.onHand <= 0 ? 'out' : 'low'}`}>
                    {product.onHand <= 0 ? 'Agotado' : `Quedan ${formatQuantity(product.onHand, product.unit)}`}
                  </span>
                )}
                {inCart > 0 && <span className="sell-product-count" aria-hidden="true">{badge}</span>}
              </button>
            );
          })}
        </div>
      )}
    </section>
  );
}
