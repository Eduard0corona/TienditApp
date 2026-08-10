import { useCallback, useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Modal } from 'react-bootstrap';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { productsApi } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import type {
  CategoryDto,
  CreateProductRequest,
  PaginatedList,
  ProductDto,
  UnitType,
} from '../../types';

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
}

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Categoria' },
  { header: 'Precio', className: 'text-end' },
  { header: 'Unidad', className: 'text-center' },
  { header: 'Horario', className: 'text-center' },
  { header: 'Duracion (min)', className: 'text-center' },
  { header: 'Rastreable', className: 'text-center' },
  { header: 'Notificar', className: 'text-center' },
  { header: 'Disponible', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

const UNIT_LABELS: Record<number, string> = {
  50: 'Hora',
  51: 'Sesion',
  52: 'Servicio',
};

const UNIT_OPTIONS: Array<{ value: UnitType; label: string }> = [
  { value: 52, label: 'Servicio' },
  { value: 50, label: 'Hora' },
  { value: 51, label: 'Sesion' },
];

export default function ServicesList() {
  const [data, setData] = useState<PaginatedList<ProductDto>>({
    items: [],
    pageNumber: 1,
    totalPages: 0,
    totalCount: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [categories, setCategories] = useState<CategoryDto[]>([]);
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
  }, [filterAvailability, filterCategory, pageNumber, pageSize]);

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
      priceCurrency: form.priceCurrency,
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
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al guardar el servicio.';
      setError(msg);
    }
  };

  const handleDelete = async (id: string) => {
    if (!window.confirm('Deseas eliminar este servicio?')) return;

    try {
      await productsApi.delete(id);
      await loadServices();
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        'Error al eliminar el servicio.';
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
      <PageTitle title="Servicios" subtitle="Catalogo" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              <div className="row mb-2">
                <div className="col-sm-4">
                  <button className="btn btn-primary mb-2" onClick={openCreateModal}>
                    <i className="ri-add-line me-1"></i> Nuevo Servicio
                  </button>
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
                      <option value="">Todas las categorias</option>
                      {categories.map((category) => (
                        <option key={category.id} value={category.id}>
                          {category.name}
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
                keyExtractor={(service) => service.id}
                loading={loading}
                emptyMessage="No hay servicios registrados."
                searchFilter={(service, search) =>
                  service.name.toLowerCase().includes(search) ||
                  (service.description || '').toLowerCase().includes(search)
                }
                serverSide={{
                  totalCount: data.totalCount,
                  page: pageNumber,
                  pageSize,
                  onPageChange: setPageNumber,
                  onPageSizeChange: (size) => {
                    setPageSize(size);
                    setPageNumber(1);
                  },
                }}
                renderRow={(service) => (
                  <>
                    <td className="fw-semibold">{service.name}</td>
                    <td>{categoryMap.get(service.categoryId) || '-'}</td>
                    <td className="text-end">
                      ${service.priceAmount.toFixed(2)} {service.priceCurrency}
                    </td>
                    <td className="text-center">{UNIT_LABELS[service.unit ?? 52] || '-'}</td>
                    <td className="text-center">
                      {service.availableFromTime && service.availableToTime
                        ? `${service.availableFromTime.slice(0, 5)} - ${service.availableToTime.slice(0, 5)}`
                        : 'Todo el dia'}
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
                    <td className="text-center">
                      <button
                        className="btn btn-sm btn-soft-primary me-1"
                        title="Editar"
                        onClick={() => openEditModal(service)}
                      >
                        <i className="ri-pencil-line"></i>
                      </button>
                      <button
                        className="btn btn-sm btn-soft-danger"
                        title="Eliminar"
                        onClick={() => handleDelete(service.id)}
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

      <Modal show={showModal} onHide={closeModal} centered>
        <Modal.Header closeButton>
          <Modal.Title>{editingService ? 'Editar Servicio' : 'Nuevo Servicio'}</Modal.Title>
        </Modal.Header>
        <form onSubmit={handleSubmit(onSubmit)}>
          <Modal.Body>
            <div className="mb-3">
              <label className="form-label">Nombre <span className="text-danger">*</span></label>
              <input
                type="text"
                className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                {...register('name', {
                  required: 'El nombre es requerido',
                  maxLength: { value: 200, message: 'Maximo 200 caracteres' },
                })}
              />
              {errors.name && <div className="invalid-feedback">{errors.name.message}</div>}
            </div>

            <div className="mb-3">
              <label className="form-label">Categoria <span className="text-danger">*</span></label>
              <select
                className={`form-select ${errors.categoryId ? 'is-invalid' : ''}`}
                {...register('categoryId', { required: 'Selecciona una categoria' })}
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
              <label className="form-label">Descripcion</label>
              <textarea
                className="form-control"
                rows={3}
                {...register('description', {
                  maxLength: { value: 1000, message: 'Maximo 1000 caracteres' },
                })}
              />
            </div>

            <div className="row">
              <div className="col-md-6 mb-3">
                <label className="form-label">Precio <span className="text-danger">*</span></label>
                <input
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
              <div className="col-md-6 mb-3">
                <label className="form-label">Moneda</label>
                <select className="form-select" {...register('priceCurrency')}>
                  <option value="MXN">MXN</option>
                  <option value="USD">USD</option>
                </select>
              </div>
            </div>

            <div className="row">
              <div className="col-md-6 mb-3">
                <label className="form-label">Unidad de servicio</label>
                <select className="form-select" {...register('unit', { valueAsNumber: true })}>
                  {UNIT_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-md-6 mb-3">
                <label className="form-label">Duracion (min)</label>
                <input
                  type="number"
                  className="form-control"
                  {...register('preparationTimeMinutes', { valueAsNumber: true })}
                />
              </div>
            </div>

            <div className="row">
              <div className="col-md-6 mb-3">
                <label className="form-label">Disponible desde</label>
                <input
                  type="time"
                  className="form-control"
                  {...register('availableFromTime')}
                />
              </div>
              <div className="col-md-6 mb-3">
                <label className="form-label">Disponible hasta</label>
                <input
                  type="time"
                  className="form-control"
                  {...register('availableToTime')}
                />
              </div>
            </div>

            <div className="mb-0">
              <label className="form-label">Orden</label>
              <input
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
              {isSubmitting ? 'Guardando...' : editingService ? 'Guardar Cambios' : 'Crear Servicio'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>
    </>
  );
}
