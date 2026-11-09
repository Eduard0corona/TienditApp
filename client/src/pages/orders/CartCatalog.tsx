import type { KeyboardEvent } from 'react';
import type { OrderType, ProductDto } from '../../types';
import { OrderType as OrderTypeConst } from '../../types';
import { formatMoney } from '../../utils/money';
import { isServiceProduct } from './ordersCartModel';

interface CartCatalogProps {
  orderType: OrderType;
  /** What the person typed to search the catalog. */
  search: string;
  onSearchChange: (search: string) => void;
  loading: boolean;
  /** The products or services of the filter picked that match the search. */
  products: ProductDto[];
  onAdd: (product: ProductDto) => void;
}

/** The catalog of Nuevo pedido: a card per product or service, added to the cart with a click, Enter or Space. */
export default function CartCatalog({ orderType, search, onSearchChange, loading, products, onAdd }: CartCatalogProps) {
  const onCardKeyDown = (event: KeyboardEvent<HTMLDivElement>, product: ProductDto) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onAdd(product);
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <div className="orders-catalog-toolbar d-flex justify-content-between align-items-center mb-3 gap-2">
          <h4 className="header-title mb-0">
            {orderType === OrderTypeConst.Service ? 'Servicios disponibles' : 'Productos disponibles'}
          </h4>
          <div className="orders-catalog-search">
            <input
              type="text"
              className="form-control form-control-sm"
              placeholder="Buscar item..."
              value={search}
              onChange={(e) => onSearchChange(e.target.value)}
            />
          </div>
        </div>

        {loading ? (
          <div className="text-center py-4">
            <div className="spinner-border text-primary" role="status"></div>
          </div>
        ) : (
          <div className="row g-2">
            {products.map((product) => (
              <div className="col-md-6 col-lg-4" key={product.id}>
                <div
                  role="button"
                  tabIndex={0}
                  className="order-catalog-card border rounded p-2 h-100 d-flex flex-column"
                  onClick={() => onAdd(product)}
                  onKeyDown={(event) => onCardKeyDown(event, product)}
                  title="Agregar al carrito"
                >
                  <div className="d-flex justify-content-between align-items-center gap-2">
                    <div className="fw-semibold">{product.name}</div>
                    <span className={`badge ${isServiceProduct(product) ? 'bg-info' : 'bg-secondary'}`}>
                      {isServiceProduct(product) ? 'Servicio' : 'Producto'}
                    </span>
                  </div>
                  <div className="text-muted small mb-2">{product.description || '-'}</div>
                  <div className="mt-auto d-flex justify-content-between align-items-center">
                    <span className="fw-semibold ta-money">
                      {formatMoney(product.priceAmount)}
                    </span>
                    <span className="text-muted small">Click para agregar</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
