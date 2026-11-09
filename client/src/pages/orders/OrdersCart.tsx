import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import { productsApi } from '../../api/productsApi';
import { ordersApi } from '../../api/ordersApi';
import { customersApi } from '../../api/customersApi';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { estimateOrderTotals } from '../../utils/orderTotals';
import { getApiErrorMessage } from '../../api/errors';
import './OrdersCart.css';
import type { CreateOrderRequest, CustomerDto, OrderType, ProductDto } from '../../types';
import { BusinessType as BusinessTypeConst, OrderType as OrderTypeConst } from '../../types';
import { STOCK_WARNING_TITLE, stockWarningMessage } from '../../utils/stockWarnings';
import CartCatalog from './CartCatalog';
import CartSummary from './CartSummary';
import ServiceIntakeForm from './ServiceIntakeForm';
import { useServiceIntake } from './useServiceIntake';
import {
  addProductLine,
  filterCatalog,
  isPlausiblePhone,
  isServiceProduct,
  withLineQuantity,
  type CartLine,
} from './ordersCartModel';

export default function OrdersCart() {
  const navigate = useNavigate();
  const { tenant } = useTenant();
  const toast = useToast();
  const pricesIncludeTax = tenant?.pricesIncludeTax ?? false;
  const defaultTaxRate = tenant?.defaultTaxRate ?? 16;
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [customers, setCustomers] = useState<CustomerDto[]>([]);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [orderType, setOrderType] = useState<OrderType>(OrderTypeConst.Retail);
  const [selectedCustomerId, setSelectedCustomerId] = useState('');
  const [customerNameOverride, setCustomerNameOverride] = useState('');
  const [customerPhoneOverride, setCustomerPhoneOverride] = useState('');
  const [tableNumber, setTableNumber] = useState('');
  const [customerNotes, setCustomerNotes] = useState('');
  const [productSearch, setProductSearch] = useState('');
  // The received date of a service starts when the order was opened (useServiceIntake).
  const [openedAt] = useState(() => new Date());
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [truncationNotice, setTruncationNotice] = useState('');
  const intake = useServiceIntake(openedAt, setError);
  const showsTableReferenceField = tenant?.businessType === BusinessTypeConst.Food;

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
      // The POS loads the first page of each catalog: say so instead of silently hiding the rest.
      const truncated = [productsResponse.data, servicesResponse.data, customersResponse.data]
        .some((page) => page.totalCount > page.items.length);
      setTruncationNotice(truncated
        ? 'El catálogo o la lista de clientes tiene más registros de los que se muestran aquí (máximo 200 por lista).'
        : '');
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

  useEffect(() => {
    setError('');
  }, [orderType]);

  const serviceCatalogItems = useMemo(
    () => products.filter((product) => isServiceProduct(product)),
    [products],
  );

  const serviceCartLines = useMemo(
    () => cart.filter((line) => line.isService),
    [cart],
  );

  const addToCart = (product: ProductDto) => {
    setCart((current) => addProductLine(current, product, defaultTaxRate));
  };

  const updateQuantity = (productId: string, quantity: number) => {
    setCart((current) => withLineQuantity(current, productId, quantity));
  };

  const removeLine = (productId: string) => {
    setCart((current) => current.filter((line) => line.productId !== productId));
  };

  // Subtotal, IVA and total as the API will compute them, with the business IVA policy.
  const totals = useMemo(() => estimateOrderTotals(cart, pricesIncludeTax), [cart, pricesIncludeTax]);
  const hasServiceItems = useMemo(() => cart.some((line) => line.isService), [cart]);
  const hasProductItems = useMemo(() => cart.some((line) => !line.isService), [cart]);
  const resolvedOrderType = useMemo(
    () => (hasServiceItems ? OrderTypeConst.Service : OrderTypeConst.Retail),
    [hasServiceItems],
  );
  const isMixedCart = hasServiceItems && hasProductItems;

  const filteredProducts = useMemo(
    () => filterCatalog(products, orderType, productSearch),
    [orderType, productSearch, products],
  );

  const selectedCustomer = useMemo(
    () => customers.find((customer) => customer.id === selectedCustomerId),
    [customers, selectedCustomerId],
  );
  const selectedCustomerPhone = selectedCustomer?.phone?.trim() || '';
  const requiresPhoneCapture = !selectedCustomer || selectedCustomer.isPublicGeneral || !selectedCustomerPhone;
  // Same rule as CreateOrderCommand: a sale to the general public may skip the phone; a named customer
  // is linked to their orders through it.
  const phoneIsOptional = !selectedCustomer || selectedCustomer.isPublicGeneral;
  const effectiveCustomerPhone = (
    selectedCustomer?.isPublicGeneral || !selectedCustomerPhone
      ? customerPhoneOverride.trim()
      : selectedCustomerPhone
  ).trim();
  const phoneFormatError = effectiveCustomerPhone && !isPlausiblePhone(effectiveCustomerPhone)
    ? 'Revisa el teléfono: escribe 10 dígitos o el número con lada internacional (+52…).'
    : '';

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

    if (!effectiveCustomerPhone && !phoneIsOptional) {
      setError('Captura el teléfono del cliente seleccionado para generar la orden.');
      return;
    }

    if (phoneFormatError) {
      setError(phoneFormatError);
      return;
    }

    const payload: CreateOrderRequest = {
      customerId: selectedCustomerId || undefined,
      customerName: customerNameOverride.trim() || undefined,
      customerPhone: effectiveCustomerPhone || undefined,
      customerNotes: customerNotes.trim() || undefined,
      tableNumber: showsTableReferenceField ? tableNumber.trim() || undefined : undefined,
      orderType: resolvedOrderType,
      items: cart.map((line) => ({
        productId: line.productId,
        quantity: line.quantity,
      })),
      ...(resolvedOrderType === OrderTypeConst.Service
        ? { serviceDetail: intake.toServiceDetail() }
        : {}),
    };

    try {
      setSubmitting(true);
      const { data: order } = await ordersApi.create(payload);
      toast.success(`Orden ${order.orderNumber} registrada.`);
      const stockWarning = stockWarningMessage(order.stockWarnings);
      if (stockWarning) toast.warning(stockWarning, { title: STOCK_WARNING_TITLE });
      navigate('/orders');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo generar la orden.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <UploadLoadingModal
        show={intake.uploading}
        title="Subiendo imágenes"
        description={intake.uploadingMessage}
      />
      <PageTitle title="Orden detallada" parent={{ label: 'Vender', to: '/sell' }} />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show">
          {error}
          <button type="button" className="btn-close" onClick={() => setError('')}></button>
        </div>
      )}
      {truncationNotice && (
        <div className="alert alert-warning" role="status">
          {truncationNotice}
        </div>
      )}

      <div className="row">
        <div className="col-xl-7">
          <CartCatalog
            orderType={orderType}
            search={productSearch}
            onSearchChange={setProductSearch}
            loading={loading}
            products={filteredProducts}
            onAdd={addToCart}
          />
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

              <div className="mb-3">
                <div className="form-label d-block">Filtro del catálogo</div>
                <div className="order-type-toggle d-flex flex-column flex-sm-row gap-2" role="group" aria-label="Tipo de orden">
                  <button
                    type="button"
                    className={`btn flex-fill ${orderType === OrderTypeConst.Retail ? 'btn-primary' : 'btn-light'}`}
                    onClick={() => setOrderType(OrderTypeConst.Retail)}
                  >
                    Venta de productos
                  </button>
                  <button
                    type="button"
                    className={`btn flex-fill ${orderType === OrderTypeConst.Service ? 'btn-primary' : 'btn-light'}`}
                    onClick={() => setOrderType(OrderTypeConst.Service)}
                  >
                    Servicio
                  </button>
                </div>
                {isMixedCart && (
                  <div className="form-text">
                    Tienes productos y servicios en el carrito. Se habilitara checklist para los servicios rastreables.
                  </div>
                )}
              </div>

              <div className="mb-2">
                <label htmlFor="orderscart-cliente" className="form-label">Cliente</label>
                <select id="orderscart-cliente"
                  className="form-select"
                  value={selectedCustomerId}
                  onChange={(e) => setSelectedCustomerId(e.target.value)}
                >
                  {customers.map((customer) => (
                    <option key={customer.id} value={customer.id}>
                      {customer.isPublicGeneral ? 'Público en general' : customer.name}
                    </option>
                  ))}
                </select>
              </div>

              {selectedCustomer?.isPublicGeneral && (
                <>
                  <div className="mb-2">
                    <label htmlFor="orderscart-nombre-para-la-orden" className="form-label">Nombre para la orden (opcional)</label>
                    <input id="orderscart-nombre-para-la-orden"
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
                  <label className="form-label" htmlFor="order-customer-phone">
                    {phoneIsOptional ? 'Teléfono (opcional)' : 'Teléfono del cliente *'}
                  </label>
                  <input
                    id="order-customer-phone"
                    type="tel"
                    inputMode="tel"
                    autoComplete="tel"
                    className={`form-control ${phoneFormatError ? 'is-invalid' : ''}`}
                    value={customerPhoneOverride}
                    onChange={(e) => setCustomerPhoneOverride(e.target.value)}
                    placeholder="Ejemplo: 55 1234 5678"
                    aria-describedby="order-customer-phone-help"
                  />
                  {phoneFormatError ? (
                    <div className="invalid-feedback">{phoneFormatError}</div>
                  ) : (
                    <div id="order-customer-phone-help" className="form-text">
                      {phoneIsOptional
                        ? 'Sin teléfono la venta queda como público en general y no se envían avisos por WhatsApp.'
                        : 'Con el teléfono la orden queda en el historial del cliente.'}
                    </div>
                  )}
                </div>
              )}

              {!selectedCustomer?.isPublicGeneral && (
                <div className="alert alert-light py-2 px-3 mb-2">
                  <div className="small text-muted mb-1">Cliente seleccionado</div>
                  <div className="fw-semibold">{selectedCustomer?.name}</div>
                  <div className="small">{selectedCustomer?.phone || 'Sin teléfono registrado'}</div>
                  {!selectedCustomer?.phone && (
                    <div className="small text-danger">Captura el teléfono para poder generar la orden.</div>
                  )}
                </div>
              )}

              {showsTableReferenceField && (
                <div className="mb-2">
                  <label htmlFor="orderscart-mesa-referencia" className="form-label">Mesa / Referencia</label>
                  <input id="orderscart-mesa-referencia"
                    type="text"
                    className="form-control"
                    value={tableNumber}
                    onChange={(e) => setTableNumber(e.target.value)}
                  />
                </div>
              )}

              {resolvedOrderType === OrderTypeConst.Service && (
                <ServiceIntakeForm
                  intake={intake}
                  serviceCatalog={serviceCatalogItems}
                  firstServiceLine={serviceCartLines[0]}
                />
              )}

              <div className="mb-0">
                <label htmlFor="orderscart-notas" className="form-label">Notas</label>
                <textarea id="orderscart-notas"
                  className="form-control"
                  rows={3}
                  value={customerNotes}
                  onChange={(e) => setCustomerNotes(e.target.value)}
                />
              </div>
            </div>
          </div>

          <CartSummary
            cart={cart}
            totals={totals}
            pricesIncludeTax={pricesIncludeTax}
            submitting={submitting}
            onQuantityChange={updateQuantity}
            onRemove={removeLine}
            onSubmit={handleCreateOrder}
          />
        </div>
      </div>
    </>
  );
}
