import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Modal, Offcanvas } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { productsApi } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import { ordersApi } from '../../api/ordersApi';
import { cashApi } from '../../api/cashApi';
import { getApiErrorMessage } from '../../api/errors';
import ChargePanel, { type ChargeResult } from '../../components/sales/ChargePanel';
import SaleTicket from '../../components/sales/SaleTicket';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import type { CategoryDto, CreateOrderRequest, OrderDto, ProductDto } from '../../types';
import { BusinessType, OrderType, TenantCapability, UnitType } from '../../types';
import { getAccess } from '../../utils/access';
import { formatMoney } from '../../utils/money';
import { estimateOrderTotals, getVatLabel, resolveProductVat } from '../../utils/orderTotals';
import { hasCapability } from '../../utils/tenantCapabilities';
import { buildTicketText, buildWhatsAppTicketUrl, printTicket } from '../../utils/ticket';
import { readSellDraft, writeSellDraft } from './sellDraft';
import './SellPage.css';

// Same limits as the API (OrderLimits).
const MAX_QUANTITY_PER_LINE = 10_000;
const MAX_LINES = 200;
// The largest page the API serves; a bigger catalog is searched on the server.
const CATALOG_PAGE_SIZE = 500;

interface CartLine {
  productId: string;
  name: string;
  unitPrice: number;
  quantity: number;
  notes: string;
  showNotes: boolean;
  isService: boolean;
  ratePercent: number;
  isExempt: boolean;
}

interface CompletedSale {
  order: OrderDto;
  change: number;
  cashReceived: number;
}

type CategoryFilter = 'all' | 'featured' | string;

const SERVICE_UNITS: ReadonlyArray<number> = [UnitType.Hour, UnitType.Session, UnitType.Service];

function isServiceProduct(product: ProductDto): boolean {
  return product.unit !== undefined && SERVICE_UNITS.includes(product.unit);
}

// Quick feedback only; the API normalizes and validates the number.
function isPlausiblePhone(value: string): boolean {
  const digits = value.replace(/\D/g, '');
  return digits.length >= 8 && digits.length <= 15;
}

/**
 * Vender: touch catalog, ticket and charge in one screen. The cart survives a reload, F9 charges and
 * the server computes the final total (the sale is rejected if a price changed since it was shown).
 */
export default function SellPage() {
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const toast = useToast();
  const access = getAccess(user, capabilities);
  const tenantId = tenant?.id ?? '';
  const pricesIncludeTax = tenant?.pricesIncludeTax ?? false;
  const defaultTaxRate = tenant?.defaultTaxRate ?? 16;
  const isFood = tenant?.businessType === BusinessType.Food;
  const canReceiveServices = hasCapability(capabilities, TenantCapability.Service);

  const [products, setProducts] = useState<ProductDto[]>([]);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [catalogTruncated, setCatalogTruncated] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [cart, setCart] = useState<CartLine[]>([]);
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [tableNumber, setTableNumber] = useState('');
  const [orderNotes, setOrderNotes] = useState('');
  const [showDetails, setShowDetails] = useState(false);
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all');
  const [chargeOpen, setChargeOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [saleError, setSaleError] = useState('');
  const [completedSale, setCompletedSale] = useState<CompletedSale | null>(null);
  const [cartOpenOnPhone, setCartOpenOnPhone] = useState(false);
  const [cashClosed, setCashClosed] = useState(false);
  const draftLoadedFor = useRef('');
  const searchInputRef = useRef<HTMLInputElement>(null);

  const toCartLine = useCallback(
    (product: ProductDto, quantity: number, notes = ''): CartLine => {
      const vat = resolveProductVat(product, defaultTaxRate);
      return {
        productId: product.id,
        name: product.name,
        unitPrice: product.priceAmount,
        quantity,
        notes,
        showNotes: notes.length > 0,
        isService: isServiceProduct(product),
        ratePercent: vat.ratePercent,
        isExempt: vat.isExempt,
      };
    },
    [defaultTaxRate],
  );

  const loadCatalog = useCallback(async () => {
    if (!tenantId) return;
    try {
      const [productsResponse, categoriesResponse] = await Promise.all([
        productsApi.getAll({ isAvailable: true, onlyAvailableNow: true, pageNumber: 1, pageSize: CATALOG_PAGE_SIZE }),
        categoriesApi.getAll(),
      ]);
      const loaded = productsResponse.data.items;
      setProducts(loaded);
      setCategories(categoriesResponse.data);
      setCatalogTruncated(productsResponse.data.totalCount > loaded.length);
      setLoadError('');

      const byId = new Map(loaded.map((product) => [product.id, product]));
      if (draftLoadedFor.current !== tenantId) {
        // First load for this business: bring back the sale that was being built.
        draftLoadedFor.current = tenantId;
        const draft = readSellDraft(tenantId);
        if (draft) {
          setCart(draft.lines.flatMap((line) => {
            const product = byId.get(line.productId);
            return product ? [toCartLine(product, Math.min(line.quantity, MAX_QUANTITY_PER_LINE), line.notes)] : [];
          }));
          setCustomerName(draft.customerName);
          setCustomerPhone(draft.customerPhone);
          setTableNumber(draft.tableNumber);
          setOrderNotes(draft.orderNotes);
          setShowDetails(Boolean(draft.customerName || draft.customerPhone || draft.tableNumber || draft.orderNotes));
        }
      } else {
        // Reload (a price changed): keep the cart with current prices and drop what is no longer sold.
        setCart((current) => current.flatMap((line) => {
          const product = byId.get(line.productId);
          return product ? [{ ...toCartLine(product, line.quantity, line.notes), showNotes: line.showNotes }] : [];
        }));
      }
    } catch (err: unknown) {
      setLoadError(getApiErrorMessage(err, 'No se pudo cargar el catálogo. Revisa tu conexión e intenta de nuevo.'));
    } finally {
      setLoading(false);
    }
  }, [tenantId, toCartLine]);

  useEffect(() => {
    void loadCatalog();
  }, [loadCatalog]);

  useEffect(() => {
    // Only people who manage the cash drawer can see it; for them a closed drawer is worth a warning.
    if (!access.cash || !tenantId) return;
    let cancelled = false;
    cashApi.getCurrentSession()
      .then(({ data }) => {
        if (!cancelled) setCashClosed(!data);
      })
      .catch(() => {
        if (!cancelled) setCashClosed(false);
      });
    return () => {
      cancelled = true;
    };
  }, [access.cash, tenantId]);

  useEffect(() => {
    if (!tenantId || draftLoadedFor.current !== tenantId) return;
    writeSellDraft(tenantId, {
      lines: cart.map((line) => ({ productId: line.productId, quantity: line.quantity, notes: line.notes })),
      customerName,
      customerPhone,
      tableNumber,
      orderNotes,
    });
  }, [cart, customerName, customerPhone, orderNotes, tableNumber, tenantId]);

  const totals = useMemo(
    () => estimateOrderTotals(
      cart.map((line) => ({
        unitPrice: line.unitPrice,
        quantity: line.quantity,
        ratePercent: line.ratePercent,
        isExempt: line.isExempt,
      })),
      pricesIncludeTax,
    ),
    [cart, pricesIncludeTax],
  );
  const itemCount = cart.reduce((sum, line) => sum + line.quantity, 0);
  const hasServices = cart.some((line) => line.isService);
  const phoneError = customerPhone.trim() && !isPlausiblePhone(customerPhone)
    ? 'Revisa el teléfono: 10 dígitos o el número con lada internacional (+52…).'
    : '';

  const categoriesWithProducts = useMemo(() => {
    const used = new Set(products.map((product) => product.categoryId));
    return categories
      .filter((category) => used.has(category.id))
      .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
  }, [categories, products]);
  const hasFeatured = products.some((product) => product.isFeatured);

  const visibleProducts = useMemo(() => {
    const term = search.trim().toLowerCase();
    return products
      .filter((product) => {
        if (categoryFilter === 'featured' && !product.isFeatured) return false;
        if (categoryFilter !== 'all' && categoryFilter !== 'featured' && product.categoryId !== categoryFilter) return false;
        if (!term) return true;
        return product.name.toLowerCase().includes(term)
          || (product.sku ?? '').toLowerCase().includes(term)
          || (product.barcode ?? '') === term;
      })
      .sort((a, b) => Number(b.isFeatured) - Number(a.isFeatured) || a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
  }, [categoryFilter, products, search]);

  const addProduct = useCallback((product: ProductDto) => {
    setSaleError('');
    setCart((current) => {
      const existing = current.find((line) => line.productId === product.id);
      if (existing) {
        return current.map((line) => (line.productId === product.id
          ? { ...line, quantity: Math.min(line.quantity + 1, MAX_QUANTITY_PER_LINE) }
          : line));
      }
      if (current.length >= MAX_LINES) {
        toast.warning(`Una venta admite hasta ${MAX_LINES} productos distintos.`);
        return current;
      }
      return [...current, toCartLine(product, 1)];
    });
  }, [toCartLine, toast]);

  const changeQuantity = (productId: string, quantity: number) => {
    setCart((current) => (quantity <= 0
      ? current.filter((line) => line.productId !== productId)
      : current.map((line) => (line.productId === productId
        ? { ...line, quantity: Math.min(quantity, MAX_QUANTITY_PER_LINE) }
        : line))));
  };

  const updateLine = (productId: string, changes: Partial<CartLine>) => {
    setCart((current) => current.map((line) => (line.productId === productId ? { ...line, ...changes } : line)));
  };

  const clearSale = () => {
    setCart([]);
    setCustomerName('');
    setCustomerPhone('');
    setTableNumber('');
    setOrderNotes('');
    setShowDetails(false);
    setSaleError('');
  };

  const handleSearchKeyDown = async (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    const term = search.trim();
    if (!term) return;
    // A barcode scanner types the code and presses Enter: add the exact match.
    const exact = products.find((product) => product.barcode === term || product.sku?.toLowerCase() === term.toLowerCase());
    if (exact) {
      addProduct(exact);
      setSearch('');
      return;
    }
    if (visibleProducts.length === 1) {
      addProduct(visibleProducts[0]);
      setSearch('');
      return;
    }
    if (visibleProducts.length === 0 && catalogTruncated) {
      try {
        const { data } = await productsApi.getAll({ isAvailable: true, onlyAvailableNow: true, search: term, pageSize: 20 });
        if (data.items.length === 1) {
          addProduct(data.items[0]);
          setSearch('');
        } else if (data.items.length > 1) {
          setProducts((current) => [...current, ...data.items.filter((item) => !current.some((p) => p.id === item.id))]);
        } else {
          toast.info(`No hay productos que coincidan con “${term}”.`);
        }
      } catch (err: unknown) {
        toast.error(getApiErrorMessage(err, 'No se pudo buscar el producto.'));
      }
    }
  };

  const validateSale = (): boolean => {
    if (cart.length === 0) {
      setSaleError('Agrega al menos un producto.');
      return false;
    }
    if (phoneError) {
      setSaleError(phoneError);
      setShowDetails(true);
      return false;
    }
    return true;
  };

  const openCharge = useCallback(() => {
    if (cart.length === 0 || submitting) return;
    setSaleError('');
    setCartOpenOnPhone(false);
    setChargeOpen(true);
  }, [cart.length, submitting]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'F9') {
        event.preventDefault();
        openCharge();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [openCharge]);

  const buildOrderRequest = (): CreateOrderRequest => ({
    customerName: customerName.trim() || undefined,
    customerPhone: customerPhone.trim() || undefined,
    customerNotes: orderNotes.trim() || undefined,
    tableNumber: isFood ? tableNumber.trim() || undefined : undefined,
    // A service in the cart makes it a service order (pieces to track, if the service is trackable).
    orderType: hasServices ? OrderType.Service : OrderType.Retail,
    items: cart.map((line) => ({
      productId: line.productId,
      quantity: line.quantity,
      notes: line.notes.trim() || undefined,
    })),
    expectedTotal: totals.total,
  });

  const submitSale = async (charge: ChargeResult | null) => {
    if (!validateSale()) {
      setChargeOpen(false);
      return;
    }
    setSubmitting(true);
    setSaleError('');
    try {
      const request = buildOrderRequest();
      const { data: order } = await ordersApi.create({
        ...request,
        payments: charge?.payments.map((payment) => ({
          amount: payment.amount,
          method: payment.method,
          reference: payment.reference,
          evidenceUrl: payment.evidenceUrl,
        })),
      });
      clearSale();
      setChargeOpen(false);
      if (charge) {
        setCompletedSale({ order, change: charge.change, cashReceived: charge.cashReceived });
      } else {
        toast.success(`Pedido ${order.orderNumber} guardado. Cóbralo desde Pedidos.`);
      }
    } catch (err: unknown) {
      const message = getApiErrorMessage(err, 'No se pudo registrar la venta.');
      setSaleError(message);
      if (!charge) toast.error(message);
      // A price or availability may have changed: refresh the catalog and the cart prices.
      void loadCatalog();
    } finally {
      setSubmitting(false);
    }
  };

  const startNewSale = () => {
    setCompletedSale(null);
    window.setTimeout(() => searchInputRef.current?.focus(), 0);
  };

  const businessName = tenant?.name ?? 'TienditApp';
  const ticketText = completedSale ? buildTicketText(completedSale.order, {
    businessName,
    change: completedSale.change,
    cashReceived: completedSale.cashReceived,
  }) : '';

  const cartPanel = (
    <div className="sell-cart-panel">
      <div className="d-flex align-items-center justify-content-between mb-2">
        <h2 className="h5 mb-0">Ticket</h2>
        {cart.length > 0 && (
          <button type="button" className="btn btn-sm btn-link text-danger" onClick={clearSale} disabled={submitting}>
            Vaciar
          </button>
        )}
      </div>

      {cart.length === 0 ? (
        <div className="sell-empty text-muted">
          <i className="ri-shopping-basket-2-line" aria-hidden="true"></i>
          <p className="mb-0">Toca un producto para agregarlo.</p>
        </div>
      ) : (
        <ul className="sell-lines list-unstyled mb-2">
          {cart.map((line) => (
            <li key={line.productId} className="sell-line">
              <div className="d-flex justify-content-between gap-2">
                <div className="sell-line-name">
                  {line.name}
                  <div className="small text-muted">{formatMoney(line.unitPrice)} c/u</div>
                </div>
                <strong className="ta-money">{formatMoney(line.unitPrice * line.quantity)}</strong>
              </div>
              <div className="d-flex align-items-center justify-content-between gap-2 mt-1">
                <div className="sell-stepper" role="group" aria-label={`Cantidad de ${line.name}`}>
                  <button
                    type="button"
                    className="btn btn-light"
                    aria-label={line.quantity === 1 ? `Quitar ${line.name}` : `Quitar uno de ${line.name}`}
                    onClick={() => changeQuantity(line.productId, line.quantity - 1)}
                  >
                    <i className={line.quantity === 1 ? 'ri-delete-bin-line' : 'ri-subtract-line'} aria-hidden="true"></i>
                  </button>
                  <input
                    className="form-control text-center ta-money"
                    inputMode="numeric"
                    aria-label={`Cantidad de ${line.name}`}
                    value={line.quantity}
                    onChange={(event) => {
                      const parsed = Number.parseInt(event.target.value, 10);
                      if (!Number.isNaN(parsed)) changeQuantity(line.productId, parsed);
                    }}
                  />
                  <button
                    type="button"
                    className="btn btn-light"
                    aria-label={`Agregar uno de ${line.name}`}
                    onClick={() => changeQuantity(line.productId, line.quantity + 1)}
                  >
                    <i className="ri-add-line" aria-hidden="true"></i>
                  </button>
                </div>
                <button
                  type="button"
                  className={`btn btn-sm ${line.showNotes ? 'btn-soft-warning' : 'btn-light'}`}
                  onClick={() => updateLine(line.productId, { showNotes: !line.showNotes })}
                  aria-expanded={line.showNotes}
                >
                  <i className="ri-sticky-note-line me-1" aria-hidden="true"></i>Nota
                </button>
              </div>
              {line.showNotes && (
                <input
                  className="form-control form-control-sm mt-2"
                  placeholder="Ej. sin cebolla, para llevar"
                  aria-label={`Nota para ${line.name}`}
                  maxLength={300}
                  value={line.notes}
                  onChange={(event) => updateLine(line.productId, { notes: event.target.value })}
                />
              )}
            </li>
          ))}
        </ul>
      )}

      <button
        type="button"
        className="btn btn-link btn-sm px-0 mb-2"
        onClick={() => setShowDetails((value) => !value)}
        aria-expanded={showDetails}
      >
        <i className={`${showDetails ? 'ri-arrow-up-s-line' : 'ri-user-add-line'} me-1`} aria-hidden="true"></i>
        {showDetails ? 'Ocultar datos del cliente' : 'Cliente, mesa y notas (opcional)'}
      </button>
      {showDetails && (
        <div className="sell-details mb-2">
          <div className="row g-2">
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor="sell-customer-name">Nombre</label>
              <input
                id="sell-customer-name"
                className="form-control"
                maxLength={200}
                placeholder="Público en general"
                value={customerName}
                onChange={(event) => setCustomerName(event.target.value)}
              />
            </div>
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor="sell-customer-phone">Teléfono</label>
              <input
                id="sell-customer-phone"
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                className={`form-control ${phoneError ? 'is-invalid' : ''}`}
                placeholder="55 1234 5678"
                value={customerPhone}
                onChange={(event) => setCustomerPhone(event.target.value)}
                aria-describedby="sell-customer-phone-help"
              />
              <div id="sell-customer-phone-help" className={phoneError ? 'invalid-feedback d-block' : 'form-text'}>
                {phoneError || 'Con teléfono la venta queda en su historial.'}
              </div>
            </div>
            {isFood && (
              <div className="col-sm-6">
                <label className="form-label small mb-1" htmlFor="sell-table">Mesa o referencia</label>
                <input
                  id="sell-table"
                  className="form-control"
                  maxLength={50}
                  value={tableNumber}
                  onChange={(event) => setTableNumber(event.target.value)}
                />
              </div>
            )}
            <div className={isFood ? 'col-sm-6' : 'col-12'}>
              <label className="form-label small mb-1" htmlFor="sell-notes">Notas del pedido</label>
              <input
                id="sell-notes"
                className="form-control"
                maxLength={1000}
                value={orderNotes}
                onChange={(event) => setOrderNotes(event.target.value)}
              />
            </div>
          </div>
        </div>
      )}

      <div className="sell-totals">
        {!pricesIncludeTax && (
          <div className="d-flex justify-content-between">
            <span>Subtotal</span>
            <span className="ta-money">{formatMoney(totals.subtotal)}</span>
          </div>
        )}
        {totals.breakdown.filter((line) => line.taxAmount > 0).map((line) => (
          <div className="d-flex justify-content-between small text-muted" key={`${line.ratePercent}-${line.isExempt}`}>
            <span>{pricesIncludeTax ? `Incluye ${getVatLabel(line)}` : getVatLabel(line)}</span>
            <span className="ta-money">{formatMoney(line.taxAmount)}</span>
          </div>
        ))}
        <div className="d-flex justify-content-between align-items-baseline sell-total">
          <span>Total</span>
          <span className="ta-money">{formatMoney(totals.total)}</span>
        </div>
      </div>

      {saleError && !chargeOpen && <div className="alert alert-danger py-2 mt-2 mb-0">{saleError}</div>}

      <div className="d-grid gap-2 mt-3">
        <button
          type="button"
          className="btn btn-primary btn-touch btn-touch-lg"
          onClick={openCharge}
          disabled={cart.length === 0 || submitting}
        >
          Cobrar {formatMoney(totals.total)} <span className="sell-kbd">F9</span>
        </button>
        <button
          type="button"
          className="btn btn-light btn-touch"
          onClick={() => void submitSale(null)}
          disabled={cart.length === 0 || submitting}
        >
          Guardar sin cobrar
        </button>
      </div>
    </div>
  );

  return (
    <div className="sell-page">
      <div className="sell-header d-flex flex-wrap align-items-center justify-content-between gap-2">
        <h1 className="h3 mb-0">Vender</h1>
        <div className="d-flex flex-wrap gap-2">
          {canReceiveServices && access.sell && (
            <Link to="/orders/cart" className="btn btn-sm btn-light">
              <i className="ri-t-shirt-line me-1" aria-hidden="true"></i>Recibir servicio con piezas
            </Link>
          )}
          {access.orders && (
            <Link to="/orders" className="btn btn-sm btn-light">
              <i className="ri-file-list-3-line me-1" aria-hidden="true"></i>Pedidos
            </Link>
          )}
        </div>
      </div>

      {cashClosed && (
        <div className="alert alert-warning d-flex flex-wrap align-items-center justify-content-between gap-2 mt-3 mb-0" role="status">
          <span>
            <i className="ri-safe-2-line me-1" aria-hidden="true"></i>
            La caja está cerrada: los cobros se guardan, pero no entran al corte.
          </span>
          <Link to="/cash" className="btn btn-sm btn-warning">Abrir caja</Link>
        </div>
      )}

      <div className="sell-layout mt-3">
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
              onChange={(event) => setSearch(event.target.value)}
              onKeyDown={(event) => void handleSearchKeyDown(event)}
              autoComplete="off"
            />
          </div>

          <div className="sell-chips" role="tablist" aria-label="Categorías">
            <button
              type="button"
              role="tab"
              aria-selected={categoryFilter === 'all'}
              className={`btn btn-touch ${categoryFilter === 'all' ? 'btn-primary' : 'btn-light'}`}
              onClick={() => setCategoryFilter('all')}
            >
              Todos
            </button>
            {hasFeatured && (
              <button
                type="button"
                role="tab"
                aria-selected={categoryFilter === 'featured'}
                className={`btn btn-touch ${categoryFilter === 'featured' ? 'btn-primary' : 'btn-light'}`}
                onClick={() => setCategoryFilter('featured')}
              >
                <i className="ri-star-line me-1" aria-hidden="true"></i>Favoritos
              </button>
            )}
            {categoriesWithProducts.map((category) => (
              <button
                key={category.id}
                type="button"
                role="tab"
                aria-selected={categoryFilter === category.id}
                className={`btn btn-touch ${categoryFilter === category.id ? 'btn-primary' : 'btn-light'}`}
                onClick={() => setCategoryFilter(category.id)}
              >
                {category.name}
              </button>
            ))}
          </div>

          {loadError && (
            <div className="alert alert-danger d-flex justify-content-between align-items-center gap-2">
              <span>{loadError}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void loadCatalog()}>Reintentar</button>
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
          ) : products.length === 0 && !loadError ? (
            <div className="card">
              <div className="card-body text-center py-5">
                <i className="ri-store-2-line fs-1 text-muted" aria-hidden="true"></i>
                <p className="mt-2 mb-3">Todavía no hay productos disponibles para vender.</p>
                {access.catalog && <Link to="/products/new" className="btn btn-primary">Agregar producto</Link>}
              </div>
            </div>
          ) : visibleProducts.length === 0 ? (
            <p className="text-muted py-4 text-center">No hay productos que coincidan con la búsqueda.</p>
          ) : (
            <div className="sell-products">
              {visibleProducts.map((product) => {
                const inCart = cart.find((line) => line.productId === product.id)?.quantity ?? 0;
                return (
                  <button
                    key={product.id}
                    type="button"
                    className={`sell-product ${inCart > 0 ? 'in-cart' : ''}`}
                    onClick={() => addProduct(product)}
                    aria-label={`Agregar ${product.name}, ${formatMoney(product.priceAmount)}${inCart > 0 ? `, ${inCart} en el ticket` : ''}`}
                  >
                    {product.images?.thumbnail && (
                      <img src={product.images.thumbnail} alt="" className="sell-product-image" loading="lazy" />
                    )}
                    <span className="sell-product-name">{product.name}</span>
                    <span className="sell-product-price ta-money">{formatMoney(product.priceAmount)}</span>
                    {isServiceProduct(product) && <span className="sell-product-tag">Servicio</span>}
                    {inCart > 0 && <span className="sell-product-count" aria-hidden="true">{inCart}</span>}
                  </button>
                );
              })}
            </div>
          )}
        </section>

        <aside className="sell-cart card d-none d-lg-block" aria-label="Ticket de la venta">
          <div className="card-body">{cartPanel}</div>
        </aside>
      </div>

      {/* Phones: the ticket lives in a bottom sheet behind this bar. */}
      <div className="sell-phone-bar d-lg-none">
        <button type="button" className="btn btn-light btn-touch flex-grow-1" onClick={() => setCartOpenOnPhone(true)}>
          <i className="ri-shopping-basket-2-line me-1" aria-hidden="true"></i>
          {itemCount} {itemCount === 1 ? 'artículo' : 'artículos'} · <span className="ta-money">{formatMoney(totals.total)}</span>
        </button>
        <button type="button" className="btn btn-primary btn-touch" onClick={openCharge} disabled={cart.length === 0}>
          Cobrar
        </button>
      </div>
      <Offcanvas show={cartOpenOnPhone} onHide={() => setCartOpenOnPhone(false)} placement="bottom" className="sell-sheet">
        <Offcanvas.Header closeButton>
          <Offcanvas.Title as="span" className="visually-hidden">Ticket</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>{cartPanel}</Offcanvas.Body>
      </Offcanvas>

      <Modal show={chargeOpen} onHide={() => !submitting && setChargeOpen(false)} centered size="lg" fullscreen="sm-down">
        <Modal.Header closeButton={!submitting}>
          <Modal.Title as="h2" className="h5">Cobrar</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {chargeOpen && (
            <ChargePanel
              amountDue={totals.total}
              submitting={submitting}
              error={saleError}
              onSubmit={(charge) => void submitSale(charge)}
              onCancel={() => setChargeOpen(false)}
              uploadEvidence={async (file) => {
                const { data } = await ordersApi.uploadPaymentEvidence(file);
                return { url: data.url, fileName: data.fileName };
              }}
            />
          )}
        </Modal.Body>
      </Modal>

      <Modal show={!!completedSale} onHide={startNewSale} centered fullscreen="sm-down">
        <Modal.Header closeButton>
          <Modal.Title as="h2" className="h5">
            <i className="ri-checkbox-circle-fill text-success me-1" aria-hidden="true"></i>
            Venta {completedSale?.order.orderNumber} cobrada
          </Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {completedSale && (
            <>
              {completedSale.change > 0 && (
                <div className="charge-change has-change mb-3" aria-live="polite">
                  <span>Cambio</span>
                  <strong className="ta-money">{formatMoney(completedSale.change)}</strong>
                </div>
              )}
              <SaleTicket
                order={completedSale.order}
                businessName={businessName}
                change={completedSale.change}
                cashReceived={completedSale.cashReceived}
              />
            </>
          )}
        </Modal.Body>
        <Modal.Footer className="flex-wrap">
          <button type="button" className="btn btn-light btn-touch" onClick={printTicket}>
            <i className="ri-printer-line me-1" aria-hidden="true"></i>Imprimir
          </button>
          <a
            className="btn btn-light btn-touch"
            href={buildWhatsAppTicketUrl(ticketText, completedSale?.order.customerPhone)}
            target="_blank"
            rel="noopener noreferrer"
          >
            <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Enviar por WhatsApp
          </a>
          <button type="button" className="btn btn-primary btn-touch flex-grow-1" onClick={startNewSale} autoFocus>
            Nueva venta
          </button>
        </Modal.Footer>
      </Modal>
    </div>
  );
}
