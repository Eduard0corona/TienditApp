import { useCallback, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { Modal } from 'react-bootstrap';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { APPOINTMENTS_SOUND_KEY, useOnAppointmentRequestsChanged } from '../../hooks/useAppointmentRequests';
import { useArrivalChime } from '../../hooks/useArrivalChime';
import PageTitle from '../../components/common/PageTitle';
import ChimeSettingsButton from '../../components/common/ChimeSettingsButton';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import VisitProblem from '../../components/appointments/VisitProblem';
import { appointmentsApi } from '../../api/appointmentsApi';
import { employeesApi } from '../../api/employeesApi';
import { quoteVisit } from '../../api/homeVisits';
import { productsApi } from '../../api/productsApi';
import { quotesApi } from '../../api/quotesApi';
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
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import { queryKeys } from '../../api/queries';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { getAccess, hasUserPermission } from '../../utils/access';
import { businessDepositLabel, canReturnDeposit, holdsDeposit } from '../../utils/bookingDeposits';
import { formatMoney } from '../../utils/money';
import { photoCount } from '../../utils/problemPhotos';
import { answerInTimeLabel, FROM_TEAM_LABEL, MARKED_FAKE_LABEL } from '../../utils/responseTime';
import {
  businessDateTimeToIso,
  businessDayStart,
  formatInBusinessZone,
  getBusinessToday,
  toBusinessDateTimeInput,
} from '../../utils/dates';
import {
  VISIT_ADDRESS_LINE_REQUIRED_MESSAGE,
  VISIT_POSTAL_CODE_REQUIRED_MESSAGE,
  describeVisitPlace,
  describeVisitToQuote,
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

/**
 * The instant, in milliseconds, of a start or an end typed on the business's clock ("2026-10-05T10:30"), not the
 * device's. An appointment being edited keeps its own while the field shows it (the hour the clock repeats).
 */
function instantOf(input: string, original?: string): number {
  return Date.parse(businessDateTimeToIso(input, original));
}

function addMinutes(input: string, minutes: number, original?: string): string {
  return toBusinessDateTimeInput(instantOf(input, original) + minutes * 60_000);
}

/** Same minute: the server may answer with seconds or another offset notation. */
function sameMinute(a: string, b: string): boolean {
  return Math.floor(new Date(a).getTime() / 60_000) === Math.floor(new Date(b).getTime() / 60_000);
}

/** «Ver lo que pasa · 2 fotos», or «Ver lo que pasa» when the customer only wrote it. */
function problemLabel(item: AppointmentDto): string {
  const photos = item.problemPhotos?.length ?? 0;
  return photos > 0 ? `Ver lo que pasa · ${photoCount(photos)}` : 'Ver lo que pasa';
}

function formatSlot(item: AppointmentDto): string {
  const start = new Date(item.startAt);
  const end = new Date(item.endAt);
  // The same day of the business: an appointment past its midnight shows the date it ends.
  const sameDay = toBusinessDateTimeInput(start).slice(0, 10) === toBusinessDateTimeInput(end).slice(0, 10);
  const day = formatInBusinessZone(start, { weekday: 'short', day: 'numeric', month: 'short' });
  const from = formatInBusinessZone(start, { hour: '2-digit', minute: '2-digit' });
  const to = sameDay
    ? formatInBusinessZone(end, { hour: '2-digit', minute: '2-digit' })
    : formatInBusinessZone(end, { dateStyle: 'short', timeStyle: 'short' });
  return `${day} · ${from} a ${to}`;
}

/** By when a request is answered in time; it reads the clock every minute, so it says the limit passed with the agenda open. */
function AnswerInTime({ answerBy }: { answerBy: string }) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 60_000);
    return () => window.clearInterval(timer);
  }, []);
  return <div className="text-muted small">{answerInTimeLabel(answerBy, now)}</div>;
}

export default function AppointmentsList() {
  const titleId = useId();
  const toast = useToast();
  const { confirm } = useConfirm();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const linkedState = searchParams.get('estado') ?? '';
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  // «Cobrar» opens Vender with the appointment: only for whoever sells.
  const canSell = getAccess(user, capabilities).sell;
  // «Cotizar» opens the quote of a visit (Fase 29C): for whoever takes orders.
  const canQuote = getAccess(user, capabilities).quotes;
  // The order of the job of a quote (Fase 29F) opens for whoever sees orders.
  const canOpenOrders = getAccess(user, capabilities).orders;
  // The deposit an appointment was booked with goes back for whoever processes refunds (Fase 31O), as the API asks.
  const canReturnDeposits = hasUserPermission(user, 'ProcessRefunds');
  const [quoting, setQuoting] = useState<string | null>(null);
  // Like the server's notices: with IVA included while the business is read.
  const pricesIncludeTax = tenant?.pricesIncludeTax ?? true;
  const [data, setData] = useState<PaginatedList<AppointmentDto>>(EMPTY_PAGINATED);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<View>('upcoming');
  const [statusFilter, setStatusFilter] = useState(linkedState === REQUESTS_LINK ? REQUESTED_FILTER : '');
  // Fase 29P: the sound of each request or cancellation of a customer, on this device; the top bar plays it.
  const chime = useArrivalChime(APPOINTMENTS_SOUND_KEY);
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
  // What the customer said happens, with their photos (Fase 29H), of the visit opened.
  const [problemOf, setProblemOf] = useState<AppointmentDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');
  const [form, setForm] = useState<FormState>(buildForm());
  const [services, setServices] = useState<ProductDto[]>([]);
  const [staff, setStaff] = useState<EmployeeDto[]>([]);
  const [quoteAnswer, setQuoteAnswer] = useState<QuoteAnswer | null>(null);

  // Each read of the list: only the latest one shows (a quiet one after a request may finish after a newer one).
  const listRead = useRef(0);
  // A read the person is waiting for (with the spinner) has not finished yet.
  const visibleReadPending = useRef(false);
  const loadData = useCallback(async ({ silent = false }: { silent?: boolean } = {}) => {
    // The route reads its own day.
    if (view === 'route') return;
    const read = ++listRead.current;
    // A quiet read that takes the place of one the person is waiting for answers as that one would, its error included.
    const quiet = silent && !visibleReadPending.current;
    if (!silent) {
      visibleReadPending.current = true;
      setLoading(true);
    }
    try {
      // Upcoming opens with the business's today, the soonest first; history reads from the newest back.
      const params: {
        status?: AppointmentStatus;
        search?: string;
        fromDate?: string;
        toDate?: string;
        homeVisit?: boolean;
        pageNumber: number;
        pageSize: number;
      } = view === 'upcoming'
        ? { pageNumber, pageSize, fromDate: businessDayStart(getBusinessToday()) }
        : { pageNumber, pageSize, toDate: new Date().toISOString() };
      if (statusFilter !== '') params.status = Number(statusFilter) as AppointmentStatus;
      if (search) params.search = search;
      if (placeFilter !== '') params.homeVisit = placeFilter === 'home';
      const { data } = await appointmentsApi.getAll(params);
      if (read === listRead.current) setData(data);
    } catch (err: unknown) {
      // A quiet read that fails says nothing: the next request, the bell or the person reads it again.
      if (read === listRead.current && !quiet) setError(getApiErrorMessage(err, 'No se pudieron cargar las citas.'));
    } finally {
      if (read === listRead.current) {
        visibleReadPending.current = false;
        setLoading(false);
      }
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

  // Fase 29N: a request online arrived (or the live connection came back): read the list and the requests again, quietly.
  useOnAppointmentRequestsChanged(
    useCallback(() => {
      void loadData({ silent: true });
      void loadRequests();
    }, [loadData, loadRequests]),
  );

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
    // A visit «Sin costo» requested online stays free wherever it moves (Fase 29B), and the job of an order charges nothing to
    // go there (Fase 29F, charged in its order): the server charges nothing for the zone.
    : {
      kind: 'quoted',
      zoneName: quoteAnswer.zoneName,
      fee: editing?.workOrderId || editing?.visit?.chargePolicy === HomeVisitChargePolicy.Free ? 0 : quoteAnswer.fee,
    };

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
      startAt: toBusinessDateTimeInput(item.startAt),
      endAt: toBusinessDateTimeInput(item.endAt),
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
      endAt: current.startAt && serviceProductId !== OTHER_SERVICE
        ? addMinutes(current.startAt, durationOf(serviceProductId), editing?.startAt)
        : current.endAt,
    }));
  };

  const changeStart = (startAt: string) => {
    setForm((current) => {
      const duration = current.startAt && current.endAt
        ? Math.max(5, Math.round((instantOf(current.endAt, editing?.endAt) - instantOf(current.startAt, editing?.startAt)) / 60_000))
        : durationOf(current.serviceProductId);
      return { ...current, startAt, endAt: startAt ? addMinutes(startAt, duration, editing?.startAt) : current.endAt };
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
    if (!form.startAt || !form.endAt || instantOf(form.endAt, editing?.endAt) <= instantOf(form.startAt, editing?.startAt)) {
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
      startAt: businessDateTimeToIso(form.startAt, editing?.startAt),
      endAt: businessDateTimeToIso(form.endAt, editing?.endAt),
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
      // Not awaited: the dialog is done once it is saved, and «Guardando…» belongs to it, not to the list.
      void loadData();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, 'No se pudo guardar la cita.'));
    } finally {
      setSaving(false);
    }
  };

  const updateStatus = async (item: AppointmentDto, newStatus: AppointmentStatus) => {
    // With its deposit held (Fase 31O), cancelling gives it back and «No asistió» keeps it: said before it changes.
    if (holdsDeposit(item) && (newStatus === AppointmentStatusConst.Cancelled || newStatus === AppointmentStatusConst.NoShow)) {
      const amount = formatMoney(item.depositAmount!);
      const cancelling = newStatus === AppointmentStatusConst.Cancelled;
      const accepted = await confirm({
        title: cancelling ? 'Cancelar la cita' : 'El cliente no asistió',
        message: cancelling
          ? `La cita de ${item.customerName} se cancela y su anticipo de ${amount} se le devuelve.`
          : `Te quedas con el anticipo de ${amount} de ${item.customerName}. Si quieres, después puedes devolvérselo.`,
        confirmLabel: cancelling ? 'Cancelar y devolver' : 'No asistió',
        cancelLabel: 'Volver',
        danger: cancelling,
      });
      if (!accepted) return;
    }
    // Done without its sale, the deposit stays held: it enters when it is charged in Vender, or the business gives it back.
    if (holdsDeposit(item) && newStatus === AppointmentStatusConst.Completed) {
      const accepted = await confirm({
        title: 'Marcar como completada',
        message: `La cita de ${item.customerName} queda completada sin cobrar: su anticipo de ${formatMoney(item.depositAmount!)} `
          + 'sigue retenido hasta que la cobres en Vender o se lo devuelvas.',
        confirmLabel: 'Completar',
        cancelLabel: 'Volver',
      });
      if (!accepted) return;
    }
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
      // The bell and Inicio count the requests too.
      void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      await Promise.all([loadData(), loadRequests()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo aceptar la cita.'));
    }
  };

  const decline = async (item: AppointmentDto) => {
    const accepted = await confirm({
      title: 'Rechazar la solicitud',
      message: `La cita de ${item.customerName} (${item.serviceName}, ${formatSlot(item)}) se cancela y el horario queda libre. `
        + 'El cliente recibe el aviso para elegir otro.'
        + (holdsDeposit(item) ? ` Su anticipo de ${formatMoney(item.depositAmount!)} se le devuelve.` : ''),
      confirmLabel: 'Rechazar',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    setError('');
    try {
      await appointmentsApi.decline(item.id);
      toast.success('Solicitud rechazada: el cliente recibe el aviso.');
      void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      await Promise.all([loadData(), loadRequests()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo rechazar la solicitud.'));
    }
  };

  // A request of a customer nobody meant to book (Fase 29V): declined without telling anyone, it does not count in Explorar.
  const markFake = async (item: AppointmentDto) => {
    const accepted = await confirm({
      title: 'Marcar la solicitud como falsa',
      message: `La cita de ${item.customerName} (${item.serviceName}, ${formatSlot(item)}) se cancela y el horario queda libre, `
        + 'sin avisarle a nadie: el teléfono puede no ser de quien la mandó. No cuenta en tu tiempo de respuesta en Explorar, '
        + 'y ese teléfono no puede pedirte otra cita ni visita en 90 días. Si te equivocas, puedes quitar la marca.'
        + (holdsDeposit(item) ? ` Su anticipo de ${formatMoney(item.depositAmount!)} se devuelve.` : ''),
      confirmLabel: 'Es falsa',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    setError('');
    try {
      await appointmentsApi.markFake(item.id);
      toast.success('Solicitud marcada como falsa: no cuenta en Explorar.');
      void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      await Promise.all([loadData(), loadRequests()]);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo marcar la solicitud como falsa.'));
    }
  };

  // A customer's after all: declined now, and told so; it cannot be marked again.
  const unmarkFake = async (item: AppointmentDto) => {
    const accepted = await confirm({
      title: 'Quitar la marca de falsa',
      message: `La solicitud de ${item.customerName} (${item.serviceName}, ${formatSlot(item)}) sigue cancelada, ahora como rechazada: `
        + 'el cliente recibe el aviso, cuenta en tu tiempo de respuesta desde hoy y ese teléfono puede volver a pedirte. '
        + 'No podrás marcarla como falsa otra vez.',
      confirmLabel: 'No era falsa',
      cancelLabel: 'Volver',
    });
    if (!accepted) return;
    setError('');
    try {
      await appointmentsApi.unmarkFake(item.id);
      toast.success('Ya no está marcada como falsa: el cliente recibe el aviso, y ese teléfono puede volver a pedirte.');
      await loadData();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo quitar la marca.'));
    }
  };

  // Fase 31O: the business gives back a deposit still held (the whole appointment is charged then) or one it kept.
  const returnDeposit = async (item: AppointmentDto) => {
    const amount = formatMoney(item.depositAmount!);
    const accepted = await confirm({
      title: 'Devolver el anticipo',
      message: holdsDeposit(item)
        ? `El anticipo de ${amount} vuelve a ${item.customerName} por Mercado Pago, y al cobrar la cita se cobra completa.`
        : `El anticipo de ${amount} que te quedaste vuelve a ${item.customerName} por Mercado Pago.`,
      confirmLabel: 'Devolver',
      cancelLabel: 'Volver',
    });
    if (!accepted) return;
    setError('');
    try {
      await appointmentsApi.returnDeposit(item.id);
      toast.success('Anticipo devuelto: se le regresa al cliente por Mercado Pago.');
      await loadData();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo devolver el anticipo.'));
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

  // The visit's quote: a new draft with its customer, or the one it already has.
  const quote = async (appointmentId: string) => {
    setQuoting(appointmentId);
    try {
      const created = await quotesApi.create({ appointmentId });
      void queryClient.invalidateQueries({ queryKey: ['quotes'] });
      navigate(`/quotes/${created.id}`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo abrir la cotización de la visita.'));
      setQuoting(null);
    }
  };

  /** What a closed appointment offers, with «Cotizar» beside it when it is a visit done (never the job of an order). */
  const withQuote = (item: AppointmentDto, action: ReactNode) => {
    const quoteAction = quoteButton(item);
    return quoteAction ? <div className="d-flex gap-1 justify-content-center align-items-center">{action}{quoteAction}</div> : action;
  };

  const quoteButton = (item: AppointmentDto) => (canQuote && item.visit && !item.workOrderId
    && (item.status === AppointmentStatusConst.Completed || !isClosed(item.status)) ? (
      <button
        type="button"
        className="btn btn-sm btn-light"
        disabled={quoting !== null}
        onClick={() => void quote(item.id)}
        aria-label={`Cotizar la visita de ${item.customerName}`}
      >
        Cotizar
      </button>
    ) : null);

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
          {view === 'upcoming' && statusFilter === REQUESTED_FILTER && (
            // Fase 29P: the server reads them by when each is answered in time.
            <p className="text-muted small mb-2">Primero las que hay que responder antes; al final, las de tu equipo.</p>
          )}
          {chime.needsTouch && (
            // The browser plays nothing until it is touched after it opens.
            <p className="text-muted small mb-2" role="status">
              Toca la pantalla o presiona una tecla para que suenen las solicitudes.
            </p>
          )}
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
            views={viewPicker}
            filterValues={[placeFilter, statusFilter]}
            filters={(
              <>
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
              <>
                <button
                  type="button"
                  className="btn btn-light btn-sm"
                  onClick={chime.toggle}
                  aria-pressed={chime.enabled}
                  title="Suena en este navegador cuando un cliente pide o cancela una cita en línea"
                >
                  <i className={`${chime.enabled ? 'ri-volume-up-line' : 'ri-volume-mute-line'} me-1`} aria-hidden="true"></i>
                  Sonido para solicitudes en línea
                </button>
                <ChimeSettingsButton />
                <button type="button" className="btn btn-primary btn-sm" onClick={openNew}>
                  <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva cita
                </button>
              </>
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
                  {item.customerEmail && <div className="text-muted small text-break">{item.customerEmail}</div>}
                </td>
                <td>
                  <div>{item.serviceName}</div>
                  <div className="text-muted small">{item.staffName || 'Sin asignar'}</div>
                  {/* A visit requested online carries what the customer said happens and their photos (Fase 29H). */}
                  {item.visit && item.channel === AppointmentChannel.Online && (item.notes || (item.problemPhotos?.length ?? 0) > 0) && (
                    <button
                      type="button"
                      className="btn btn-link btn-sm p-0 text-start"
                      onClick={() => setProblemOf(item)}
                      aria-label={`${problemLabel(item)}, en la visita de ${item.customerName}`}
                    >
                      {problemLabel(item)}
                    </button>
                  )}
                  {/* The job of an order (Fase 29F): charged in it, so it opens the order (for whoever sees orders) instead of «Cobrar». */}
                  {item.workOrderNumber && (
                    <div className="text-muted small">
                      Se cobra en la orden{' '}
                      {canOpenOrders
                        ? <Link to={`/orders?search=${encodeURIComponent(item.workOrderNumber)}`}>{item.workOrderNumber}</Link>
                        : item.workOrderNumber}
                    </div>
                  )}
                </td>
                <td>
                  <div>{formatSlot(item)}</div>
                  {item.visit && <div className="text-muted small">{describeVisitPlace(item.visit)}</div>}
                  {item.visit && describeVisitToQuote(item.visit) && <div className="text-muted small">{describeVisitToQuote(item.visit)}</div>}
                </td>
                <td className="text-center">
                  <span className={`badge ${statusBadge(item.status)}`}>{statusLabel(item.status)}</span>
                  {item.cancelledByCustomer && <div className="text-muted small">La canceló el cliente</div>}
                  {item.markedFake && <div className="text-muted small">{MARKED_FAKE_LABEL}</div>}
                  {/* The deposit it was booked with online, where it stands (Fase 31O). */}
                  {businessDepositLabel(item) && <div className="text-muted small">{businessDepositLabel(item)}</div>}
                  {canReturnDeposits && canReturnDeposit(item) && (
                    <button
                      type="button"
                      className="btn btn-link btn-sm p-0"
                      onClick={() => void returnDeposit(item)}
                      aria-label={`Devolver anticipo de ${item.customerName}`}
                    >
                      Devolver anticipo
                    </button>
                  )}
                  {/* By when it is answered in time, as Explorar counts it (Fase 29L); one of the team does not count (29M). */}
                  {item.status === AppointmentStatusConst.Requested && item.answerBy && (
                    <AnswerInTime answerBy={item.answerBy} />
                  )}
                  {item.status === AppointmentStatusConst.Requested && item.fromTeam && (
                    <div className="text-muted small">{FROM_TEAM_LABEL}</div>
                  )}
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
                      {/* One of its own team does not count anyway (Fase 29M): only a customer's is marked as fake. */}
                      {!item.fromTeam && (
                        <button
                          type="button"
                          className="btn btn-sm btn-light"
                          onClick={() => void markFake(item)}
                          aria-label={`Es falsa la solicitud de ${item.customerName}`}
                        >
                          Es falsa
                        </button>
                      )}
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
                  ) : item.markedFake ? (
                    <button
                      type="button"
                      className="btn btn-sm btn-light"
                      onClick={() => void unmarkFake(item)}
                      aria-label={`No era falsa la solicitud de ${item.customerName}`}
                    >
                      No era falsa
                    </button>
                  ) : isClosed(item.status) ? (
                    withQuote(item, canSell && canBeCharged(item) ? (
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
                    ))
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
                      {quoteButton(item)}
                    </div>
                  )}
                </td>
              </>
            )}
          />
          )}
        </div>
      </div>

      <Modal show={problemOf !== null} onHide={() => setProblemOf(null)} centered aria-labelledby={`${titleId}-problem`}>
        <Modal.Header closeButton>
          <Modal.Title id={`${titleId}-problem`} as="h2" className="h5">Lo que pasa</Modal.Title>
        </Modal.Header>
        {problemOf && (
          <Modal.Body>
            <p className="text-muted small mb-2">{problemOf.customerName} · {formatSlot(problemOf)}</p>
            <VisitProblem problem={problemOf.notes} photos={problemOf.problemPhotos ?? []} />
          </Modal.Body>
        )}
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setProblemOf(null)}>Cerrar</button>
        </Modal.Footer>
      </Modal>

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
          {editing && form.startAt && !sameMinute(editing.startAt, businessDateTimeToIso(form.startAt, editing.startAt)) && (
            <div className="alert alert-info py-2 small mt-3 mb-0">Al guardar, el cliente recibe por WhatsApp el nuevo horario.</div>
          )}
          {editing && sameMinute(editing.startAt, form.startAt ? businessDateTimeToIso(form.startAt, editing.startAt) : editing.startAt)
            && (editing.visit?.postalCode ?? '') !== (form.atHome ? form.postalCode.trim() : '') && (
            <div className="alert alert-info py-2 small mt-3 mb-0">Al guardar, el cliente recibe por WhatsApp el nuevo lugar.</div>
          )}
          {formError && <div className="alert alert-danger mt-3 mb-0" role="alert">{formError}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={() => setShowModal(false)} disabled={saving}>Cancelar</button>
          {/* Also while it fades out after saving, so a double click does not book it twice. */}
          <button type="button" className="btn btn-primary" onClick={() => void submit()} disabled={saving || !showModal}>
            {saving ? 'Guardando…' : editing ? 'Guardar cambios' : 'Registrar cita'}
          </button>
        </Modal.Footer>
      </Modal>
    </>
  );
}
