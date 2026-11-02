import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Offcanvas } from 'react-bootstrap';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, type AccountAddress } from '../../api/accountApi';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import {
  publicMenuApi,
  type PublicAppointmentReceipt,
  type PublicBookingSlot,
  type RequestPublicHomeVisit,
} from '../../api/publicMenuApi';
import { ProblemPhotosPicker, ProblemPhotosSender } from '../../components/booking/ProblemPhotos';
import SlotPicker from '../../components/booking/SlotPicker';
import StaffPicker from '../../components/booking/StaffPicker';
import PhotoOrInitial from '../../components/common/PhotoOrInitial';
import CopyTrackingLink from '../../components/tracking/CopyTrackingLink';
import { useCustomerAccount } from '../../hooks/useCustomerAccount';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { describeAccountAddress } from '../../utils/accountAddresses';
import { visitPolicyNote, visitPriceText } from '../../utils/businessCategories';
import { ADDRESS_LINE_MAX_LENGTH, REFERENCES_MAX_LENGTH, normalizePostalCode } from '../../utils/homeDelivery';
import { describeDuration, shortDayLabel, upperFirst } from '../../utils/onlineBooking';
import { readPostalCode } from '../../utils/postalCodePreference';
import { isPostalCode } from '../../utils/postalCodes';
import { localPhone } from '../../utils/phones';
import { previewBusinessTheme } from '../../utils/theme';
import type { PickedPhoto } from '../../utils/problemPhotos';
import { newUuid } from '../../utils/uuid';
import { businessWhatsAppUrl } from '../../utils/whatsApp';
import './PublicMenuPage.css';
import './PublicBookingPage.css';

const PROBLEM_MAX_LENGTH = 500;

/** The server's words when the business changed the visit's price or policy after the page showed it. */
const VISIT_CHANGED_MESSAGE = 'El negocio cambió el costo de la visita o cómo la cobra. Revísalo antes de enviar tu solicitud.';

interface VisitAddress {
  postalCode: string;
  addressLine: string;
  references: string;
}

/** No answer, or the server failed: the request may have been stored, and the same one goes again with its key. */
const answerLost = (err: unknown) => {
  const status = getApiErrorStatus(err);
  return status === undefined || status >= 500;
};

/**
 * The request page of a visit to quote (/m/:slug/visita, Fase 29B, ADR 0053 point 11), without signing in: the customer
 * writes its postal code (the server says whether the business goes there and what the visit costs there), picks a day
 * and one of the visit's free times (with the trip around each visit already left free) and, when the business says who
 * goes, the person; then says what happens, where and how to reach them. The visit waits «Por confirmar» until the
 * business accepts it; the receipt gives its link (/cita#link), and with a customer account it is also in Mis citas.
 */
export default function PublicHomeVisitPage() {
  const sheetTitleId = useId();
  const fieldId = useId();
  const { slug = '' } = useParams<{ slug: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const staffParam = searchParams.get('con') ?? '';
  const queryClient = useQueryClient();
  const { customer, signedIn } = useCustomerAccount();
  const visitQuery = useQuery({
    queryKey: ['public-home-visit', slug],
    queryFn: ({ signal }) => publicMenuApi.getHomeVisit(slug, signal),
    retry: false,
    staleTime: 60_000,
  });
  const data = visitQuery.data;
  useDocumentTitle(data ? `Solicita una visita de ${data.businessName} · TienditApp` : null);
  const theme = data?.theme;
  useEffect(() => {
    // The page in the colors its business picked, like its store.
    if (theme === undefined) return undefined;
    previewBusinessTheme(theme);
    return () => previewBusinessTheme(undefined);
  }, [theme]);
  const people = data?.staff ?? [];
  const person = people.find((item) => item.id === staffParam) ?? null;
  const staffId = person?.id ?? '';

  // Where the visit is: one of the account's addresses, or the one written. The postal code says if the business goes.
  const addresses = useQuery({
    queryKey: ['me', 'addresses'],
    queryFn: ({ signal }) => accountApi.getAddresses(signal),
    enabled: !!customer,
  });
  const savedAddresses = useMemo(() => (customer ? addresses.data ?? [] : []), [customer, addresses.data]);
  const [addressChoice, setAddressChoice] = useState<string | null>(null);
  const [address, setAddress] = useState<VisitAddress>({ postalCode: '', addressLine: '', references: '' });
  // The customer picked or typed where: the page no longer chooses for them.
  const [addressTouched, setAddressTouched] = useState(false);
  const applyAddress = (saved: AccountAddress | null) => {
    setAddressChoice(saved?.id ?? null);
    setAddress(saved
      ? { postalCode: saved.postalCode, addressLine: saved.addressLine, references: saved.references ?? '' }
      : { postalCode: readPostalCode(), addressLine: '', references: '' });
  };
  const chooseAddress = (saved: AccountAddress | null) => {
    setAddressTouched(true);
    applyAddress(saved);
  };
  // Where it starts, for a visitor and again once the account's session and addresses arrive (the session is read after
  // the page opens): the account's last address used, or the postal code given in Explorar; never over what was typed.
  const [addressDefaultFor, setAddressDefaultFor] = useState<string | null>(null);
  const addressOwner = customer?.id ?? '';
  if (addressDefaultFor !== addressOwner && (!customer || !addresses.isPending)) {
    setAddressDefaultFor(addressOwner);
    if (!addressTouched) {
      if (savedAddresses[0]) {
        applyAddress(savedAddresses[0]);
      } else {
        const remembered = readPostalCode();
        if (remembered) setAddress((current) => ({ ...current, postalCode: current.postalCode || remembered }));
      }
    }
  }
  const usingSaved = savedAddresses.some((saved) => saved.id === addressChoice);
  const postalCode = normalizePostalCode(address.postalCode);
  const coverage = useQuery({
    queryKey: ['public-home-visit-coverage', slug, postalCode],
    queryFn: ({ signal }) => publicMenuApi.getHomeVisitCoverage(slug, postalCode, signal),
    enabled: !!data && isPostalCode(postalCode),
    retry: false,
    staleTime: 60_000,
  });
  const covered = isPostalCode(postalCode) && coverage.data?.postalCode === postalCode && coverage.data.covered;
  const coveredPrice = covered ? { amount: coverage.data?.price ?? 0, isStarting: false } : null;
  const price = coveredPrice && data ? visitPriceText(coveredPrice, data.chargePolicy, data.pricesIncludeTax) : null;

  // The weeks visited: the first starts today, each next one where the server said.
  const [weeks, setWeeks] = useState<string[]>([]);
  const [day, setDay] = useState<string | null>(null);
  const [chosen, setChosen] = useState<PublicBookingSlot | null>(null);
  const [notice, setNotice] = useState('');
  // Another person: the times are theirs.
  const [pickedFor, setPickedFor] = useState(staffId);
  if (pickedFor !== staffId) {
    setPickedFor(staffId);
    setWeeks([]);
    setDay(null);
    setChosen(null);
    setNotice('');
  }
  const from = weeks[weeks.length - 1];
  const slots = useQuery({
    queryKey: ['public-home-visit-slots', slug, staffId, from ?? ''],
    queryFn: ({ signal }) => publicMenuApi.getHomeVisitSlots(slug, from, staffId || undefined, signal),
    enabled: covered,
    retry: false,
  });
  // The person chosen no longer goes to visits online.
  const personGone = staffId !== '' && slots.isError && getApiErrorStatus(slots.error) === 404;
  const anyoneRef = useRef<HTMLButtonElement>(null);

  const [sheetOpen, setSheetOpen] = useState(false);
  const [problem, setProblem] = useState('');
  // The photos of what happens (Fase 29H): picked here, sent with the link once the request is saved.
  const [photos, setPhotos] = useState<PickedPhoto[]>([]);
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [email, setEmail] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');
  const [receipt, setReceipt] = useState<PublicAppointmentReceipt | null>(null);
  const [prefilledFor, setPrefilledFor] = useState<string | null>(null);
  // The key of the request being sent: the same request sent again (an answer lost) keeps it, and the server stores it once.
  const attempt = useRef<{ signature: string; key: string } | null>(null);

  // The customer account's name, phone and email, written once its session is known; they can still be changed.
  if (customer && prefilledFor !== customer.id) {
    setPrefilledFor(customer.id);
    setName((current) => current || `${customer.firstName} ${customer.lastName}`.trim());
    setPhone((current) => current || localPhone(customer.phoneNumber));
    setEmail((current) => current || customer.email || '');
  }

  const choosePerson = (id: string | null) => setSearchParams(id ? { con: id } : {});
  const changeWeek = (next: string | null) => {
    setWeeks((current) => (next ? [...current, next] : current.slice(0, -1)));
    setDay(null);
    setChosen(null);
  };

  const send = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!chosen || sending) return;
    setSending(true);
    setError('');
    const request: RequestPublicHomeVisit = {
      startAt: chosen.startAt,
      postalCode,
      addressLine: address.addressLine.trim(),
      references: address.references.trim() || null,
      problem: problem.trim(),
      customerName: name.trim(),
      customerPhone: phone.trim(),
      customerEmail: email.trim() || null,
      staffId: staffId || null,
      // What the page said: a visit charged otherwise since is asked again, never stored.
      expectedChargePolicy: data?.chargePolicy ?? HomeVisitChargePolicy.Charged,
      expectedPrice: coverage.data?.price ?? 0,
    };
    // Another request (another time, place, name or phone) gets another key.
    const signature = JSON.stringify(request);
    if (attempt.current?.signature !== signature) attempt.current = { signature, key: newUuid() };
    try {
      const result = await publicMenuApi.requestHomeVisit(slug, { ...request, idempotencyKey: attempt.current.key });
      attempt.current = null;
      setSheetOpen(false);
      setReceipt(result);
      void queryClient.invalidateQueries({ queryKey: ['public-home-visit-slots', slug] });
      if (result.linkedToAccount) void queryClient.invalidateQueries({ queryKey: ['me', 'appointments'] });
    } catch (err: unknown) {
      const message = getApiErrorMessage(err, 'No se pudo enviar tu solicitud. Intenta de nuevo.');
      // The answer was lost: the request may be stored and its time no longer free. The sheet stays with the same key, so
      // sending it again answers the one stored instead of storing another.
      if (answerLost(err)) {
        setError(`${message} Si ya se había enviado, no se repite.`);
        return;
      }
      // The business changed what the visit costs or how it charges it: the page reads both again and the customer sees them.
      if (message === VISIT_CHANGED_MESSAGE) {
        setSheetOpen(false);
        setNotice(message);
        void visitQuery.refetch();
        void coverage.refetch();
        return;
      }
      // The person no longer goes: the page reads who does, and the customer picks again.
      if (staffId) {
        const page = await visitQuery.refetch();
        if (!page.data?.staff.some((item) => item.id === staffId)) {
          setSheetOpen(false);
          setPickedFor('');
          setWeeks([]);
          setDay(null);
          setChosen(null);
          choosePerson(null);
          setNotice(message);
          return;
        }
      }
      // Taken meanwhile: the times are read again, and the customer picks another one.
      const fresh = await slots.refetch();
      const stillFree = fresh.data?.days.some((item) => item.slots.some((slot) => slot.startAt === chosen.startAt));
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

  if (visitQuery.isPending) {
    return <div className="pm-page"><p className="text-muted p-4" aria-busy="true">Cargando…</p></div>;
  }
  if (visitQuery.isError || !data) {
    return (
      <div className="pm-page">
        <div className="pm-empty">
          <i className="ri-home-gear-line" aria-hidden="true"></i>
          <h1 className="h5">Este negocio no recibe visitas en línea</h1>
          <p className="text-muted">Mira su tienda o escríbele para pedir una visita.</p>
          <Link className="btn btn-light" to={`/m/${slug}`}>Ver la tienda</Link>
        </div>
      </div>
    );
  }

  if (receipt) {
    const followUp = businessWhatsAppUrl(
      receipt.contactPhone,
      `Hola, pedí una visita a domicilio${receipt.staffName ? ` con ${receipt.staffName}` : ''} el ${receipt.dateLabel} `
        + `a las ${receipt.timeLabel} en su tienda en línea.`,
    );
    return (
      <div className="pm-page">
        <div className="pm-empty">
          <i className="ri-checkbox-circle-fill text-success" aria-hidden="true"></i>
          <h1 className="h4">¡Solicitud enviada!</h1>
          <p className="mb-1"><strong>{receipt.serviceName}</strong></p>
          <p className="mb-1">{upperFirst(receipt.dateLabel)} · {receipt.timeLabel}</p>
          {receipt.staffName && <p className="mb-1">Va {receipt.staffName}</p>}
          {receipt.visit && <p className="small text-muted mb-1">A domicilio · CP {receipt.visit.postalCode}</p>}
          {price && <p className="small text-muted mb-1">{price}</p>}
          <p className="text-muted">{receipt.businessName} te confirma por WhatsApp. Mientras, tu visita queda por confirmar.</p>
          {receipt.linkedToAccount
            ? <p className="small mb-0">La encuentras en tus citas.</p>
            : <p className="small mb-0">Guarda el enlace de tu visita para verla o cancelarla.</p>}
          {photos.length > 0 && (
            <section className="text-start w-100 mt-3" aria-labelledby={`${fieldId}-photos`}>
              <h2 id={`${fieldId}-photos`} className="h6">Tus fotos de lo que pasa</h2>
              <ProblemPhotosSender token={receipt.trackingToken} sent={0} canAdd initial={photos} onSent={() => undefined} />
            </section>
          )}
          <div className="d-grid gap-2 mt-3 pb-receipt-actions">
            <Link className="btn btn-primary btn-touch" to={`/cita#${receipt.trackingToken}`}>
              <i className="ri-calendar-event-line me-1" aria-hidden="true"></i>Ver tu visita
            </Link>
            {/* A guest keeps the link only if it saves it: an account finds the visit in Mis citas. */}
            {!receipt.linkedToAccount && <CopyTrackingLink token={receipt.trackingToken} path="/cita" />}
            {receipt.linkedToAccount && <Link className="btn btn-light btn-touch" to="/mis-citas">Ver mis citas</Link>}
            {followUp && (
              <a className="btn btn-soft-success btn-touch" href={followUp} target="_blank" rel="noopener noreferrer">
                <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Avisar por WhatsApp
              </a>
            )}
            <Link className="btn btn-light btn-touch" to={`/m/${slug}`}>Volver a la tienda</Link>
          </div>
          {!signedIn && (
            <p className="small text-muted mt-3 mb-0">
              ¿Pides servicios seguido? <Link to="/register?cuenta=cliente">Crea tu cuenta</Link> y tus citas quedan juntas.
            </p>
          )}
        </div>
      </div>
    );
  }

  const noticeAlert = notice && (
    <div className="alert alert-warning d-flex align-items-start gap-2 mt-3 mb-0" role="status">
      <span className="flex-grow-1">{notice}</span>
      <button type="button" className="btn-close" aria-label="Cerrar aviso" onClick={() => setNotice('')}></button>
    </div>
  );
  const startingPrice = visitPriceText(data.price, data.chargePolicy, data.pricesIncludeTax);
  const notGoing = !!coverage.data && !covered && coverage.data.postalCode === postalCode;
  const coverageText = !isPostalCode(postalCode) ? 'Escribe tu código postal para ver si vamos y cuánto cuesta la visita.'
    : coverage.isFetching && coverage.data?.postalCode !== postalCode ? 'Revisando si vamos…'
    : coverage.isError ? getApiErrorMessage(coverage.error, 'No pudimos revisar tu código postal.')
    : covered ? `Vamos al ${postalCode}${price ? ` · ${price}` : ''}`
    : notGoing ? `Por ahora no vamos al ${postalCode}.`
    : '';

  return (
    <div className="pm-page">
      <header className="pm-bar">
        {/* In place of this page: back on the store, its arrow goes where the customer came from, not here. */}
        <Link className="pm-round" to={`/m/${slug}`} replace aria-label="Ver la tienda">
          <i className="ri-arrow-left-line" aria-hidden="true"></i>
        </Link>
        <PhotoOrInitial src={data.logoUrl} name={data.businessName} className="pm-bar-logo" loading="eager" />
        <div className="min-w-0">
          <h1 className="pm-bar-name">{data.businessName}</h1>
          <div className="small text-muted">Solicita una visita a domicilio</div>
        </div>
      </header>

      <main className="pm-main pb-main">
        <div className="pb-chosen">
          <div className="min-w-0">
            <div className="fw-semibold">{data.visitName}</div>
            <div className="small text-muted">
              {describeDuration(data.durationMinutes)}{startingPrice && <> · <span className="ta-money">{startingPrice}</span></>}
            </div>
            <div className="small text-muted">{visitPolicyNote(coveredPrice ?? data.price, data.chargePolicy)}</div>
          </div>
        </div>

        {noticeAlert}

        <h2 className="pm-section-title pb-step" id={`${fieldId}-where`}>¿A dónde vamos?</h2>
        {savedAddresses.length > 0 && (
          <div className="pm-addresses mb-2" role="radiogroup" aria-labelledby={`${fieldId}-where`}>
            {savedAddresses.map((saved) => (
              <button
                key={saved.id}
                type="button"
                role="radio"
                aria-checked={saved.id === addressChoice}
                className={`pm-address-option ${saved.id === addressChoice ? 'active' : ''}`}
                onClick={() => chooseAddress(saved)}
              >
                <span className="fw-semibold">{saved.label}</span>
                <span className="small text-muted">{describeAccountAddress(saved)}</span>
              </button>
            ))}
            <button
              type="button"
              role="radio"
              aria-checked={!usingSaved}
              className={`pm-address-option ${usingSaved ? '' : 'active'}`}
              onClick={() => chooseAddress(null)}
            >
              <span className="fw-semibold">Otra dirección</span>
            </button>
          </div>
        )}
        <div className="row g-2">
          {!usingSaved && (
            <div className="col-6 col-sm-4">
              <label className="form-label small mb-1" htmlFor={`${fieldId}-postal-code`}>Código postal</label>
              <input
                id={`${fieldId}-postal-code`}
                className={`form-control ${notGoing ? 'is-invalid' : ''}`}
                aria-invalid={notGoing}
                inputMode="numeric"
                autoComplete="postal-code"
                maxLength={5}
                pattern="\d{5}"
                value={address.postalCode}
                onChange={(event) => {
                  setAddressTouched(true);
                  setAddress((current) => ({ ...current, postalCode: event.target.value.replace(/\D/g, '') }));
                }}
                aria-describedby={`${fieldId}-coverage`}
              />
            </div>
          )}
          <div id={`${fieldId}-coverage`} className="col-12 small" role="status">
            <span className={notGoing ? 'text-danger' : 'text-muted'}>{coverageText}</span>
            {coverage.isError && !coverage.isFetching && (
              <>
                {' '}
                <button type="button" className="btn btn-link btn-sm p-0 align-baseline" onClick={() => void coverage.refetch()}>Intentar de nuevo</button>
              </>
            )}
          </div>
        </div>

        {covered && (
          <>
            {people.length > 0 && (
              <StaffPicker people={people} personId={person?.id ?? null} onChoose={choosePerson} anyoneRef={anyoneRef} label="Quién va" />
            )}

            <h2 className="pm-section-title pb-step">Elige el día</h2>

            {slots.isPending && <p className="text-muted" aria-busy="true">Buscando horarios libres…</p>}
            {personGone && (
              <div className="alert alert-warning" role="alert">
                {person?.name ?? 'Esa persona'} ya no hace visitas en línea.{' '}
                <button
                  type="button"
                  className="btn btn-link btn-sm p-0 align-baseline"
                  onClick={() => {
                    choosePerson(null);
                    void visitQuery.refetch();
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
              <SlotPicker
                week={slots.data}
                fetching={slots.isFetching}
                canGoBack={weeks.length > 0}
                onWeekChange={changeWeek}
                day={day}
                onDayChange={setDay}
                chosen={chosen?.startAt ?? null}
                onChoose={(slot) => {
                  setChosen(slot);
                  setNotice('');
                }}
                noMoreDays="No hay más días para pedir la visita en línea."
              />
            )}
          </>
        )}
      </main>

      {covered && chosen && (
        <div className="pm-cart-bar">
          <button
            type="button"
            className="btn btn-primary pm-cart-button justify-content-center"
            onClick={() => {
              setError('');
              setSheetOpen(true);
            }}
          >
            Continuar · {shortDayLabel(chosen.date)} · {chosen.label}
          </button>
        </div>
      )}

      <Offcanvas show={sheetOpen && covered && !!chosen} onHide={() => setSheetOpen(false)} placement="bottom" className="pm-sheet" aria-labelledby={sheetTitleId}>
        <Offcanvas.Header closeButton>
          <Offcanvas.Title id={sheetTitleId} as="h2" className="h5">Tu visita</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>
          {chosen && (
            <form onSubmit={(event) => void send(event)}>
              <p className="mb-3">
                <strong>{data.visitName}</strong>
                <span className="d-block">{upperFirst(chosen.dateLabel)} · {chosen.label}</span>
                {people.length > 0 && <span className="d-block">{person ? `Va ${person.name}` : 'Va quien esté libre'}</span>}
                <span className="d-block small text-muted">A domicilio · CP {postalCode}{price ? ` · ${price}` : ''}</span>
              </p>
              <div className="row g-2">
                <div className="col-12">
                  <label className="form-label small mb-1" htmlFor={`${fieldId}-problem`}>¿Qué pasa?</label>
                  <textarea id={`${fieldId}-problem`} className="form-control" required rows={3} maxLength={PROBLEM_MAX_LENGTH}
                    placeholder="Ej. gotea el calentador desde ayer"
                    value={problem} onChange={(event) => setProblem(event.target.value)} />
                </div>
                <div className="col-12">
                  <ProblemPhotosPicker photos={photos} onChange={setPhotos} businessName={data.businessName} disabled={sending} />
                </div>
                <div className="col-12">
                  <label className="form-label small mb-1" htmlFor={`${fieldId}-address`}>Dirección</label>
                  <input id={`${fieldId}-address`} className="form-control" required maxLength={ADDRESS_LINE_MAX_LENGTH}
                    autoComplete="street-address" placeholder="Calle, número y colonia"
                    value={address.addressLine} onChange={(event) => setAddress((current) => ({ ...current, addressLine: event.target.value }))} />
                </div>
                <div className="col-12">
                  <label className="form-label small mb-1" htmlFor={`${fieldId}-references`}>Referencias (opcional)</label>
                  <input id={`${fieldId}-references`} className="form-control" maxLength={REFERENCES_MAX_LENGTH}
                    placeholder="Ej. portón verde, entre qué calles"
                    value={address.references} onChange={(event) => setAddress((current) => ({ ...current, references: event.target.value }))} />
                </div>
                <div className="col-sm-6">
                  <label className="form-label small mb-1" htmlFor={`${fieldId}-name`}>Tu nombre</label>
                  <input id={`${fieldId}-name`} className="form-control" required maxLength={100} autoComplete="name"
                    value={name} onChange={(event) => setName(event.target.value)} />
                </div>
                <div className="col-sm-6">
                  <label className="form-label small mb-1" htmlFor={`${fieldId}-phone`}>Teléfono (WhatsApp)</label>
                  <input id={`${fieldId}-phone`} className="form-control" required type="tel" inputMode="tel" maxLength={30} autoComplete="tel"
                    value={phone} onChange={(event) => setPhone(event.target.value)} />
                </div>
                <div className="col-12">
                  <label className="form-label small mb-1" htmlFor={`${fieldId}-email`}>Correo (opcional)</label>
                  <input id={`${fieldId}-email`} className="form-control" type="email" maxLength={254} autoComplete="email"
                    aria-describedby={`${fieldId}-email-help`}
                    value={email} onChange={(event) => setEmail(event.target.value)} />
                  <div id={`${fieldId}-email-help`} className="form-text">Para mandarte la cotización también por correo.</div>
                </div>
              </div>
              {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}
              <button type="submit" className="btn btn-primary btn-touch btn-touch-lg w-100 mt-3" disabled={sending}>
                {sending ? 'Enviando…' : 'Solicitar visita'}
              </button>
              <p className="small text-muted mt-2 mb-0">
                {data.businessName} te confirma por WhatsApp. Usamos tus datos solo para esta visita y su cotización.
              </p>
            </form>
          )}
        </Offcanvas.Body>
      </Offcanvas>
    </div>
  );
}
