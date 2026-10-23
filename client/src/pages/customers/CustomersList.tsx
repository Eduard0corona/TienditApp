import { useCallback, useEffect, useId, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Modal } from 'react-bootstrap';
import { useForm } from 'react-hook-form';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import CustomerCardModal from '../../components/customers/CustomerCardModal';
import CreditLimitModal from '../../components/customers/CreditLimitModal';
import CustomerFiscalDataModal from '../../components/customers/CustomerFiscalDataModal';
import { describeTerm } from '../../components/customers/customerAccount';
import { customersApi } from '../../api/customersApi';
import type { CreateCustomerRequest, CustomerDto, PaginatedList } from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { formatMoney } from '../../utils/money';

const COLUMNS = [
  { header: 'Nombre' },
  { header: 'Teléfono' },
  { header: 'Correo' },
  { header: 'Tipo', className: 'text-center' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

export default function CustomersList() {
  const titleId = useId();
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
  const [creditCustomer, setCreditCustomer] = useState<CustomerDto | null>(null);
  const [fiscalCustomer, setFiscalCustomer] = useState<CustomerDto | null>(null);
  const { user } = useAuth();
  const { capabilities } = useTenant();
  // How much a customer may owe is the owner's decision (owners and admins, as the API asks).
  const canGiveCredit = getAccess(user, capabilities).isAdmin;

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting, isDirty },
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
      <PageTitle title="Clientes" />

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
                keyExtractor={(customer) => customer.id}
                loading={loading}
                emptyMessage="No hay clientes registrados."
                searchPlaceholder="Nombre, teléfono o correo"
                filters={(
                  <DataTableFilter label="Estado" htmlFor="customers-status">
                    <select
                      id="customers-status"
                      className="form-select form-select-sm"
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
                  </DataTableFilter>
                )}
                toolbarEnd={(
                  <button type="button" className="btn btn-primary btn-sm" onClick={openCreateModal}>
                    <i className="ri-add-line me-1" aria-hidden="true"></i>Nuevo cliente
                  </button>
                )}
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
                    <td>
                      <div className="fw-semibold">{customer.name}</div>
                      {customer.creditLimit != null && (
                        <span className="badge bg-warning-subtle text-warning-emphasis">Fiado hasta {formatMoney(customer.creditLimit)}</span>
                      )}
                      {customer.creditTermDays != null && (
                        <span className="badge bg-secondary-subtle text-secondary-emphasis ms-1">
                          Paga en {describeTerm(customer.creditTermDays)}
                        </span>
                      )}
                      {customer.fiscalData && (
                        <span className="badge bg-secondary-subtle text-secondary-emphasis ms-1">RFC {customer.fiscalData.rfc}</span>
                      )}
                    </td>
                    <td className="text-nowrap">{customer.phone || '-'}</td>
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
                    <td className="text-center text-nowrap">
                      <button
                        type="button"
                        className="btn btn-sm btn-light me-1"
                        title="Ficha: pedidos, saldo y citas"
                        aria-label={`Ficha de ${customer.name}`}
                        onClick={() => setCardCustomer(customer)}
                        disabled={customer.isPublicGeneral}
                      >
                        Ficha
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-light me-1"
                        aria-label={`Editar ${customer.name}`}
                        onClick={() => openEditModal(customer)}
                        disabled={customer.isPublicGeneral}
                      >
                        Editar
                      </button>
                      {canGiveCredit && (
                        <button
                          type="button"
                          className="btn btn-sm btn-light me-1"
                          aria-label={`Fiado de ${customer.name}`}
                          onClick={() => setCreditCustomer(customer)}
                          disabled={customer.isPublicGeneral || !customer.phone}
                        >
                          Fiado
                        </button>
                      )}
                      <button
                        type="button"
                        className="btn btn-sm btn-light me-1"
                        aria-label={`${customer.isActive ? 'Desactivar' : 'Activar'} ${customer.name}`}
                        onClick={() => toggleActive(customer)}
                        disabled={customer.isPublicGeneral}
                      >
                        {customer.isActive ? 'Desactivar' : 'Activar'}
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-light text-danger"
                        aria-label={`Eliminar ${customer.name}`}
                        onClick={() => handleDelete(customer)}
                        disabled={customer.isPublicGeneral}
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
          <Modal.Title id={titleId}>{editingCustomer ? 'Editar cliente' : 'Nuevo cliente'}</Modal.Title>
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
            {editingCustomer && !editingCustomer.isPublicGeneral && (
              // The fiscal data has its own dialog (Fase 28A): the invoice of the customer's orders proposes it.
              <div className="border-top pt-3 d-flex flex-wrap align-items-center gap-2">
                <div className="small me-auto">
                  <div className="fw-semibold">Datos fiscales</div>
                  <div className="text-muted">
                    {editingCustomer.fiscalData
                      ? `RFC ${editingCustomer.fiscalData.rfc} · ${editingCustomer.fiscalData.name}`
                      : 'Sin datos fiscales: la factura de sus pedidos los pide.'}
                  </div>
                </div>
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  disabled={isDirty}
                  onClick={() => {
                    const customer = editingCustomer;
                    closeModal();
                    setFiscalCustomer(customer);
                  }}
                >
                  {editingCustomer.fiscalData ? 'Cambiar datos fiscales' : 'Agregar datos fiscales'}
                </button>
                {isDirty && <p className="small text-muted w-100 mb-0">Guarda primero los cambios del cliente.</p>}
              </div>
            )}
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={closeModal}>
              Cancelar
            </button>
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
              {isSubmitting ? 'Guardando...' : editingCustomer ? 'Guardar cambios' : 'Crear cliente'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>

      {cardCustomer && (
        <CustomerCardModal customerId={cardCustomer.id} name={cardCustomer.name} onClose={() => setCardCustomer(null)} />
      )}
      {fiscalCustomer && (
        <CustomerFiscalDataModal
          customer={fiscalCustomer}
          onClose={(changed) => {
            setFiscalCustomer(null);
            if (changed) void loadCustomers();
          }}
        />
      )}
      {creditCustomer && (
        <CreditLimitModal
          customer={creditCustomer}
          onClose={() => {
            setCreditCustomer(null);
            void loadCustomers();
          }}
        />
      )}
    </>
  );
}
