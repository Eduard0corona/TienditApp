import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import { productsApi } from '../../api/productsApi';
import { ordersApi } from '../../api/ordersApi';
import { customersApi } from '../../api/customersApi';
import type { CreateOrderRequest, CustomerDto, ProductDto } from '../../types';

interface CartLine {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

type CatalogFilter = 'All' | 'Product' | 'Service';

export default function OrdersCart() {
  const navigate = useNavigate();
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [customers, setCustomers] = useState<CustomerDto[]>([]);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [selectedCustomerId, setSelectedCustomerId] = useState('');
  const [customerNameOverride, setCustomerNameOverride] = useState('');
  const [customerPhoneOverride, setCustomerPhoneOverride] = useState('');
  const [tableNumber, setTableNumber] = useState('');
  const [customerNotes, setCustomerNotes] = useState('');
  const [productSearch, setProductSearch] = useState('');
  const [catalogFilter, setCatalogFilter] = useState<CatalogFilter>('All');
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const isService = (product: ProductDto) => product.unit === 50 || product.unit === 51 || product.unit === 52;

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [productsResponse, servicesResponse, customersResponse] = await Promise.all([
        productsApi.getAll({
          isAvailable: true,
          onlyAvailableNow: true,
          itemType: 'Product',
          pageNumber: 1,
          pageSize: 200,
        }),
        productsApi.getAll({
          isAvailable: true,
          onlyAvailableNow: true,
          itemType: 'Service',
          pageNumber: 1,
          pageSize: 200,
        }),
        customersApi.getAll({
          isActive: true,
          pageNumber: 1,
          pageSize: 200,
        }),
      ]);

      const loadedProducts = [...productsResponse.data.items, ...servicesResponse.data.items]
        .sort((a, b) => a.name.localeCompare(b.name));
      const loadedCustomers = customersResponse.data.items;
      const defaultCustomer = loadedCustomers.find((c) => c.isPublicGeneral) ?? loadedCustomers[0];

      setProducts(loadedProducts);
      setCustomers(loadedCustomers);
      setSelectedCustomerId(defaultCustomer?.id ?? '');
    } catch {
      setError('Error al cargar datos para generar la orden.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const addToCart = (product: ProductDto) => {
    setCart((current) => {
      const existing = current.find((line) => line.productId === product.id);
      if (existing) {
        return current.map((line) =>
          line.productId === product.id
            ? { ...line, quantity: line.quantity + 1 }
            : line,
        );
      }

      return [
        ...current,
        {
          productId: product.id,
          productName: product.name,
          unitPrice: product.priceAmount,
          quantity: 1,
        },
      ];
    });
  };

  const updateQuantity = (productId: string, quantity: number) => {
    if (quantity <= 0) {
      setCart((current) => current.filter((line) => line.productId !== productId));
      return;
    }

    setCart((current) =>
      current.map((line) =>
        line.productId === productId ? { ...line, quantity } : line,
      ),
    );
  };

  const removeLine = (productId: string) => {
    setCart((current) => current.filter((line) => line.productId !== productId));
  };

  const total = useMemo(
    () => cart.reduce((sum, line) => sum + line.unitPrice * line.quantity, 0),
    [cart],
  );

  const filteredProducts = useMemo(() => {
    const search = productSearch.trim().toLowerCase();
    return products.filter((product) => {
      const matchesType = catalogFilter === 'All'
        ? true
        : catalogFilter === 'Service'
          ? isService(product)
          : !isService(product);

      if (!matchesType) return false;
      if (!search) return true;

      return product.name.toLowerCase().includes(search) ||
        (product.description || '').toLowerCase().includes(search);
    });
  }, [catalogFilter, productSearch, products]);

  const selectedCustomer = useMemo(
    () => customers.find((customer) => customer.id === selectedCustomerId),
    [customers, selectedCustomerId],
  );
  const selectedCustomerPhone = selectedCustomer?.phone?.trim() || '';
  const requiresPhoneCapture = !selectedCustomer || selectedCustomer.isPublicGeneral || !selectedCustomerPhone;
  const effectiveCustomerPhone = (
    selectedCustomer?.isPublicGeneral || !selectedCustomerPhone
      ? customerPhoneOverride.trim()
      : selectedCustomerPhone
  ).trim();

  const handleCreateOrder = async () => {
    setError('');

    if (cart.length === 0) {
      setError('Agrega al menos un item al carrito.');
      return;
    }

    if (!selectedCustomerId) {
      setError('Selecciona un cliente para generar la orden.');
      return;
    }

    if (!effectiveCustomerPhone) {
      setError('Debes capturar un telefono del cliente para generar la orden.');
      return;
    }

    const payload: CreateOrderRequest = {
      customerId: selectedCustomerId || undefined,
      customerName: customerNameOverride.trim() || undefined,
      customerPhone: effectiveCustomerPhone,
      customerNotes: customerNotes.trim() || undefined,
      tableNumber: tableNumber.trim() || undefined,
      items: cart.map((line) => ({
        productId: line.productId,
        quantity: line.quantity,
      })),
    };

    try {
      setSubmitting(true);
      await ordersApi.create(payload);
      navigate('/orders');
    } catch (err: unknown) {
      const response = (err as {
        response?: {
          data?: {
            error?: string;
            detail?: string;
          };
        };
      })?.response?.data;

      const msg =
        response?.error ||
        response?.detail ||
        'Error al generar la orden.';
      setError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <PageTitle title="Generar Orden" subtitle="Carrito" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}

      <div className="row">
        <div className="col-xl-7">
          <div className="card">
            <div className="card-body">
              <div className="d-flex justify-content-between align-items-center mb-3 gap-2">
                <h4 className="header-title mb-0">Productos y servicios disponibles</h4>
                <div style={{ minWidth: 280 }}>
                  <input
                    type="text"
                    className="form-control form-control-sm"
                    placeholder="Buscar item..."
                    value={productSearch}
                    onChange={(e) => setProductSearch(e.target.value)}
                  />
                </div>
              </div>
              <div className="btn-group mb-3" role="group" aria-label="Filtro catalogo">
                <button
                  type="button"
                  className={`btn btn-sm ${catalogFilter === 'All' ? 'btn-primary' : 'btn-light'}`}
                  onClick={() => setCatalogFilter('All')}
                >
                  Todos
                </button>
                <button
                  type="button"
                  className={`btn btn-sm ${catalogFilter === 'Product' ? 'btn-primary' : 'btn-light'}`}
                  onClick={() => setCatalogFilter('Product')}
                >
                  Productos
                </button>
                <button
                  type="button"
                  className={`btn btn-sm ${catalogFilter === 'Service' ? 'btn-primary' : 'btn-light'}`}
                  onClick={() => setCatalogFilter('Service')}
                >
                  Servicios
                </button>
              </div>

              {loading ? (
                <div className="text-center py-4">
                  <div className="spinner-border text-primary" role="status"></div>
                </div>
              ) : (
                <div className="row g-2">
                  {filteredProducts.map((product) => (
                    <div className="col-md-6 col-lg-4" key={product.id}>
                      <div className="border rounded p-2 h-100 d-flex flex-column">
                        <div className="d-flex justify-content-between align-items-center gap-2">
                          <div className="fw-semibold">{product.name}</div>
                          <span className={`badge ${isService(product) ? 'bg-info' : 'bg-secondary'}`}>
                            {isService(product) ? 'Servicio' : 'Producto'}
                          </span>
                        </div>
                        <div className="text-muted small mb-2">{product.description || '-'}</div>
                        <div className="mt-auto d-flex justify-content-between align-items-center">
                          <span className="fw-semibold">
                            ${product.priceAmount.toFixed(2)} {product.priceCurrency}
                          </span>
                          <button
                            type="button"
                            className="btn btn-sm btn-soft-primary"
                            onClick={() => addToCart(product)}
                          >
                            <i className="ri-add-line me-1"></i>Agregar
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="col-xl-5">
          <div className="card">
            <div className="card-body">
              <div className="d-flex justify-content-between align-items-center mb-3">
                <h4 className="header-title mb-0">Datos de la orden</h4>
                <Link to="/customers" className="btn btn-sm btn-soft-primary">
                  <i className="ri-user-add-line me-1"></i>Nuevo cliente
                </Link>
              </div>

              <div className="mb-2">
                <label className="form-label">Cliente</label>
                <select
                  className="form-select"
                  value={selectedCustomerId}
                  onChange={(e) => setSelectedCustomerId(e.target.value)}
                >
                  {customers.map((customer) => (
                    <option key={customer.id} value={customer.id}>
                      {customer.name}{customer.isPublicGeneral ? ' (Publico en general)' : ''}
                    </option>
                  ))}
                </select>
              </div>

              {selectedCustomer?.isPublicGeneral && (
                <>
                  <div className="mb-2">
                    <label className="form-label">Nombre para la orden (opcional)</label>
                    <input
                      type="text"
                      className="form-control"
                      value={customerNameOverride}
                      onChange={(e) => setCustomerNameOverride(e.target.value)}
                    />
                  </div>
                </>
              )}

              {requiresPhoneCapture && (
                <div className="mb-2">
                  <label className="form-label">Telefono para la orden *</label>
                  <input
                    type="text"
                    className="form-control"
                    value={customerPhoneOverride}
                    onChange={(e) => setCustomerPhoneOverride(e.target.value)}
                    placeholder="Ejemplo: 55 1234 5678"
                  />
                </div>
              )}

              {!selectedCustomer?.isPublicGeneral && (
                <div className="alert alert-light py-2 px-3 mb-2">
                  <div className="small text-muted mb-1">Cliente seleccionado</div>
                  <div className="fw-semibold">{selectedCustomer?.name}</div>
                  <div className="small">{selectedCustomer?.phone || 'Sin telefono registrado'}</div>
                  {!selectedCustomer?.phone && (
                    <div className="small text-danger">Captura telefono para poder generar la orden.</div>
                  )}
                </div>
              )}

              <div className="mb-2">
                <label className="form-label">Mesa / Referencia</label>
                <input
                  type="text"
                  className="form-control"
                  value={tableNumber}
                  onChange={(e) => setTableNumber(e.target.value)}
                />
              </div>

              <div className="mb-0">
                <label className="form-label">Notas</label>
                <textarea
                  className="form-control"
                  rows={3}
                  value={customerNotes}
                  onChange={(e) => setCustomerNotes(e.target.value)}
                />
              </div>
            </div>
          </div>

          <div className="card">
            <div className="card-body">
              <h4 className="header-title mb-3">Carrito</h4>
              {cart.length === 0 ? (
                <p className="text-muted mb-0">No hay items en el carrito.</p>
              ) : (
                <>
                  <div className="table-responsive">
                    <table className="table table-sm mb-2">
                      <thead>
                        <tr>
                          <th>Producto</th>
                          <th className="text-center">Cant.</th>
                          <th className="text-end">Subtotal</th>
                          <th></th>
                        </tr>
                      </thead>
                      <tbody>
                        {cart.map((line) => (
                          <tr key={line.productId}>
                            <td>{line.productName}</td>
                            <td className="text-center" style={{ width: 90 }}>
                              <input
                                type="number"
                                min={1}
                                className="form-control form-control-sm text-center"
                                value={line.quantity}
                                onChange={(e) =>
                                  updateQuantity(
                                    line.productId,
                                    Number(e.target.value || 0),
                                  )
                                }
                              />
                            </td>
                            <td className="text-end">
                              ${(line.unitPrice * line.quantity).toFixed(2)}
                            </td>
                            <td className="text-end">
                              <button
                                type="button"
                                className="btn btn-sm btn-soft-danger"
                                onClick={() => removeLine(line.productId)}
                              >
                                <i className="ri-delete-bin-line"></i>
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  <div className="d-flex justify-content-between align-items-center mb-3">
                    <span className="fw-semibold">Total</span>
                    <span className="fw-bold">${total.toFixed(2)}</span>
                  </div>
                </>
              )}

              <div className="d-grid gap-2">
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={submitting}
                  onClick={handleCreateOrder}
                >
                  {submitting ? 'Generando...' : 'Generar Orden'}
                </button>
                <Link to="/orders" className="btn btn-light">
                  Cancelar
                </Link>
              </div>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
