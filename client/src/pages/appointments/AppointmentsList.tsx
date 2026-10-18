import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useNavigate, useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { appointmentsApi } from '../../api/appointmentsApi';
import { employeesApi } from '../../api/employeesApi';
import { quoteVisit } from '../../api/homeVisits';
import { productsApi } from '../../api/productsApi';
import type {
  AppointmentDto,
  AppointmentStatus,
  CreateAppointmentRequest,
  EmployeeDto,
  PaginatedList,
  ProductDto,
} from '../../types';
import { AppointmentChannel, AppointmentStatus as AppointmentStatusConst } from '../../types';
import { getApiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { getAccess } from '../../utils/access';
import { formatInBusinessZone } from '../../utils/dates';
import {
  VISIT_ADDRESS_LINE_REQUIRED_MESSAGE,
  VISIT_POSTAL_CODE_REQUIRED_MESSAGE,
  describeVisitPlace,
} from '../../utils/homeVisits';
import { isPostalCode } from '../../utils/postalCodes';
import AppointmentVisitFields, { type VisitFieldsState, type VisitQuoteView } from './AppointmentVisitFields';
import { isSameAddress } from './appointmentVisit';
import { canBeCharged, isClosed, saleLabel, statusBadge, statusLabel } from './appointmentStatus';
import VisitRoute from './VisitRoute';

const COLUMNS = [
  { header: 'Cliente' },
  { header: 'Servicio' },
  { header: 'Horario' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Acciones', className: 'text-center' },
];

const EMPTY_PAGINATED = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

/** A free service name typed by hand instead of one of the catalog. */
const OTHER_SERVICE = 'other';
const DEFAULT_DURATION_MINUTES = 60;

type View = 'upcoming' | 'history' | 'route';

/** The agenda opened from the notice of requests booked online (/appointments?estado=por-confirmar, Fase 24D). */
const REQUESTS_LINK = 'por-confirmar';
const REQUESTED_FILTER = String(AppointmentStatusConst.Requested);

/** Where the appointments listed happen: '' all, 'home' at the customer's address, 'business' at the business. */
type PlaceFilter = '' | 'home' | 'business';

interface FormState extends VisitFieldsState {
  customerName: string;
  customerPhone: string;
  serviceProductId: string;
  serviceName: string;
  staffUserId: string;
  startAt: string;
  endAt: string;
  notes: string;
}

const buildForm = (): FormState => ({
  customerName: '',
  customerPhone: '',
  serviceProductId: '',
  serviceName: '',
  staffUserId: '',
  startAt: '',
  endAt: '',
  notes: '',
  atHome: false,
  postalCode: '',
  addressLine: '',
  references: '',
});

/** The answer of the server for a postal code: the zone and the charge, or why it does not go there. */
type QuoteAnswer = { postalCode: string } & ({ zoneName: string; fee: number } | { message: string });

/** "2026-10-05T10:30" for a datetime-local input, in the device's time. */
function toInputDateTime(value: string | Date): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function addMinutes(input: string, minutes: number): string {
  return toInputDateTime(new Date(new Date(input).getTime() + minutes * 60_000));
}

/** Same minute: the server may answer with seconds or another offset notation. */
function sameMinute(a: string, b: string): boolean {
  return Math.floor(new Date(a).getTime() / 60_000) === Math.floor(new Date(b).getTime() / 60_000);
}

function startOfToday(): Date {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return today;
}

function formatSlot(item: AppointmentDto): string {
  const start = new Date(item.startAt);
  const end = new Date(item.endAt);
  const sameDay = start.toDateString() === end.toDateString();
  const day = formatInBusinessZone(start, { weekday: 'short', day: 'numeric', month: 'short' });
  const from = formatInBusinessZone(start, { hour: '2-digit', minute: '2-digit' });
  const to = sameDay
    ? formatInBusinessZone(end, { hour: '2-digit', minute: '2-digit' })
    : formatInBusinessZone(end, { dateStyle: 'short', timeStyle: 'short' });
  return `${day} · ${from} a ${to}`;
}

export default function AppointmentsList() {
  const titleId = useId();
  const toast = useToast();
  const { confirm } = useConfirm();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const linkedState = searchParams.get('estado') ?? '';
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  // «Cobrar» opens Vender with the appointment: only for whoever sells.
  const canSell = getAccess(user, capabilities).sell;
  // Like the server's notices: with IVA included while the business is read.
  const pricesIncludeTax = tenant?.pricesIncludeTax ?? true;
  const [data, setData] = useState<PaginatedList<AppointmentDto>>(EMPTY_PAGINATED);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<View>('upcoming');
  const [statusFilter, setStatusFilter] = useState(linkedState === REQUESTS_LINK ? REQUESTED_FILTER : '');
  const [placeFilter, setPlaceFilter] = useState<PlaceFilter>('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  // Requests booked online still waiting from now on (Fase 24D): the notice above the list counts them.
  const [requests, setRequests] = useState(0);
  const [seenLink, setSeenLink] = useState(linkedState);
  if (seenLink !== linkedState) {
    setSeenLink(linkedState);
    if (linkedState === REQUESTS_LINK) {
      setView('upcoming');
      setStatusFilter(REQUESTED_FILTER);
      setPageNumber(1);
    }
  }

  const [editing, setEditing] = useState<AppointmentDto | null>(null);
  const [showModal, setShowModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');
  const [form, setForm] = useState<FormState>(buildForm());
  const [services, setServices] = useState<ProductDto[]>([]);
  const [staff, setStaff] = useState<EmployeeDto[]>([]);
  const [quoteAnswer, setQuoteAnswer] = useState<QuoteAnswer | null>(null);

  const loadData = useCallback(async () => {
    // The route reads its own day.
    if (view === 'route') return;
    setLoading(true);
    try {
      // Upcoming opens with today, the soonest first; history reads from the newest back.
      const params: {
        status?: AppointmentStatus;
        search?: string;
        fromDate?: string;
        toDate?: string;
        homeVisit?: boolean;
        pageNumber: number;
        pageSize: number;
      } = view === 'upcoming'
        ? { pageNumber, pageSize, fromDate: startOfToday().toISOString() }
        : { pageNumber, pageSize, toDate: new Date().toISOString() };
      if (statusFilter !== '') params.status = Number(statusFilter) as AppointmentStatus;
      if (search) params.search = search;
      if (placeFilter !== '') params.homeVisit = placeFilter === 'home';
      const { data } = await appointmentsApi.getAll(params);
      setData(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar las citas.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, placeFilter, search, statusFilter, view]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const loadRequests = useCallback(async () => {
    try {
      const { data } = await appointmentsApi.getAll({
        status: AppointmentStatusConst.Requested,
        fromDate: new Date().toISOString(),
        pageSize: 1,
      });
      setRequests(data.totalCount);
    } catch {
      // The list says what failed; the notice waits for the next time.
    }
  }, []);

  useEffect(() => {
    void loadRequests();
  }, [loadRequests]);

  // The catalog's services and the active team, for the form and the route.
  useEffect(() => {
    if ((!showModal && view !== 'route') || (services.length > 0 && staff.length > 0)) return;
    let cancelled = false;
    Promise.all([
      productsApi.getAll({ itemType: 'Service', isAvailable: true, pageSize: 200 }).catch(() => null),
      employeesApi.getAll({ status: 'Active', pageSize: 200 }).catch(() => null),
    ]).then(([servicesResponse, staffResponse]) => {
      if (cancelled) return;
      if (servicesResponse) setServices(servicesResponse.data.items);
      if (staffResponse) setStaff(staffResponse.data.items.filter((member) => member.isActive));
    });
    return () => {
      cancelled = true;
    };
  }, [showModal, view, services.length, staff.length]);

  // At the same address an appointment keeps what was agreed; another one is quoted again.
  const agreed = editing?.visit && form.atHome && isSameAddress(editing.visit, form) ? editing.visit : null;
  // What a visit to the postal code typed charges: asked once the five digits are there.
  const typedPostalCode = form.postalCode.trim();
  const shouldQuote = showModal && form.atHome && !agreed && isPostalCode(typedPostalCode);
  useEffect(() => {
    if (!shouldQuote) return;
    const controller = new AbortController();
    quoteVisit(typedPostalCode, controller.signal)
      .then((quote) => setQuoteAnswer({ postalCode: typedPostalCode, zoneName: quote.zoneName, fee: quote.fee }))
      .catch((err: unknown) => {
        if (!controller.signal.aborted) {
          setQuoteAnswer({ postalCode: typedPostalCode, message: getApiErrorMessage(err, 'No se pudo consultar la zona.') });
        }
      });
    return () => controller.abort();
  }, [shouldQuote, typedPostalCode]);

  const quoteView: VisitQuoteView = agreed
    ? { kind: 'agreed', zoneName: agreed.zoneName, fee: agreed.fee }
    : !shouldQuote ? { kind: 'none' }
    : quoteAnswer?.postalCode !== typedPostalCode ? { kind: 'loading' }
    : 'message' in quoteAnswer ? { kind: 'refused', message: quoteAnswer.message }
    : { kind: 'quoted', zoneName: quoteAnswer.zoneName, fee: quoteAnswer.fee };

  const serviceById = useMemo(() => new Map(services.map((service) => [service.id, service])), [services]);

  const openNew = () => {
    setEditing(null);
    setForm(buildForm());
    setFormError('');
    setShowModal(true);
  };

  const openEdit = (item: AppointmentDto) => {
    setEditing(item);
    setForm({
      customerName: item.customerName,
      customerPhone: item.customerPhone,
      serviceProductId: item.serviceProductId ?? OTHER_SERVICE,
      serviceName: item.serviceName,
      staffUserId: item.staffUserId ?? '',
      startAt: toInputDateTime(item.startAt),
      endAt: toInputDateTime(item.endAt),
      notes: item.notes ?? '',
      atHome: Boolean(item.visit),
      postalCode: item.visit?.postalCode ?? '',
      addressLine: item.visit?.addressLine ?? '',
      references: item.visit?.references ?? '',
    });
    setFormError('');
    setShowModal(true);
  };

  const durationOf = (serviceProductId: string) =>
    serviceById.get(serviceProductId)?.preparationTimeMinutes || DEFAULT_DURATION_MINUTES;

  const changeService = (serviceProductId: string) => {
    setForm((current) => ({
      ...current,
      serviceProductId,
      // Changing the service keeps the start and moves the end to its duration.
      endAt: current.startAt && serviceProductId !== OTHER_SERVICE ? addMinutes(current.startAt, durationOf(serviceProductId)) : current.endAt,
    }));
  };

  const changeStart = (startAt: string) => {
    setForm((current) => {
      const duration = current.startAt && current.endAt
        ? Math.max(5, Math.round((new Date(current.endAt).getTime() - new Date(current.startAt).getTime()) / 60_000))
        : durationOf(current.serviceProductId);
      return { ...current, startAt, endAt: startAt ? addMinutes(startAt, duration) : current.endAt };
    });
  };

  const submit = async () => {
    setFormError('');
    const usesCatalog = form.serviceProductId !== '' && form.serviceProductId !== OTHER_SERVICE;
    if (!form.customerName.trim() || !form.customerPhone.trim()) {
      setFormError('El nombre y el teléfono del cliente son obligatorios.');
      return;
    }
    if (!usesCatalog && !form.serviceName.trim()) {
      setFormError('Elige el servicio o escribe cuál es.');
      return;
    }
    if (!form.startAt || !form.endAt || new Date(form.endAt) <= new Date(form.startAt)) {
      setFormError('Indica el inicio y un fin posterior al inicio.');
      return;
    }
    if (form.atHome && !isPostalCode(form.postalCode.trim())) {
      setFormError(VISIT_POSTAL_CODE_REQUIRED_MESSAGE);
      return;
    }
    if (form.atHome && !form.addressLine.trim()) {
      setFormError(VISIT_ADDRESS_LINE_REQUIRED_MESSAGE);
      return;
    }

    const payload: CreateAppointmentRequest = {
      customerName: form.customerName.trim(),
      customerPhone: form.customerPhone.trim(),
      serviceProductId: usesCatalog ? form.serviceProductId : undefined,
      serviceName: usesCatalog ? undefined : form.serviceName.trim(),
      staffUserId: form.staffUserId || undefined,
      startAt: new Date(form.startAt).toISOString(),
      endAt: new Date(form.endAt).toISOString(),
      notes: form.notes.trim() || undefined,
      // The server finds the zone and sets the charge (at the same address it keeps the one agreed).
      visit: form.atHome
        ? { postalCode: form.postalCode.trim(), addressLine: form.addressLine.trim(), references: form.references.trim() || undefined }
        : null,
    };

    try {
      setSaving(true);
      if (editing) {
        await appointmentsApi.update(editing.id, payload);
        const moved = !sameMinute(editing.startAt, payload.startAt) || (editing.visit?.postalCode ?? null) !== (payload.visit?.postalCode ?? null);
        toast.success(moved ? 'Cita reprogramada: el cliente recibe el aviso.' : 'Cita actualizada.');
      } else {
        await appointmentsApi.create(payload);
        toast.success('Cita registrada.');
      }
      setShowModal(false);
      await loadData();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, 'No se pudo guardar la cita.'));
    } finally {
      setSaving(false);
    }
  };

  const updateStatus = async (item: AppointmentDto, newStatus: AppointmentStatus) => {
    setError('');
    try {
      await appointmentsApi.updateStatus(item.id, newStatus);
      toast.success('Estado de la cita actualizado.');
      await loadData();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo actualizar el estado.'));
    }
  };

  const accept = async (item: AppointmentDto) => {
    setError('');
    try {
      await appointmentsApi.accept(item.id);
      toast.success('Cita aceptada: el cliente recibe el aviso.');
      await Promise.all([loadData(), loadRequests()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo aceptar la cita.'));
    }
  };

  const decline = async (item: AppointmentDto) => {
    const accepted = await confirm({
      title: 'Rechazar la solicitud',
      message: `La cita de ${item.customerName} (${item.serviceName}, ${formatSlot(item)}) se cancela y el horario queda libre. `
        + 'El cliente recibe el aviso para elegir otro.',
      confirmLabel: 'Rechazar',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    setError('');
    try {
      await appointmentsApi.decline(item.id);
      toast.success('Solicitud rechazada: el cliente recibe el aviso.');
      await Promise.all([loadData(), loadRequests()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo rechazar la solicitud.'));
    }
  };

  const showRequests = () => {
    setView('upcoming');
    setStatusFilter(REQUESTED_FILTER);
    setPageNumber(1);
  };

  const chooseView = (next: View) => {
    setView(next);
    setPageNumber(1);
  };

  const charge = (appointmentId: string) => navigate(`/sell?appointment=${encodeURIComponent(appointmentId)}`);

  const viewPicker = (
    <div className="btn-group btn-group-sm" role="group" aria-label="Qué citas ver">
      {([['upcoming', 'Próximas'], ['history', 'Anteriores'], ['route', 'Ruta del día']] as const).map(([value, label]) => (
        <button
          key={value}
          type="button"
          className={`btn ${view === value ? 'btn-primary' : 'btn-light'}`}
          aria-pressed={view === value}
          onClick={() => chooseView(value)}
        >
          {label}
        </button>
      ))}
    </div>
  );

  return (
    <>
      <PageTitle title="Agenda" />

      {error && (
        <div className="alert alert-danger alert-dismissible fade show" role="alert">
          {error}
          <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => setError('')}></button>
        </div>
      )}

      {requests > 0 && view !== 'route' && statusFilter !== REQUESTED_FILTER && (
        <div className="alert alert-warning d-flex flex-wrap align-items-center justify-content-between gap-2" role="status">
          <span>
            <i className="ri-calendar-todo-line me-1" aria-hidden="true"></i>
            {requests === 1 ? 'Tienes 1 solicitud de cita por confirmar.' : `Tienes ${requests} solicitudes de cita por confirmar.`}
            {' '}Acéptalas o recházalas: el cliente recibe el aviso.
          </span>
          <button type="button" className="btn btn-sm btn-light" onClick={showRequests}>Ver solicitudes</button>
        </div>
      )}

      <div className="card">
        <div className="card-body">
          {view === 'route' ? (
            <>
              <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-3">
                {viewPicker}
                <button type="button" className="btn btn-primary btn-sm" onClick={openNew}>
                  <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva cita
                </button>
              </div>
              <VisitRoute staff={staff} pricesIncludeTax={pricesIncludeTax} canCharge={canSell} onCharge={charge} />
            </>
          ) : (
          <DataTable
            data={data.items}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyMessage={view === 'upcoming' ? 'No hay citas de hoy en adelante.' : 'No hay citas anteriores.'}
            searchPlaceholder="Cliente, teléfono, servicio o persona"
            filters={(
              <>
                {viewPicker}
                <DataTableFilter label="Dónde" htmlFor="appointments-place">
                  <select
                    id="appointments-place"
                    className="form-select form-select-sm"
                    value={placeFilter}
                    onChange={(e) => {
                      setPlaceFilter(e.target.value as PlaceFilter);
                      setPageNumber(1);
                    }}
                  >
                    <option value="">Todas</option>
                    <option value="business">En el negocio</option>
                    <option value="home">A domicilio</option>
                  </select>
                </DataTableFilter>
                <DataTableFilter label="Estado" htmlFor="appointments-status">
                  <select
                    id="appointments-status"
                    className="form-select form-select-sm"
                    value={statusFilter}
                    onChange={(e) => {
                      setStatusFilter(e.target.value);
                      setPageNumber(1);
                    }}
                  >
                    <option value="">Todos</option>
                    <option value={AppointmentStatusConst.Requested}>Por confirmar</option>
                    <option value={AppointmentStatusConst.Scheduled}>Programada</option>
                    <option value={AppointmentStatusConst.Confirmed}>Confirmada</option>
                    <option value={AppointmentStatusConst.InService}>En servicio</option>
                    <option value={AppointmentStatusConst.Completed}>Completada</option>
                    <option value={AppointmentStatusConst.NoShow}>No asistió</option>
                    <option value={AppointmentStatusConst.Cancelled}>Cancelada</option>
                  </select>
                </DataTableFilter>
              </>
            )}
            toolbarEnd={(
              <button type="button" className="btn btn-primary btn-sm" onClick={openNew}>
                <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva cita
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
            renderRow={(item) => (
              <>
                <td>
                  <div className="fw-semibold">
                    {item.customerName}
                    {/* Booked by the customer from the online menu (Fase 24D): neutral, it is where it came from. */}
                    {item.channel === AppointmentChannel.Online && (
                      <span className="badge text-bg-light border ms-1 fw-normal">En línea</span>
                    )}
                  </div>
                  <div className="text-muted small">{item.customerPhone}</div>
                </td>
                <td>
                  <div>{item.serviceName}</div>
                  <div className="text-muted small">{item.staffName || 'Sin asignar'}</div>
                </td>
                <td>
                  <div>{formatSlot(item)}</div>
                  {item.visit && <div className="text-muted small">{describeVisitPlace(item.visit)}</div>}
                </td>
                <td className="text-center">
                  <span className={`badge ${statusBadge(item.status)}`}>{statusLabel(item.status)}</span>
                  {item.cancelledByCustomer && <div className="text-muted small">La canceló el cliente</div>}
                </td>
                <td className="text-center">
                  {item.status === AppointmentStatusConst.Requested ? (
                    // A request booked online is decided first: accepted, declined, or moved to another time and then accepted.
                    <div className="d-flex gap-1 justify-content-center flex-wrap">
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        onClick={() => void accept(item)}
                        aria-label={`Aceptar la cita de ${item.customerName}`}
                      >
                        Aceptar
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        onClick={() => void decline(item)}
                        aria-label={`Rechazar la cita de ${item.customerName}`}
                      >
                        Rechazar
                      </button>
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        onClick={() => openEdit(item)}
                        aria-label={`Editar o reprogramar la cita de ${item.customerName}`}
                        title="Editar o reprogramar"
                      >
                        Editar
                      </button>
                    </div>
                  ) : isClosed(item.status) ? (
                    canSell && canBeCharged(item) ? (
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        onClick={() => charge(item.id)}
                        aria-label={`Cobrar la cita de ${item.customerName}`}
                      >
                        Cobrar
                      </button>
                    ) : (
                      <span className="text-muted small">{saleLabel(item) ?? 'Cerrada'}</span>
                    )
                  ) : (
                    <div className="d-flex gap-1 justify-content-center">
                      <select
                        className="form-select form-select-sm w-auto"
                        aria-label={`Estado de la cita de ${item.customerName}`}
                        value={item.status}
                        onChange={(e) => void updateStatus(item, Number(e.target.value) as AppointmentStatus)}
                      >
                        <option value={AppointmentStatusConst.Scheduled}>Programada</option>
                        <option value={AppointmentStatusConst.Confirmed}>Confirmada</option>
                        <option value={AppointmentStatusConst.InService}>En servicio</option>
                        <option value={AppointmentStatusConst.Completed}>Completada</option>
                        <option value={AppointmentStatusConst.NoShow}>No asistió</option>
                        <option value={AppointmentStatusConst.Cancelled}>Cancelada</option>
                      </select>
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        onClick={() => openEdit(item)}
                        aria-label={`Editar o reprogramar la cita de ${item.customerName}`}
                        title="Editar o reprogramar"
                      >
                        Editar
                      </button>
                      {canSell && canBeCharged(item) && (
                        <button
                          type="button"
                          className="btn btn-sm btn-light"
                          onClick={() => charge(item.id)}
                          aria-label={`Cobrar la cita de ${item.customerName}`}
                        >
                          Cobrar
                        </button>
                      )}
                    </div>
                  )}
                </td>
              </>
            )}
          />
          )}
        </div>
      </div>

      <Modal show={showModal} onHide={() => !saving && setShowModal(false)} centered aria-labelledby={titleId}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">{editing ? 'Editar o reprogramar la cita' : 'Nueva cita'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <div className="row">
            <div className="col-md-6 mb-2">
              <label className="form-label" htmlFor="appointment-customer">Cliente *</label>
              <input id="appointment-customer" className="form-control" value={form.customerName} onChange={(e) => setForm((x) => ({ ...x, customerName: e.target.value }))} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label" htmlFor="appointment-phone">Teléfono *</label>
              <input id="appointment-phone" type="tel" className="form-control" value={form.customerPhone} onChange={(e) => setForm((x) => ({ ...x, customerPhone: e.target.value }))} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label" htmlFor="appointment-service">Servicio *</label>
              <select id="appointment-service" className="form-select" value={form.serviceProductId} onChange={(e) => changeService(e.target.value)}>
                <option value="">Elige un servicio</option>
                {services.map((service) => (
                  <option key={service.id} value={service.id}>{service.name}</option>
                ))}
                <option value={OTHER_SERVICE}>Otro (escribirlo)</option>
              </select>
              {form.serviceProductId === OTHER_SERVICE && (
                <input
                  className="form-control mt-2"
                  aria-label="Nombre del servicio"
                  placeholder="Nombre del servicio"
                  value={form.serviceName}
                  onChange={(e) => setForm((x) => ({ ...x, serviceName: e.target.value }))}
                />
              )}
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label" htmlFor="appointment-staff">Atiende</label>
              <select id="appointment-staff" className="form-select" value={form.staffUserId} onChange={(e) => setForm((x) => ({ ...x, staffUserId: e.target.value }))}>
                <option value="">Sin asignar</option>
                {staff.map((member) => (
                  <option key={member.userId} value={member.userId}>{member.firstName} {member.lastName}</option>
                ))}
              </select>
            </div>
            <AppointmentVisitFields
              value={form}
              onChange={(changes) => setForm((x) => ({ ...x, ...changes }))}
              quote={quoteView}
              pricesIncludeTax={pricesIncludeTax}
              disabled={saving}
            />
            <div className="col-md-6 mb-2">
              <label className="form-label" htmlFor="appointment-start">Inicio *</label>
              <input id="appointment-start" type="datetime-local" className="form-control" value={form.startAt} onChange={(e) => changeStart(e.target.value)} />
            </div>
            <div className="col-md-6 mb-2">
              <label className="form-label" htmlFor="appointment-end">Fin *</label>
              <input id="appointment-end" type="datetime-local" className="form-control" value={form.endAt} onChange={(e) => setForm((x) => ({ ...x, endAt: e.target.value }))} />
            </div>
            <div className="col-12 mb-0">
              <label className="form-label" htmlFor="appointment-notes">Notas</label>
              <textarea id="appointment-notes" className="form-control" rows={2} value={form.notes} onChange={(e) => setForm((x) => ({ ...x, notes: e.target.value }))} />
            </div>
          </div>
          {editing && form.startAt && !sameMinute(editing.startAt, form.startAt) && (
            <div className="alert alert-info py-2 small mt-3 mb-0">Al guardar, el cliente recibe por WhatsApp el nuevo horario.</div>
          )}
          {editing && sameMinute(editing.startAt, form.startAt || editing.startAt)
            && (editing.visit?.postalCode ?? '') !== (form.atHome ? form.postalCode.trim() : '') && (
            <div className="alert alert-info py-2 small mt-3 mb-0">Al guardar, el cliente recibe por WhatsApp el nuevo lugar.</div>
          )}
          {formError && <div className="alert alert-danger mt-3 mb-0" role="alert">{formError}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setShowModal(false)} disabled={saving}>Cancelar</button>
          <button type="button" className="btn btn-primary" onClick={() => void submit()} disabled={saving}>
            {saving ? 'Guardando…' : editing ? 'Guardar cambios' : 'Registrar cita'}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
