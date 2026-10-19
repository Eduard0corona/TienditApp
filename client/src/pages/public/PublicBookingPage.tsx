import { useEffect, useId, useRef, useState } from 'react';
import { Offcanvas } from 'react-bootstrap';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import {
  publicMenuApi,
  type PublicAppointmentReceipt,
  type PublicBookingSlot,
  type RequestPublicAppointment,
} from '../../api/publicMenuApi';
import PhotoOrInitial from '../../components/common/PhotoOrInitial';
import CopyTrackingLink from '../../components/tracking/CopyTrackingLink';
import { useCustomerAccount } from '../../hooks/useCustomerAccount';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { useScrollFade } from '../../hooks/useScrollFade';
import { formatMoney } from '../../utils/money';
import { describeDuration, shortDayLabel, upperFirst } from '../../utils/onlineBooking';
import { localPhone } from '../../utils/phones';
import { previewBusinessTheme } from '../../utils/theme';
import { newUuid } from '../../utils/uuid';
import { businessWhatsAppUrl } from '../../utils/whatsApp';
import './PublicMenuPage.css';
import './PublicBookingPage.css';

/** The time picked: its own date, which after midnight is the day after the one it is listed under. */
interface ChosenTime {
  slot: PublicBookingSlot;
}

/** No answer, or the server failed: the request may have been stored, and the same one goes again with its key. */
const answerLost = (err: unknown) => {
  const status = getApiErrorStatus(err);
  return status === undefined || status >= 500;
};

const horarios = (count: number) => (count === 1 ? '1 horario' : `${count} horarios`);

/**
 * A business's booking page (/m/:slug/agendar, Fase 24D, ADR 0025), without signing in: the customer picks a service it
 * books online, a day of the week and one of its free times, and leaves its name and phone. The appointment waits «Por
 * confirmar» until the business accepts it; the receipt gives the link to follow it (/cita#link), and with a customer
 * account it is also in Mis citas. The server works out the free times (its hours, the service's duration and the
 * appointments already taken) and says no when the time was taken meanwhile; the page only shows them. When the business
 * says who attends (Fase 24J, ADR 0032), the customer chooses the person, or no preference, and the times are theirs.
 */
export default function PublicBookingPage() {
  const sheetTitleId = useId();
  const { slug = '' } = useParams<{ slug: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const serviceId = searchParams.get('servicio') ?? '';
  const staffParam = searchParams.get('con') ?? '';
  const queryClient = useQueryClient();
  const { customer, signedIn } = useCustomerAccount();
  const booking = useQuery({
    queryKey: ['public-booking', slug],
    queryFn: ({ signal }) => publicMenuApi.getBooking(slug, signal),
    retry: false,
    staleTime: 60_000,
  });
  const data = booking.data;
  // The title the server wrote for search engines (PublicPages.cs).
  useDocumentTitle(data ? `Agenda tu cita en ${data.businessName} · TienditApp` : null);
  const theme = data?.theme;
  useEffect(() => {
    // The page in the colors its business picked, like its menu.
    if (theme === undefined) return undefined;
    previewBusinessTheme(theme);
    return () => previewBusinessTheme(undefined);
  }, [theme]);
  const service = data?.services.find((item) => item.id === serviceId) ?? null;
  // Who does the service, when the business says who attends; the person chosen among them, or none (no preference).
  const people = service ? (data?.staff ?? []).filter((person) => service.staffIds.includes(person.id)) : [];
  const person = people.find((item) => item.id === staffParam) ?? null;
  const staffId = person?.id ?? '';

  // The weeks visited for the service: the first starts today, each next one where the server said.
  const [weeks, setWeeks] = useState<string[]>([]);
  const [day, setDay] = useState<string | null>(null);
  const [chosen, setChosen] = useState<ChosenTime | null>(null);
  const [notice, setNotice] = useState('');
  // What is being picked: the service and the person the page still offers (the link may name ones it no longer does).
  const picking = `${service?.id ?? ''}|${staffId}`;
  const [pickedFor, setPickedFor] = useState(picking);
  if (pickedFor !== picking) {
    setPickedFor(picking);
    setWeeks([]);
    setDay(null);
    setChosen(null);
    setNotice('');
  }
  const from = weeks[weeks.length - 1];
  const slots = useQuery({
    queryKey: ['public-booking-slots', slug, serviceId, staffId, from ?? ''],
    queryFn: ({ signal }) => publicMenuApi.getBookingSlots(slug, serviceId, from, staffId || undefined, signal),
    enabled: service !== null,
    retry: false,
  });
  const days = slots.data?.days ?? [];
  // The day picked while it has free times; otherwise the first of the week that has.
  const shownDay = days.find((item) => item.date === day && item.slots.length > 0) ?? days.find((item) => item.slots.length > 0) ?? null;
  const { ref: daysRef, fadeClass: daysFade } = useScrollFade<HTMLDivElement>(days.length);
  const { ref: peopleRef, fadeClass: peopleFade } = useScrollFade<HTMLDivElement>(people.length);
  // The times are not found for the person chosen: they stopped taking the service, or the service stopped being booked.
  const personGone = staffId !== '' && slots.isError && getApiErrorStatus(slots.error) === 404;
  const anyoneRef = useRef<HTMLButtonElement>(null);

  const [sheetOpen, setSheetOpen] = useState(false);
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [notes, setNotes] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');
  const [receipt, setReceipt] = useState<PublicAppointmentReceipt | null>(null);
  const [prefilledFor, setPrefilledFor] = useState<string | null>(null);
  // The key of the request being sent: the same request sent again (an answer lost) keeps it, and the server books it once.
  const attempt = useRef<{ signature: string; key: string } | null>(null);

  // The customer account's name and phone, written once its session is known; they can still be changed.
  if (customer && prefilledFor !== customer.id) {
    setPrefilledFor(customer.id);
    setName((current) => current || `${customer.firstName} ${customer.lastName}`.trim());
    setPhone((current) => current || localPhone(customer.phoneNumber));
  }

  const chooseService = (id: string | null) => setSearchParams(id ? { servicio: id } : {});
  const choosePerson = (id: string | null) => setSearchParams(id ? { servicio: serviceId, con: id } : { servicio: serviceId });

  const changeWeek = (next: string | null) => {
    setWeeks((current) => (next ? [...current, next] : current.slice(0, -1)));
    setDay(null);
    setChosen(null);
  };

  const send = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!service || !chosen || sending) return;
    setSending(true);
    setError('');
    const request: RequestPublicAppointment = {
      serviceProductId: service.id,
      startAt: chosen.slot.startAt,
      customerName: name.trim(),
      customerPhone: phone.trim(),
      notes: notes.trim() || null,
      staffId: staffId || null,
    };
    // Another request (another time, name or phone) gets another key.
    const signature = JSON.stringify(request);
    if (attempt.current?.signature !== signature) attempt.current = { signature, key: newUuid() };
    try {
      const result = await publicMenuApi.requestAppointment(slug, { ...request, idempotencyKey: attempt.current.key });
      attempt.current = null;
      setSheetOpen(false);
      setReceipt(result);
      void queryClient.invalidateQueries({ queryKey: ['public-booking-slots', slug] });
      if (result.linkedToAccount) void queryClient.invalidateQueries({ queryKey: ['me', 'appointments'] });
    } catch (err: unknown) {
      const message = getApiErrorMessage(err, 'No se pudo enviar tu solicitud. Intenta de nuevo.');
      // The answer was lost: the request may be stored and its time no longer free. The sheet stays with the same key, so
      // sending it again answers the one stored instead of booking another.
      if (answerLost(err)) {
        setError(`${message} Si ya se había enviado, no se repite.`);
        return;
      }
      // The person no longer takes it: the page reads who does, and the customer picks again; with nobody doing the
      // service any more, the customer picks another one.
      if (staffId) {
        const page = await booking.refetch();
        const fresh = page.data?.services.find((item) => item.id === service.id);
        if (!fresh?.staffIds.includes(staffId)) {
          // What changing the person or the service resets, here, so the message stays on top.
          setSheetOpen(false);
          setPickedFor(fresh ? `${service.id}|` : '|');
          setWeeks([]);
          setDay(null);
          setChosen(null);
          if (fresh) choosePerson(null);
          else chooseService(null);
          setNotice(message);
          return;
        }
      }
      // Taken meanwhile: the times are read again, and the customer picks another one.
      const fresh = await slots.refetch();
      const stillFree = fresh.data?.days.some((item) => item.slots.some((slot) => slot.startAt === chosen.slot.startAt));
      if (stillFree) {
        setError(message);
      } else {
        setSheetOpen(false);
        setChosen(null);
        setNotice(message);
      }
    } finally {
      setSending(false);
    }
  };

  if (booking.isPending) {
    return <div className="pm-page"><p className="text-muted p-4" aria-busy="true">Cargando…</p></div>;
  }
  if (booking.isError || !data) {
    return (
      <div className="pm-page">
        <div className="pm-empty">
          <i className="ri-calendar-close-line" aria-hidden="true"></i>
          <h1 className="h5">Este negocio no agenda citas en línea</h1>
          <p className="text-muted">Mira su menú o escríbele para agendar.</p>
          <Link className="btn btn-light" to={`/m/${slug}`}>Ver el menú</Link>
        </div>
      </div>
    );
  }

  if (receipt) {
    const followUp = businessWhatsAppUrl(
      receipt.contactPhone,
      `Hola, pedí una cita de ${receipt.serviceName}${receipt.staffName ? ` con ${receipt.staffName}` : ''} el ${receipt.dateLabel} `
        + `a las ${receipt.timeLabel} en su menú en línea.`,
    );
    return (
      <div className="pm-page">
        <div className="pm-empty">
          <i className="ri-checkbox-circle-fill text-success" aria-hidden="true"></i>
          <h1 className="h4">¡Solicitud enviada!</h1>
          <p className="mb-1"><strong>{receipt.serviceName}</strong></p>
          <p className="mb-1">{upperFirst(receipt.dateLabel)} · {receipt.timeLabel}</p>
          {receipt.staffName && <p className="mb-1">Te atiende {receipt.staffName}</p>}
          {receipt.address && <p className="small text-muted mb-1">{receipt.address}</p>}
          <p className="text-muted">{receipt.businessName} te confirma por WhatsApp. Mientras, tu cita queda por confirmar.</p>
          {receipt.linkedToAccount
            ? <p className="small mb-0">La encuentras en tus citas.</p>
            : <p className="small mb-0">Guarda el enlace de tu cita para verla o cancelarla.</p>}
          <div className="d-grid gap-2 mt-3 pb-receipt-actions">
            <Link className="btn btn-primary btn-touch" to={`/cita#${receipt.trackingToken}`}>
              <i className="ri-calendar-event-line me-1" aria-hidden="true"></i>Ver tu cita
            </Link>
            {/* A guest keeps the link only if it saves it: an account finds the appointment in Mis citas. */}
            {!receipt.linkedToAccount && <CopyTrackingLink token={receipt.trackingToken} path="/cita" />}
            {receipt.linkedToAccount && <Link className="btn btn-light btn-touch" to="/mis-citas">Ver mis citas</Link>}
            {followUp && (
              <a className="btn btn-soft-success btn-touch" href={followUp} target="_blank" rel="noopener noreferrer">
                <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Avisar por WhatsApp
              </a>
            )}
            <Link className="btn btn-light btn-touch" to={`/m/${slug}`}>Volver al menú</Link>
          </div>
          {!signedIn && (
            <p className="small text-muted mt-3 mb-0">
              ¿Agendas seguido? <Link to="/register?cuenta=cliente">Crea tu cuenta</Link> y tus citas quedan juntas.
            </p>
          )}
        </div>
      </div>
    );
  }

  const withPhotos = data.services.some((item) => item.imageUrl);
  const noticeAlert = notice && (
    <div className="alert alert-warning d-flex align-items-start gap-2 mt-3 mb-0" role="status">
      <span className="flex-grow-1">{notice}</span>
      <button type="button" className="btn-close" aria-label="Cerrar aviso" onClick={() => setNotice('')}></button>
    </div>
  );

  return (
    <div className="pm-page">
      <header className="pm-bar">
        {/* In place of this page: back on the menu, its arrow goes where the customer came from, not here. */}
        <Link className="pm-round" to={`/m/${slug}`} replace aria-label="Ver menú">
          <i className="ri-arrow-left-line" aria-hidden="true"></i>
        </Link>
        <PhotoOrInitial src={data.logoUrl} name={data.businessName} className="pm-bar-logo" loading="eager" />
        <div className="min-w-0">
          <h1 className="pm-bar-name">{data.businessName}</h1>
          <div className="small text-muted">Agenda tu cita</div>
        </div>
      </header>

      {!service ? (
        <main className="pm-main pb-main">
          {noticeAlert}
          <h2 className="pm-section-title">Elige el servicio</h2>
          <div className="pm-items">
            {data.services.map((item) => (
              <button key={item.id} type="button" className="pm-item pb-service" onClick={() => chooseService(item.id)}>
                <span className="pm-item-text">
                  <span className="pm-item-name d-block">{item.name}</span>
                  <span className="pm-price-row">
                    <span className="text-muted">{describeDuration(item.durationMinutes)} ·</span>
                    <span className="pm-price ta-money">{formatMoney(item.price)}</span>
                  </span>
                  {item.description && <span className="pm-item-description">{item.description}</span>}
                </span>
                {withPhotos
                  ? <PhotoOrInitial src={item.imageUrl} name={item.name} className="pm-item-photo" />
                  : <i className="ri-arrow-right-s-line fs-4 text-muted align-self-center" aria-hidden="true"></i>}
              </button>
            ))}
          </div>
          {data.services.length === 0 && (
            <p className="text-muted text-center py-5">Este negocio todavía no tiene servicios para agendar en línea.</p>
          )}
          {data.address && (
            <p className="small text-muted mt-3"><i className="ri-map-pin-line me-1" aria-hidden="true"></i>Las citas son en {data.address}.</p>
          )}
        </main>
      ) : (
        <main className="pm-main pb-main">
          <div className="pb-chosen">
            <div className="min-w-0">
              <div className="fw-semibold">{service.name}</div>
              <div className="small text-muted">
                {describeDuration(service.durationMinutes)} · <span className="ta-money">{formatMoney(service.price)}</span>
              </div>
            </div>
            <button type="button" className="btn btn-sm btn-light text-nowrap" onClick={() => chooseService(null)} aria-label="Cambiar servicio">
              Cambiar
            </button>
          </div>

          {noticeAlert}

          {people.length > 0 && (
            <>
              <h2 className="pm-section-title pb-step">¿Con quién?</h2>
              <div ref={peopleRef} className={`pb-people ta-scroll-fade ${peopleFade}`} role="group" aria-label="Quién te atiende">
                <button
                  ref={anyoneRef}
                  type="button"
                  className={`btn pb-person ${person === null ? 'btn-primary' : 'btn-light'}`}
                  aria-pressed={person === null}
                  onClick={() => choosePerson(null)}
                >
                  <span className="d-block fw-semibold">Sin preferencia</span>
                  <span className="d-block small">Quien esté libre</span>
                </button>
                {people.map((item) => {
                  const active = person?.id === item.id;
                  return (
                    <button
                      key={item.id}
                      type="button"
                      className={`btn pb-person ${active ? 'btn-primary' : 'btn-light'}`}
                      aria-pressed={active}
                      onClick={() => choosePerson(item.id)}
                    >
                      <PhotoOrInitial name={item.name} className="pb-person-initial" />
                      <span className="d-block fw-semibold text-truncate pb-person-name">{item.name}</span>
                    </button>
                  );
                })}
              </div>
            </>
          )}

          <h2 className="pm-section-title pb-step">Elige el día</h2>

          {slots.isPending && <p className="text-muted" aria-busy="true">Buscando horarios libres…</p>}
          {personGone && (
            <div className="alert alert-warning" role="alert">
              Ya no se agenda este servicio con {person?.name} en línea.{' '}
              <button
                type="button"
                className="btn btn-link btn-sm p-0 align-baseline"
                onClick={() => {
                  choosePerson(null);
                  void booking.refetch();
                  // This button goes away with the person: the choice it made keeps the focus.
                  anyoneRef.current?.focus();
                }}
              >
                Ver con quien esté libre
              </button>
            </div>
          )}
          {slots.isError && !personGone && (
            <div className="alert alert-warning" role="alert">
              {getApiErrorMessage(slots.error, 'No pudimos cargar los horarios.')}{' '}
              <button type="button" className="btn btn-link btn-sm p-0 align-baseline" onClick={() => void slots.refetch()}>Intentar de nuevo</button>
            </div>
          )}

          {slots.data && (
            <>
              <div ref={daysRef} className={`pb-days ta-scroll-fade ${daysFade}`} role="group" aria-label="Días de la semana">
                {days.map((item) => {
                  const active = shownDay?.date === item.date;
                  return (
                    <button
                      key={item.date}
                      type="button"
                      className={`btn pb-day ${active ? 'btn-primary' : 'btn-light'}`}
                      aria-pressed={active}
                      disabled={item.slots.length === 0}
                      onClick={() => setDay(item.date)}
                    >
                      {/* Its name says what shows, and the whole date for a screen reader. */}
                      <span className="d-block fw-semibold">{shortDayLabel(item.date)}</span>
                      <span className="d-block small">{item.slots.length > 0 ? horarios(item.slots.length) : 'Sin horarios'}</span>
                      <span className="visually-hidden">, {item.label}</span>
                    </button>
                  );
                })}
              </div>
              <div className="pb-weeks">
                <button type="button" className="btn btn-sm btn-light" disabled={weeks.length === 0 || slots.isFetching} onClick={() => changeWeek(null)}>
                  <i className="ri-arrow-left-s-line" aria-hidden="true"></i>Semana anterior
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-light"
                  disabled={!slots.data?.nextFrom || slots.isFetching}
                  onClick={() => changeWeek(slots.data?.nextFrom ?? null)}
                >
                  Semana siguiente<i className="ri-arrow-right-s-line" aria-hidden="true"></i>
                </button>
              </div>

              {shownDay ? (
                <>
                  <h2 className="pm-section-title pb-step">Elige la hora · {shownDay.label}</h2>
                  <div className="pb-slots" role="group" aria-label={`Horarios libres del ${shownDay.label}`}>
                    {shownDay.slots.map((slot) => {
                      const active = chosen?.slot.startAt === slot.startAt;
                      return (
                        <button
                          key={slot.startAt}
                          type="button"
                          className={`btn ${active ? 'btn-primary' : 'btn-light'}`}
                          aria-pressed={active}
                          onClick={() => {
                            setChosen({ slot });
                            setNotice('');
                          }}
                        >
                          {slot.label}
                          {/* After midnight, in the opening of the day before: it says its own day. */}
                          {slot.date !== shownDay.date && ` · ${shortDayLabel(slot.date)}`}
                        </button>
                      );
                    })}
                  </div>
                </>
              ) : (
                <p className="text-muted text-center py-4 mb-0">
                  {days.length === 0 ? 'No hay más días para agendar en línea.' : 'No hay horarios libres esta semana.'}
                  {slots.data.nextFrom && ' Mira la semana siguiente.'}
                </p>
              )}
            </>
          )}
        </main>
      )}

      {service && chosen && (
        <div className="pm-cart-bar">
          <button type="button" className="btn btn-primary pm-cart-button justify-content-center" onClick={() => setSheetOpen(true)}>
            Continuar · {shortDayLabel(chosen.slot.date)} · {chosen.slot.label}
          </button>
        </div>
      )}

      <Offcanvas show={sheetOpen && !!chosen} onHide={() => setSheetOpen(false)} placement="bottom" className="pm-sheet" aria-labelledby={sheetTitleId}>
        <Offcanvas.Header closeButton>
          <Offcanvas.Title id={sheetTitleId} as="h2" className="h5">Tu cita</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>
          {service && chosen && (
            <form onSubmit={(event) => void send(event)}>
              <p className="mb-3">
                <strong>{service.name}</strong>
                <span className="d-block">{upperFirst(chosen.slot.dateLabel)} · {chosen.slot.label}</span>
                {people.length > 0 && <span className="d-block">{person ? `Con ${person.name}` : 'Con quien esté libre'}</span>}
                {data.address && <span className="d-block small text-muted">{data.address}</span>}
              </p>
              <div className="row g-2">
                <div className="col-sm-6">
                  <label className="form-label small mb-1" htmlFor="pb-name">Tu nombre</label>
                  <input id="pb-name" className="form-control" required maxLength={100} autoComplete="name"
                    value={name} onChange={(event) => setName(event.target.value)} />
                </div>
                <div className="col-sm-6">
                  <label className="form-label small mb-1" htmlFor="pb-phone">Teléfono (WhatsApp)</label>
                  <input id="pb-phone" className="form-control" required type="tel" inputMode="tel" maxLength={30} autoComplete="tel"
                    value={phone} onChange={(event) => setPhone(event.target.value)} />
                </div>
                <div className="col-12">
                  <label className="form-label small mb-1" htmlFor="pb-notes">Notas (opcional)</label>
                  <input id="pb-notes" className="form-control" maxLength={500} placeholder="Ej. es la primera vez que voy"
                    value={notes} onChange={(event) => setNotes(event.target.value)} />
                </div>
              </div>
              {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}
              <button type="submit" className="btn btn-primary btn-touch btn-touch-lg w-100 mt-3" disabled={sending}>
                {sending ? 'Enviando…' : 'Pedir cita'}
              </button>
              <p className="small text-muted mt-2 mb-0">
                {data.businessName} te confirma por WhatsApp. Usamos tu teléfono solo para avisarte de esta cita.
              </p>
            </form>
          )}
        </Offcanvas.Body>
      </Offcanvas>
    </div>
  );
}
