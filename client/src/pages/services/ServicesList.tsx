import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { productsApi } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import type {
  CategoryDto,
  CreateProductRequest,
  PaginatedList,
  ProductDto,
  UnitType,
} from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import { useTenant } from '../../hooks/useTenant';
import { formatMoney } from '../../utils/money';
import { fromVatOption, getVatOptions, toVatOption, type VatOption } from '../../utils/vatOptions';
import { getDefaultSatUnitCode } from '../../utils/units';
import SatFiscalFields from '../../components/catalog/SatFiscalFields';
import { satProductCodeRules, satUnitCodeRules, toSatCodePayload } from '../../utils/satCodes';
import { CfdiTaxObject } from '../../types';
import { useConfirm } from '../../hooks/useConfirm';

interface ServiceFormData {
  categoryId: string;
  name: string;
  description?: string;
  priceAmount: number;
  priceCurrency: string;
  unit: UnitType;
  preparationTimeMinutes?: number;
  availableFromTime?: string;
  availableToTime?: string;
  sortOrder: number;
  isTrackable: boolean;
  notifyCustomer: boolean;
  vatOption: VatOption;
  satProductCode: string;
  satUnitCode: string;
  taxObject: CfdiTaxObject;
}

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Categoría' },
  { header: 'Precio', className: 'text-end' },
  { header: 'Unidad', className: 'text-center' },
  { header: 'Horario', className: 'text-center' },
  { header: 'Duración (min)', className: 'text-center' },
  { header: 'Rastreable', className: 'text-center' },
  { header: 'Notificar', className: 'text-center' },
  { header: 'Disponible', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

const UNIT_LABELS: Record<number, string> = {
  50: 'Hora',
  51: 'Sesión',
  52: 'Servicio',
};

const UNIT_OPTIONS: Array<{ value: UnitType; label: string }> = [
  { value: 52, label: 'Servicio' },
  { value: 50, label: 'Hora' },
  { value: 51, label: 'Sesión' },
];

export default function ServicesList() {
  const titleId = useId();
  const { tenant } = useTenant();
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
  const [showModal, setShowModal] = useState(false);
  const [editingService, setEditingService] = useState<ProductDto | null>(null);
  const [filterCategory, setFilterCategory] = useState('');
  const [filterAvailability, setFilterAvailability] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<ServiceFormData>({
    defaultValues: {
      categoryId: '',
      name: '',
      description: '',
      priceAmount: 0,
      priceCurrency: 'MXN',
      unit: 52,
      preparationTimeMinutes: undefined,
      availableFromTime: undefined,
      availableToTime: undefined,
      sortOrder: 0,
      isTrackable: false,
      notifyCustomer: false,
    },
  });

  const categoryMap = useMemo(
    () => new Map(categories.map((category) => [category.id, category.name])),
    [categories],
  );
  const isTrackableSelected = watch('isTrackable');

  useEffect(() => {
    if (!isTrackableSelected) setValue('notifyCustomer', false);
  }, [isTrackableSelected, setValue]);

  const loadCategories = useCallback(async () => {
    try {
      const { data: categoriesData } = await categoriesApi.getAll();
      setCategories(categoriesData);
    } catch {
      // non-critical
    }
  }, []);

  const loadServices = useCallback(async () => {
    setLoading(true);
    setError('');

    try {
      const params: Record<string, unknown> = {
        itemType: 'Service',
        search: search || undefined,
        pageNumber,
        pageSize,
      };

      if (filterCategory) params.categoryId = filterCategory;
      if (filterAvailability) params.isAvailable = filterAvailability === 'true';

      const { data: servicesData } = await productsApi.getAll(
        params as Parameters<typeof productsApi.getAll>[0],
      );
      setData(servicesData);
    } catch {
      setError('Error al cargar los servicios.');
    } finally {
      setLoading(false);
    }
  }, [filterAvailability, filterCategory, pageNumber, pageSize, search]);

  useEffect(() => {
    loadCategories();
  }, [loadCategories]);

  useEffect(() => {
    loadServices();
  }, [loadServices]);

  const openCreateModal = () => {
    setEditingService(null);
    reset({
      categoryId: '',
      name: '',
      description: '',
      priceAmount: 0,
      priceCurrency: 'MXN',
      unit: 52,
      preparationTimeMinutes: undefined,
      availableFromTime: undefined,
      availableToTime: undefined,
      sortOrder: 0,
      isTrackable: false,
      notifyCustomer: false,
      vatOption: '',
      satProductCode: '',
      satUnitCode: '',
      taxObject: CfdiTaxObject.Subject,
    });
    setShowModal(true);
  };

  const openEditModal = (service: ProductDto) => {
    setEditingService(service);
    reset({
      categoryId: service.categoryId,
      name: service.name,
      description: service.description || '',
      priceAmount: service.priceAmount,
      priceCurrency: service.priceCurrency,
      unit: (service.unit ?? 52) as UnitType,
      preparationTimeMinutes: service.preparationTimeMinutes ?? undefined,
      availableFromTime: service.availableFromTime ? service.availableFromTime.slice(0, 5) : undefined,
      availableToTime: service.availableToTime ? service.availableToTime.slice(0, 5) : undefined,
      sortOrder: service.sortOrder,
      isTrackable: service.isTrackable ?? false,
      notifyCustomer: service.notifyCustomer ?? false,
      vatOption: toVatOption(service),
      satProductCode: service.satProductCode ?? '',
      satUnitCode: service.satUnitCode ?? '',
      taxObject: service.taxObject ?? CfdiTaxObject.Subject,
    });
    setShowModal(true);
  };

  const closeModal = () => {
    setShowModal(false);
    setEditingService(null);
  };

  const onSubmit = async (form: ServiceFormData) => {
    setError('');

    const payload: CreateProductRequest = {
      categoryId: form.categoryId,
      name: form.name.trim(),
      description: form.description?.trim() || undefined,
      priceAmount: Number(form.priceAmount),
      // Every amount is charged and shown in pesos.
      priceCurrency: 'MXN',
      sortOrder: Number(form.sortOrder || 0),
      isFeatured: false,
      unit: Number(form.unit) as UnitType,
      preparationTimeMinutes: form.preparationTimeMinutes
        ? Number(form.preparationTimeMinutes)
        : undefined,
      availableFromTime: form.availableFromTime ? `${form.availableFromTime}:00` : undefined,
      availableToTime: form.availableToTime ? `${form.availableToTime}:00` : undefined,
      isTrackable: form.isTrackable,
      notifyCustomer: form.notifyCustomer,
      // Sent every time: an update without them would reset the rate and the CFDI codes.
      ...fromVatOption(form.vatOption),
      satProductCode: toSatCodePayload(form.satProductCode),
      satUnitCode: toSatCodePayload(form.satUnitCode),
      taxObject: Number(form.taxObject) as CfdiTaxObject,
    };

    try {
      if (editingService) {
        await productsApi.update(editingService.id, { ...payload, id: editingService.id }, 'Service');
      } else {
        await productsApi.create(payload, 'Service');
      }

      closeModal();
      await loadServices();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al guardar el servicio.');
      setError(msg);
    }
  };

  const handleDelete = async (id: string) => {
    const service = data.items.find((item) => item.id === id);
    const accepted = await confirm({
      title: 'Eliminar servicio',
      message: `Se eliminará ${service ? `“${service.name}”` : 'el servicio'} del catálogo.`,
      confirmLabel: 'Eliminar',
      danger: true,
    });
    if (!accepted) return;

    try {
      await productsApi.delete(id);
      await loadServices();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al eliminar el servicio.');
      setError(msg);
    }
  };

  const handleToggleAvailability = async (service: ProductDto) => {
    try {
      await productsApi.toggleAvailability(service.id, !service.isAvailable);
      await loadServices();
    } catch {
      setError('Error al actualizar la disponibilidad.');
    }
  };

  return (
    <>
      <PageTitle title="Servicios" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              {error && (
                <div className="alert alert-danger alert-dismissible fade show">
                  {error}
                  <button type="button" className="btn-close" onClick={() => setError('')}></button>
                </div>
              )}

              <DataTable
                data={data.items}
                columns={COLUMNS}
                keyExtractor={(service) => service.id}
                loading={loading}
                emptyMessage="No hay servicios registrados."
                searchPlaceholder="Nombre del servicio"
                filters={(
                  <>
                    <DataTableFilter label="Categoría" htmlFor="services-category">
                      <select
                        id="services-category"
                        className="form-select form-select-sm"
                        value={filterCategory}
                        onChange={(e) => {
                          setFilterCategory(e.target.value);
                          setPageNumber(1);
                        }}
                      >
                        <option value="">Todas</option>
                        {categories.map((category) => (
                          <option key={category.id} value={category.id}>
                            {category.name}
                          </option>
                        ))}
                      </select>
                    </DataTableFilter>
                    <DataTableFilter label="Disponibilidad" htmlFor="services-availability">
                      <select
                        id="services-availability"
                        className="form-select form-select-sm"
                        value={filterAvailability}
                        onChange={(e) => {
                          setFilterAvailability(e.target.value);
                          setPageNumber(1);
                        }}
                      >
                        <option value="">Todos</option>
                        <option value="true">Disponible</option>
                        <option value="false">No disponible</option>
                      </select>
                    </DataTableFilter>
                  </>
                )}
                toolbarEnd={(
                  <button type="button" className="btn btn-primary btn-sm" onClick={openCreateModal}>
                    <i className="ri-add-line me-1" aria-hidden="true"></i>Nuevo servicio
                  </button>
                )}
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
                renderRow={(service) => (
                  <>
                    <td className="fw-semibold">{service.name}</td>
                    <td>{categoryMap.get(service.categoryId) || '-'}</td>
                    <td className="text-end">
                      {formatMoney(service.priceAmount)}
                    </td>
                    <td className="text-center">{UNIT_LABELS[service.unit ?? 52] || '-'}</td>
                    <td className="text-center">
                      {service.availableFromTime && service.availableToTime
                        ? `${service.availableFromTime.slice(0, 5)} - ${service.availableToTime.slice(0, 5)}`
                        : 'Todo el día'}
                    </td>
                    <td className="text-center">{service.preparationTimeMinutes ?? '-'}</td>
                    <td className="text-center">
                      <span className={`badge ${service.isTrackable ? 'bg-success' : 'bg-secondary'}`}>
                        {service.isTrackable ? 'Si' : 'No'}
                      </span>
                    </td>
                    <td className="text-center">
                      <span className={`badge ${service.notifyCustomer ? 'bg-info' : 'bg-secondary'}`}>
                        {service.notifyCustomer ? 'Si' : 'No'}
                      </span>
                    </td>
                    <td className="text-center">
                      <div className="form-check form-switch d-inline-block">
                        <input
                          type="checkbox"
                          className="form-check-input"
                          checked={service.isAvailable}
                          onChange={() => handleToggleAvailability(service)}
                        />
                      </div>
                    </td>
                    <td className="text-center text-nowrap">
                      <button
                        type="button"
                        className="btn btn-sm btn-light me-1"
                        aria-label={`Editar ${service.name}`}
                        onClick={() => openEditModal(service)}
                      >
                        Editar
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-light text-danger"
                        aria-label={`Eliminar ${service.name}`}
                        onClick={() => handleDelete(service.id)}
                      >
                        Eliminar
                      </button>
                    </td>
                  </>
                )}
              />
            </div>
          </div>
        </div>
      </div>

      <Modal show={showModal} onHide={closeModal} centered aria-labelledby={titleId}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId}>{editingService ? 'Editar servicio' : 'Nuevo servicio'}</Modal.Title>
        </Modal.Header>
        <form onSubmit={handleSubmit(onSubmit)}>
          <Modal.Body>
            <div className="mb-3">
              <label htmlFor="serviceslist-nombre" className="form-label">Nombre <span className="text-danger">*</span></label>
              <input id="serviceslist-nombre"
                type="text"
                className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                {...register('name', {
                  required: 'El nombre es requerido',
                  maxLength: { value: 200, message: 'Máximo 200 caracteres' },
                })}
              />
              {errors.name && <div className="invalid-feedback">{errors.name.message}</div>}
            </div>

            <div className="mb-3">
              <label htmlFor="serviceslist-categoria" className="form-label">Categoría <span className="text-danger">*</span></label>
              <select id="serviceslist-categoria"
                className={`form-select ${errors.categoryId ? 'is-invalid' : ''}`}
                {...register('categoryId', { required: 'Selecciona una categoría' })}
              >
                <option value="">Seleccionar...</option>
                {categories.map((category) => (
                  <option key={category.id} value={category.id}>
                    {category.name}
                  </option>
                ))}
              </select>
              {errors.categoryId && <div className="invalid-feedback">{errors.categoryId.message}</div>}
            </div>

            <div className="mb-3">
              <label htmlFor="serviceslist-descripcion" className="form-label">Descripción</label>
              <textarea id="serviceslist-descripcion"
                className="form-control"
                rows={3}
                {...register('description', {
                  maxLength: { value: 1000, message: 'Máximo 1000 caracteres' },
                })}
              />
            </div>

            <div className="row">
              <div className="col-md-6 mb-3">
                <label htmlFor="serviceslist-precio" className="form-label">Precio <span className="text-danger">*</span></label>
                <input id="serviceslist-precio"
                  type="number"
                  step="0.01"
                  className={`form-control ${errors.priceAmount ? 'is-invalid' : ''}`}
                  {...register('priceAmount', {
                    required: 'El precio es requerido',
                    valueAsNumber: true,
                    min: { value: 0.01, message: 'Debe ser mayor a 0' },
                  })}
                />
                {errors.priceAmount && <div className="invalid-feedback">{errors.priceAmount.message}</div>}
              </div>
              <div className="col-12 mb-3">
                <label className="form-label" htmlFor="service-vat">IVA</label>
                <select id="service-vat" className="form-select" {...register('vatOption')}>
                  {getVatOptions(tenant?.defaultTaxRate ?? 16).map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
                <div className="form-text">Algunos servicios están exentos (por ejemplo, consultas médicas).</div>
              </div>
              <div className="col-12 mb-3">
                <SatFiscalFields
                  idPrefix="service"
                  productCode={register('satProductCode', satProductCodeRules)}
                  unitCode={register('satUnitCode', satUnitCodeRules)}
                  taxObject={register('taxObject')}
                  defaultUnitCode={getDefaultSatUnitCode(Number(watch('unit')) as UnitType)}
                  errors={{ productCode: errors.satProductCode?.message, unitCode: errors.satUnitCode?.message }}
                />
              </div>
            </div>

            <div className="row">
              <div className="col-md-6 mb-3">
                <label htmlFor="serviceslist-unidad-de-servicio" className="form-label">Unidad de servicio</label>
                <select id="serviceslist-unidad-de-servicio" className="form-select" {...register('unit', { valueAsNumber: true })}>
                  {UNIT_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-md-6 mb-3">
                <label htmlFor="serviceslist-duracion-min" className="form-label">Duración (min)</label>
                <input id="serviceslist-duracion-min"
                  type="number"
                  className="form-control"
                  {...register('preparationTimeMinutes', { valueAsNumber: true })}
                />
              </div>
            </div>

            <div className="row">
              <div className="col-md-6 mb-3">
                <label htmlFor="serviceslist-disponible-desde" className="form-label">Disponible desde</label>
                <input id="serviceslist-disponible-desde"
                  type="time"
                  className="form-control"
                  {...register('availableFromTime')}
                />
              </div>
              <div className="col-md-6 mb-3">
                <label htmlFor="serviceslist-disponible-hasta" className="form-label">Disponible hasta</label>
                <input id="serviceslist-disponible-hasta"
                  type="time"
                  className="form-control"
                  {...register('availableToTime')}
                />
              </div>
            </div>

            <div className="mb-0">
              <label htmlFor="serviceslist-orden" className="form-label">Orden</label>
              <input id="serviceslist-orden"
                type="number"
                className="form-control"
                {...register('sortOrder', { valueAsNumber: true })}
              />
            </div>

            <div className="mt-3">
              <div className="form-check mb-2">
                <input
                  id="service-is-trackable"
                  type="checkbox"
                  className="form-check-input"
                  {...register('isTrackable')}
                />
                <label htmlFor="service-is-trackable" className="form-check-label">
                  Rastreable
                </label>
              </div>
              <div className="form-check">
                <input
                  id="service-notify-customer"
                  type="checkbox"
                  className="form-check-input"
                  disabled={!isTrackableSelected}
                  {...register('notifyCustomer')}
                />
                <label htmlFor="service-notify-customer" className="form-check-label">
                  Notificar al cliente
                </label>
              </div>
            </div>
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={closeModal}>
              Cancelar
            </button>
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando...' : editingService ? 'Guardar cambios' : 'Crear servicio'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>
    </>
  );
}
