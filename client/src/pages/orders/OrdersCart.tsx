import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import UploadLoadingModal from '../../components/common/UploadLoadingModal';
import { productsApi } from '../../api/productsApi';
import { ordersApi } from '../../api/ordersApi';
import { customersApi } from '../../api/customersApi';
import { useTenant } from '../../hooks/useTenant';
import './OrdersCart.css';
import type {
  CreateOrderRequest,
  CreateServiceOrderPieceRequest,
  CustomerDto,
  OrderType,
  ProductDto,
  UploadedFileDto,
} from '../../types';
import { BusinessType as BusinessTypeConst, OrderType as OrderTypeConst, UnitType } from '../../types';

interface CartLine {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  isService: boolean;
}

interface ServiceChecklistItem {
  id: string;
  label: string;
  checked: boolean;
}

interface ServicePhotoItem {
  id: string;
  url: string;
  fileName: string;
}

function toLocalDateTimeInput(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  const hours = String(date.getHours()).padStart(2, '0');
  const minutes = String(date.getMinutes()).padStart(2, '0');
  return `${year}-${month}-${day}T${hours}:${minutes}`;
}

function buildChecklistItem(label = ''): ServiceChecklistItem {
  return {
    id: crypto.randomUUID(),
    label,
    checked: false,
  };
}

function buildServicePiece(): CreateServiceOrderPieceRequest {
  return {
    itemName: '',
    requestedService: '',
    isTrackable: true,
    notifyCustomer: false,
  };
}

function buildPhoto(uploaded: UploadedFileDto): ServicePhotoItem {
  return {
    id: crypto.randomUUID(),
    url: uploaded.url,
    fileName: uploaded.fileName,
  };
}

export default function OrdersCart() {
  const navigate = useNavigate();
  const { tenant } = useTenant();
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
  const [serviceReceivedAt, setServiceReceivedAt] = useState(() => toLocalDateTimeInput(new Date()));
  const [servicePromisedAt, setServicePromisedAt] = useState('');
  const [servicePriority, setServicePriority] = useState(1);
  const [serviceIntakeNotes, setServiceIntakeNotes] = useState('');
  const [serviceDeliveryNotes, setServiceDeliveryNotes] = useState('');
  const [serviceChecklist, setServiceChecklist] = useState<ServiceChecklistItem[]>([
    buildChecklistItem(''),
  ]);
  const [beforePhotos, setBeforePhotos] = useState<ServicePhotoItem[]>([]);
  const [afterPhotos, setAfterPhotos] = useState<ServicePhotoItem[]>([]);
  const [uploadingPhotos, setUploadingPhotos] = useState(false);
  const [uploadingMessage, setUploadingMessage] = useState('Subiendo evidencia de servicio...');
  const [servicePieces, setServicePieces] = useState<CreateServiceOrderPieceRequest[]>([
    buildServicePiece(),
  ]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const showsTableReferenceField = tenant?.businessType === BusinessTypeConst.Food;

  const isService = (product: ProductDto) =>
    product.unit === UnitType.Hour ||
    product.unit === UnitType.Session ||
    product.unit === UnitType.Service;

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

  useEffect(() => {
    setError('');
  }, [orderType]);

  const serviceCatalogItems = useMemo(
    () => products.filter((product) => isService(product)),
    [products],
  );

  const serviceCartLines = useMemo(
    () => cart.filter((line) => line.isService),
    [cart],
  );

  const updateServicePiece = (
    index: number,
    changes: Partial<CreateServiceOrderPieceRequest>,
  ) => {
    setServicePieces((current) =>
      current.map((piece, currentIndex) =>
        currentIndex === index ? { ...piece, ...changes } : piece,
      ),
    );
  };

  const addServicePiece = () => {
    const firstServiceLine = serviceCartLines[0];
    setServicePieces((current) => [
      ...current,
      {
        ...buildServicePiece(),
        requestedService: firstServiceLine?.productName ?? '',
        serviceProductId: firstServiceLine?.productId,
      },
    ]);
  };

  const removeServicePiece = (index: number) => {
    setServicePieces((current) =>
      current.length <= 1 ? current : current.filter((_, currentIndex) => currentIndex !== index),
    );
  };

  const updateChecklistItem = (id: string, changes: Partial<ServiceChecklistItem>) => {
    setServiceChecklist((current) =>
      current.map((item) => (item.id === id ? { ...item, ...changes } : item)),
    );
  };

  const addChecklistItem = () => {
    setServiceChecklist((current) => [...current, buildChecklistItem('')]);
  };

  const removeChecklistItem = (id: string) => {
    setServiceChecklist((current) =>
      current.length <= 1 ? current : current.filter((item) => item.id !== id),
    );
  };

  const removePhoto = (type: 'before' | 'after', id: string) => {
    if (type === 'before') {
      setBeforePhotos((current) => current.filter((photo) => photo.id !== id));
      return;
    }

    setAfterPhotos((current) => current.filter((photo) => photo.id !== id));
  };

  const uploadServicePhotos = async (files: FileList | null, type: 'before' | 'after') => {
    if (!files || files.length === 0) return;

    setError('');
    setUploadingPhotos(true);
    setUploadingMessage(type === 'before'
      ? 'Subiendo fotos de recepcion...'
      : 'Subiendo fotos de entrega...');

    try {
      const uploads: ServicePhotoItem[] = [];
      for (const file of Array.from(files)) {
        const { data } = await ordersApi.uploadServicePhoto(file);
        uploads.push(buildPhoto(data));
      }

      if (type === 'before') {
        setBeforePhotos((current) => [...current, ...uploads]);
      } else {
        setAfterPhotos((current) => [...current, ...uploads]);
      }
    } catch {
      setError('No se pudieron subir una o mas imagenes. Intenta nuevamente.');
    } finally {
      setUploadingPhotos(false);
    }
  };

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
          isService: isService(product),
        },
      ];
    });
  };

  const onProductCardKeyDown = (event: React.KeyboardEvent<HTMLDivElement>, product: ProductDto) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      addToCart(product);
    }
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
  const hasServiceItems = useMemo(() => cart.some((line) => line.isService), [cart]);
  const hasProductItems = useMemo(() => cart.some((line) => !line.isService), [cart]);
  const resolvedOrderType = useMemo(
    () => (hasServiceItems ? OrderTypeConst.Service : OrderTypeConst.Retail),
    [hasServiceItems],
  );
  const isMixedCart = hasServiceItems && hasProductItems;

  const filteredProducts = useMemo(() => {
    const search = productSearch.trim().toLowerCase();
    return products.filter((product) => {
      const matchesType = orderType === OrderTypeConst.Service
        ? isService(product)
        : !isService(product);

      if (!matchesType) return false;
      if (!search) return true;

      return product.name.toLowerCase().includes(search) ||
        (product.description || '').toLowerCase().includes(search);
    });
  }, [orderType, productSearch, products]);

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

    const cleanedServicePieces = servicePieces
      .map((piece) => ({
        ...piece,
        itemName: piece.itemName.trim(),
        serviceProductId: piece.serviceProductId || undefined,
        isTrackable: piece.isTrackable ?? true,
        notifyCustomer: piece.notifyCustomer ?? false,
        brand: piece.brand?.trim() || undefined,
        model: piece.model?.trim() || undefined,
        color: piece.color?.trim() || undefined,
        size: piece.size?.trim() || undefined,
        material: piece.material?.trim() || undefined,
        conditionNotes: piece.conditionNotes?.trim() || undefined,
        requestedService: piece.requestedService?.trim() || undefined,
        internalNotes: piece.internalNotes?.trim() || undefined,
      }))
      .filter((piece) => piece.itemName);

    const cleanedChecklist = serviceChecklist
      .map((item) => ({
        label: item.label.trim(),
        checked: item.checked,
      }))
      .filter((item) => item.label.length > 0);

    const payload: CreateOrderRequest = {
      customerId: selectedCustomerId || undefined,
      customerName: customerNameOverride.trim() || undefined,
      customerPhone: effectiveCustomerPhone,
      customerNotes: customerNotes.trim() || undefined,
      tableNumber: showsTableReferenceField ? tableNumber.trim() || undefined : undefined,
      orderType: resolvedOrderType,
      items: cart.map((line) => ({
        productId: line.productId,
        quantity: line.quantity,
      })),
      ...(resolvedOrderType === OrderTypeConst.Service
        ? {
            serviceDetail: {
              receivedAt: serviceReceivedAt ? new Date(serviceReceivedAt).toISOString() : undefined,
              promisedAt: servicePromisedAt ? new Date(servicePromisedAt).toISOString() : undefined,
              priority: servicePriority,
              intakeNotes: serviceIntakeNotes.trim() || undefined,
              deliveryNotes: serviceDeliveryNotes.trim() || undefined,
              intakeChecklistJson: cleanedChecklist.length > 0 ? JSON.stringify(cleanedChecklist) : undefined,
              beforePhotosJson: beforePhotos.length > 0 ? JSON.stringify(beforePhotos) : undefined,
              afterPhotosJson: afterPhotos.length > 0 ? JSON.stringify(afterPhotos) : undefined,
              pieces: cleanedServicePieces,
            },
          }
        : {}),
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
      <UploadLoadingModal
        show={uploadingPhotos}
        title="Subiendo imagenes"
        description={uploadingMessage}
      />
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
              <div className="orders-catalog-toolbar d-flex justify-content-between align-items-center mb-3 gap-2">
                <h4 className="header-title mb-0">
                  {orderType === OrderTypeConst.Service ? 'Servicios disponibles' : 'Productos disponibles'}
                </h4>
                <div className="orders-catalog-search">
                  <input
                    type="text"
                    className="form-control form-control-sm"
                    placeholder="Buscar item..."
                    value={productSearch}
                    onChange={(e) => setProductSearch(e.target.value)}
                  />
                </div>
              </div>

              {loading ? (
                <div className="text-center py-4">
                  <div className="spinner-border text-primary" role="status"></div>
                </div>
              ) : (
                <div className="row g-2">
                  {filteredProducts.map((product) => (
                    <div className="col-md-6 col-lg-4" key={product.id}>
                      <div
                        role="button"
                        tabIndex={0}
                        className="order-catalog-card border rounded p-2 h-100 d-flex flex-column"
                        onClick={() => addToCart(product)}
                        onKeyDown={(event) => onProductCardKeyDown(event, product)}
                        title="Agregar al carrito"
                      >
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
                          <span className="text-muted small">Click para agregar</span>
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

              <div className="mb-3">
                <label className="form-label d-block">Filtro del catalogo</label>
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

              {showsTableReferenceField && (
                <div className="mb-2">
                  <label className="form-label">Mesa / Referencia</label>
                  <input
                    type="text"
                    className="form-control"
                    value={tableNumber}
                    onChange={(e) => setTableNumber(e.target.value)}
                  />
                </div>
              )}

              {resolvedOrderType === OrderTypeConst.Service && (
                <div className="border rounded p-2 mb-2">
                  <h5 className="font-15 mb-2">Recepcion de servicio</h5>
                  <div className="row g-2 mb-2">
                    <div className="col-md-6">
                      <label className="form-label">Fecha de recepcion</label>
                      <input
                        type="datetime-local"
                        className="form-control"
                        value={serviceReceivedAt}
                        onChange={(e) => setServiceReceivedAt(e.target.value)}
                      />
                    </div>
                    <div className="col-md-6">
                      <label className="form-label">Fecha promesa</label>
                      <input
                        type="datetime-local"
                        className="form-control"
                        value={servicePromisedAt}
                        onChange={(e) => setServicePromisedAt(e.target.value)}
                      />
                    </div>
                    <div className="col-md-6">
                      <label className="form-label">Prioridad</label>
                      <select
                        className="form-select"
                        value={servicePriority}
                        onChange={(e) => setServicePriority(Number(e.target.value))}
                      >
                        <option value={1}>Baja</option>
                        <option value={2}>Media-baja</option>
                        <option value={3}>Media</option>
                        <option value={4}>Media-alta</option>
                        <option value={5}>Alta</option>
                      </select>
                    </div>
                  </div>

                  <div className="mb-2">
                    <label className="form-label">Notas de ingreso</label>
                    <textarea
                      className="form-control"
                      rows={2}
                      value={serviceIntakeNotes}
                      onChange={(e) => setServiceIntakeNotes(e.target.value)}
                    />
                  </div>

                  <div className="mb-2">
                    <label className="form-label">Notas de entrega</label>
                    <textarea
                      className="form-control"
                      rows={2}
                      value={serviceDeliveryNotes}
                      onChange={(e) => setServiceDeliveryNotes(e.target.value)}
                    />
                  </div>

                  <div className="mb-2">
                    <div className="d-flex justify-content-between align-items-center mb-1">
                      <label className="form-label mb-0">Checklist de recepcion</label>
                      <button type="button" className="btn btn-sm btn-soft-primary" onClick={addChecklistItem}>
                        <i className="ri-add-line me-1"></i>Agregar item
                      </button>
                    </div>

                    {serviceChecklist.map((item) => (
                      <div className="d-flex align-items-center gap-2 mb-1" key={item.id}>
                        <input
                          className="form-check-input mt-0"
                          type="checkbox"
                          checked={item.checked}
                          onChange={(e) => updateChecklistItem(item.id, { checked: e.target.checked })}
                        />
                        <input
                          type="text"
                          className="form-control form-control-sm"
                          placeholder="Ejemplo: Sin manchas visibles"
                          value={item.label}
                          onChange={(e) => updateChecklistItem(item.id, { label: e.target.value })}
                        />
                        <button
                          type="button"
                          className="btn btn-sm btn-soft-danger"
                          onClick={() => removeChecklistItem(item.id)}
                          disabled={serviceChecklist.length <= 1}
                        >
                          <i className="ri-delete-bin-line"></i>
                        </button>
                      </div>
                    ))}
                  </div>

                  <div className="row g-2 mb-2">
                    <div className="col-md-6">
                      <label className="form-label">Fotos antes</label>
                      <input
                        type="file"
                        accept="image/*"
                        className="form-control"
                        multiple
                        onChange={(e) => {
                          void uploadServicePhotos(e.target.files, 'before');
                          e.currentTarget.value = '';
                        }}
                      />
                      {beforePhotos.length > 0 && (
                        <div className="orders-photo-grid mt-2">
                          {beforePhotos.map((photo) => (
                            <div className="orders-photo-item" key={photo.id}>
                              <img src={photo.url} alt={photo.fileName} className="img-fluid rounded" />
                              <button
                                type="button"
                                className="btn btn-xs btn-danger orders-photo-remove"
                                onClick={() => removePhoto('before', photo.id)}
                              >
                                <i className="ri-close-line"></i>
                              </button>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                    <div className="col-md-6">
                      <label className="form-label">Fotos despues</label>
                      <input
                        type="file"
                        accept="image/*"
                        className="form-control"
                        multiple
                        onChange={(e) => {
                          void uploadServicePhotos(e.target.files, 'after');
                          e.currentTarget.value = '';
                        }}
                      />
                      {afterPhotos.length > 0 && (
                        <div className="orders-photo-grid mt-2">
                          {afterPhotos.map((photo) => (
                            <div className="orders-photo-item" key={photo.id}>
                              <img src={photo.url} alt={photo.fileName} className="img-fluid rounded" />
                              <button
                                type="button"
                                className="btn btn-xs btn-danger orders-photo-remove"
                                onClick={() => removePhoto('after', photo.id)}
                              >
                                <i className="ri-close-line"></i>
                              </button>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>

                  <div>
                    <div className="d-flex justify-content-between align-items-center mb-2">
                      <label className="form-label mb-0">Piezas</label>
                      <button type="button" className="btn btn-sm btn-soft-primary" onClick={addServicePiece}>
                        <i className="ri-add-line me-1"></i>Agregar pieza
                      </button>
                    </div>

                    {servicePieces.map((piece, index) => (
                      <div key={`piece-${index}`} className="border rounded p-2 mb-2">
                        <div className="row g-2">
                          <div className="col-md-6">
                            <label className="form-label">Nombre de pieza *</label>
                            <input
                              type="text"
                              className="form-control"
                              value={piece.itemName}
                              onChange={(e) => updateServicePiece(index, { itemName: e.target.value })}
                            />
                          </div>
                          <div className="col-md-6">
                            <label className="form-label">Servicio solicitado</label>
                            <input
                              type="text"
                              className="form-control"
                              value={piece.requestedService || ''}
                              onChange={(e) => updateServicePiece(index, { requestedService: e.target.value })}
                            />
                          </div>
                          <div className="col-md-6">
                            <label className="form-label">Servicio del catalogo</label>
                            <select
                              className="form-select"
                              value={piece.serviceProductId || ''}
                              onChange={(e) =>
                                updateServicePiece(index, {
                                  serviceProductId: e.target.value || undefined,
                                })
                              }
                            >
                              <option value="">Seleccionar...</option>
                              {serviceCatalogItems.map((item) => (
                                <option key={item.id} value={item.id}>{item.name}</option>
                              ))}
                            </select>
                          </div>
                          <div className="col-md-2">
                            <label className="form-label d-block">Rastreable</label>
                            <input
                              type="checkbox"
                              className="form-check-input"
                              checked={piece.isTrackable ?? true}
                              onChange={(e) => updateServicePiece(index, { isTrackable: e.target.checked })}
                            />
                          </div>
                          <div className="col-md-2">
                            <label className="form-label d-block">Notificar</label>
                            <input
                              type="checkbox"
                              className="form-check-input"
                              checked={piece.notifyCustomer ?? false}
                              onChange={(e) => updateServicePiece(index, { notifyCustomer: e.target.checked })}
                            />
                          </div>
                          <div className="col-md-6">
                            <label className="form-label">Marca</label>
                            <input
                              type="text"
                              className="form-control"
                              value={piece.brand || ''}
                              onChange={(e) => updateServicePiece(index, { brand: e.target.value })}
                            />
                          </div>
                          <div className="col-md-6">
                            <label className="form-label">Modelo</label>
                            <input
                              type="text"
                              className="form-control"
                              value={piece.model || ''}
                              onChange={(e) => updateServicePiece(index, { model: e.target.value })}
                            />
                          </div>
                          <div className="col-md-4">
                            <label className="form-label">Color</label>
                            <input
                              type="text"
                              className="form-control"
                              value={piece.color || ''}
                              onChange={(e) => updateServicePiece(index, { color: e.target.value })}
                            />
                          </div>
                          <div className="col-md-4">
                            <label className="form-label">Talla</label>
                            <input
                              type="text"
                              className="form-control"
                              value={piece.size || ''}
                              onChange={(e) => updateServicePiece(index, { size: e.target.value })}
                            />
                          </div>
                          <div className="col-md-4">
                            <label className="form-label">Material</label>
                            <input
                              type="text"
                              className="form-control"
                              value={piece.material || ''}
                              onChange={(e) => updateServicePiece(index, { material: e.target.value })}
                            />
                          </div>
                          <div className="col-md-6">
                            <label className="form-label">Condicion</label>
                            <textarea
                              className="form-control"
                              rows={2}
                              value={piece.conditionNotes || ''}
                              onChange={(e) => updateServicePiece(index, { conditionNotes: e.target.value })}
                            />
                          </div>
                          <div className="col-md-6">
                            <label className="form-label">Nota interna</label>
                            <textarea
                              className="form-control"
                              rows={2}
                              value={piece.internalNotes || ''}
                              onChange={(e) => updateServicePiece(index, { internalNotes: e.target.value })}
                            />
                          </div>
                        </div>
                        <div className="mt-2 text-end">
                          <button
                            type="button"
                            className="btn btn-sm btn-soft-danger"
                            onClick={() => removeServicePiece(index)}
                            disabled={servicePieces.length <= 1}
                          >
                            <i className="ri-delete-bin-line"></i>
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

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
                          <th>Item</th>
                          <th className="text-center">Cant.</th>
                          <th className="text-end">Subtotal</th>
                          <th></th>
                        </tr>
                      </thead>
                      <tbody>
                        {cart.map((line) => (
                          <tr key={line.productId}>
                            <td>
                              {line.productName}
                              <span className={`badge ms-2 ${line.isService ? 'bg-info' : 'bg-secondary'}`}>
                                {line.isService ? 'Servicio' : 'Producto'}
                              </span>
                            </td>
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
