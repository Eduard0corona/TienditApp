import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Modal } from 'react-bootstrap';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import CustomerCardModal from '../../components/customers/CustomerCardModal';
import { customersApi } from '../../api/customersApi';
import type { CreateCustomerRequest, CustomerDto, PaginatedList } from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Teléfono' },
  { header: 'Email' },
  { header: 'Tipo', className: 'text-center' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

export default function CustomersList() {
  const { confirm } = useConfirm();
  const [searchParams] = useSearchParams();
  // A customer opened from the global search (Ctrl K) arrives with its name or phone.
  const initialSearch = searchParams.get('search')?.trim() ?? '';
  const [search, setSearch] = useState(initialSearch);
  const [data, setData] = useState<PaginatedList<CustomerDto>>({
    items: [],
    pageNumber: 1,
    totalPages: 0,
    totalCount: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [editingCustomer, setEditingCustomer] = useState<CustomerDto | null>(null);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [filterActive, setFilterActive] = useState('');
  const [cardCustomer, setCardCustomer] = useState<CustomerDto | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateCustomerRequest>({
    defaultValues: {
      name: '',
      phone: '',
      email: '',
      notes: '',
    },
  });

  const loadCustomers = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const params: {
        isActive?: boolean;
        search?: string;
        pageNumber: number;
        pageSize: number;
      } = {
        pageNumber,
        pageSize,
      };

      if (filterActive !== '') params.isActive = filterActive === 'true';
      if (search) params.search = search;

      const { data: response } = await customersApi.getAll(params);
      setData(response);
    } catch {
      setError('Error al cargar clientes.');
    } finally {
      setLoading(false);
    }
  }, [filterActive, pageNumber, pageSize, search]);

  useEffect(() => {
    loadCustomers();
  }, [loadCustomers]);

  const openCreateModal = () => {
    setEditingCustomer(null);
    reset({ name: '', phone: '', email: '', notes: '' });
    setShowModal(true);
  };

  const openEditModal = (customer: CustomerDto) => {
    setEditingCustomer(customer);
    reset({
      name: customer.name,
      phone: customer.phone || '',
      email: customer.email || '',
      notes: customer.notes || '',
    });
    setShowModal(true);
  };

  const closeModal = () => {
    setShowModal(false);
    setEditingCustomer(null);
  };

  const onSubmit = async (form: CreateCustomerRequest) => {
    const phone = form.phone.trim();
    if (!phone) {
      setError('El teléfono es obligatorio.');
      return;
    }

    try {
      if (editingCustomer) {
        await customersApi.update(editingCustomer.id, {
          id: editingCustomer.id,
          name: form.name,
          phone,
          email: form.email,
          notes: form.notes ?? '',
        });
      } else {
        await customersApi.create({
          ...form,
          phone,
        });
      }

      closeModal();
      await loadCustomers();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'Error al guardar cliente.');
      setError(msg);
    }
  };

  const toggleActive = async (customer: CustomerDto) => {
    try {
      await customersApi.setActive(customer.id, !customer.isActive);
      await loadCustomers();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo actualizar el estado.');
      setError(msg);
    }
  };

  const handleDelete = async (customer: CustomerDto) => {
    if (customer.isPublicGeneral) return;
    const accepted = await confirm({
      title: 'Eliminar cliente',
      message: `Se eliminará a ${customer.name}. Sus pedidos se conservan.`,
      confirmLabel: 'Eliminar',
      danger: true,
    });
    if (!accepted) return;

    try {
      await customersApi.delete(customer.id);
      await loadCustomers();
    } catch (err: unknown) {
      const msg = getApiErrorMessage(err, 'No se pudo eliminar el cliente.');
      setError(msg);
    }
  };

  return (
    <>
      <PageTitle title="Clientes" subtitle="Operación" />

      <div className="row">
        <div className="col-12">
          <div className="card">
            <div className="card-body">
              <div className="row mb-2">
                <div className="col-sm-4">
                  <button className="btn btn-primary mb-2" onClick={openCreateModal}>
                    <i className="ri-add-line me-1"></i> Nuevo Cliente
                  </button>
                </div>
                <div className="col-sm-8">
                  <div className="d-flex justify-content-sm-end">
                    <select
                      className="form-select form-select-sm"
                      style={{ width: 'auto' }}
                      value={filterActive}
                      onChange={(e) => {
                        setFilterActive(e.target.value);
                        setPageNumber(1);
                      }}
                    >
                      <option value="">Todos</option>
                      <option value="true">Activos</option>
                      <option value="false">Inactivos</option>
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
                keyExtractor={(customer) => customer.id}
                loading={loading}
                emptyMessage="No hay clientes registrados."
                searchPlaceholder="Nombre, teléfono o correo"
                initialSearch={initialSearch}
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
                renderRow={(customer) => (
                  <>
                    <td className="fw-semibold">{customer.name}</td>
                    <td>{customer.phone || '-'}</td>
                    <td>{customer.email || '-'}</td>
                    <td className="text-center">
                      {customer.isPublicGeneral ? (
                        <span className="badge bg-info">Público general</span>
                      ) : (
                        <span className="badge bg-secondary">Registrado</span>
                      )}
                    </td>
                    <td className="text-center">
                      <span className={`badge ${customer.isActive ? 'bg-success' : 'bg-danger'}`}>
                        {customer.isActive ? 'Activo' : 'Inactivo'}
                      </span>
                    </td>
                    <td className="text-center">
                      <button
                        className="btn btn-sm btn-soft-info me-1"
                        title="Ficha: pedidos, saldo y citas"
                        aria-label={`Ficha de ${customer.name}`}
                        onClick={() => setCardCustomer(customer)}
                        disabled={customer.isPublicGeneral}
                      >
                        <i className="ri-profile-line"></i>
                      </button>
                      <button
                        className="btn btn-sm btn-soft-secondary me-1"
                        title={customer.isActive ? 'Desactivar' : 'Activar'}
                        onClick={() => toggleActive(customer)}
                        disabled={customer.isPublicGeneral}
                      >
                        <i className={customer.isActive ? 'ri-pause-line' : 'ri-play-line'}></i>
                      </button>
                      <button
                        className="btn btn-sm btn-soft-primary me-1"
                        title="Editar"
                        onClick={() => openEditModal(customer)}
                        disabled={customer.isPublicGeneral}
                      >
                        <i className="ri-pencil-line"></i>
                      </button>
                      <button
                        className="btn btn-sm btn-soft-danger"
                        title="Eliminar"
                        onClick={() => handleDelete(customer)}
                        disabled={customer.isPublicGeneral}
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
          <Modal.Title>{editingCustomer ? 'Editar Cliente' : 'Nuevo Cliente'}</Modal.Title>
        </Modal.Header>
        <form onSubmit={handleSubmit(onSubmit)}>
          <Modal.Body>
            <div className="mb-3">
              <label htmlFor="customerslist-nombre" className="form-label">Nombre *</label>
              <input id="customerslist-nombre"
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
              <label htmlFor="customerslist-telefono" className="form-label">Teléfono *</label>
              <input id="customerslist-telefono"
                type="text"
                className={`form-control ${errors.phone ? 'is-invalid' : ''}`}
                placeholder="55 1234 5678"
                {...register('phone', {
                  required: 'El teléfono es requerido',
                  maxLength: { value: 20, message: 'Máximo 20 caracteres' },
                })}
              />
              {errors.phone && <div className="invalid-feedback">{errors.phone.message}</div>}
              <div className="form-text">A 10 dígitos; se guarda con lada +52 para encontrarlo igual en el chat y en los pedidos.</div>
            </div>

            <div className="mb-0">
              <label htmlFor="customerslist-email" className="form-label">Email</label>
              <input id="customerslist-email"
                type="email"
                className={`form-control ${errors.email ? 'is-invalid' : ''}`}
                {...register('email', {
                  maxLength: { value: 200, message: 'Máximo 200 caracteres' },
                })}
              />
              {errors.email && <div className="invalid-feedback">{errors.email.message}</div>}
            </div>

            <div className="mb-0 mt-3">
              <label className="form-label" htmlFor="customer-form-notes">Notas</label>
              <textarea
                id="customer-form-notes"
                className={`form-control ${errors.notes ? 'is-invalid' : ''}`}
                rows={2}
                placeholder="Preferencias, alergias, cómo paga…"
                {...register('notes', {
                  maxLength: { value: 2000, message: 'Máximo 2,000 caracteres' },
                })}
              />
              {errors.notes && <div className="invalid-feedback">{errors.notes.message}</div>}
            </div>
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={closeModal}>
              Cancelar
            </button>
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando...' : editingCustomer ? 'Guardar Cambios' : 'Crear Cliente'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>

      {cardCustomer && (
        <CustomerCardModal customerId={cardCustomer.id} name={cardCustomer.name} onClose={() => setCardCustomer(null)} />
      )}
    </>
  );
}
