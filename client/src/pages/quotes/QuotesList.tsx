import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import ChimeSettingsButton from '../../components/common/ChimeSettingsButton';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import QuoteDepositSetting from '../../components/quotes/QuoteDepositSetting';
import { getApiErrorMessage } from '../../api/errors';
import { quotesApi } from '../../api/quotesApi';
import { useArrivalChime } from '../../hooks/useArrivalChime';
import { useAuth } from '../../hooks/useAuth';
import { QUOTES_SOUND_KEY } from '../../hooks/useQuoteReplies';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { QuoteListFilter } from '../../types';
import { getAccess } from '../../utils/access';
import { formatCalendarDate } from '../../utils/dates';
import { formatMoney } from '../../utils/money';
import { QUOTE_FILTERS, quoteFolio, quoteStatusChip, quoteStatusLabel } from '../../utils/quotes';

const COLUMNS = [
  { header: 'Folio' },
  { header: 'Cliente' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Vigencia', className: 'text-center' },
  { header: 'Total', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

interface NewQuoteForm {
  customerName: string;
  customerPhone: string;
  customerEmail: string;
}

const EMPTY_FORM: NewQuoteForm = { customerName: '', customerPhone: '', customerEmail: '' };

/** «?estado=por-convertir» opens the accepted ones still to convert: where «Requiere atención» leads (Fase 29G). */
const TO_CONVERT_LINK = 'por-convertir';

/**
 * The business's quotes (Fase 29C, ADR 0053): what it proposed after looking at each job and where each one stands. A new
 * one starts here for a customer, or from the Agenda for the visit of an appointment; each opens in its own screen.
 */
export default function QuotesList() {
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const titleId = useId();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const isAdmin = getAccess(user, capabilities).isAdmin;
  const [searchParams, setSearchParams] = useSearchParams();
  const linked = searchParams.get('estado');
  const [filter, setFilter] = useState<QuoteListFilter>(linked === TO_CONVERT_LINK ? QuoteListFilter.ToConvert : QuoteListFilter.Open);
  const onlineRepliesId = useId();
  const depositId = useId();
  const [search, setSearch] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  // The bell leads here also from this screen: a link followed again sets the filter it names.
  const [seenLink, setSeenLink] = useState(linked);
  if (seenLink !== linked) {
    setSeenLink(linked);
    if (linked === TO_CONVERT_LINK) {
      setFilter(QuoteListFilter.ToConvert);
      setPageNumber(1);
    }
  }
  const [showNew, setShowNew] = useState(false);
  const [form, setForm] = useState<NewQuoteForm>(EMPTY_FORM);
  const [formError, setFormError] = useState('');
  const [validity, setValidity] = useState<string | null>(null);
  // The template picked and not saved yet ('' for TienditApp's text); null while untouched.
  const [template, setTemplate] = useState<string | null>(null);

  const quotesQuery = useQuery({
    queryKey: ['quotes', filter, search, pageNumber],
    queryFn: ({ signal }) => quotesApi.list(filter, search, pageNumber, signal),
    placeholderData: keepPreviousData,
  });
  const settingsQuery = useQuery({ queryKey: ['quotes', 'settings'], queryFn: ({ signal }) => quotesApi.settings(signal) });

  const create = useMutation({
    mutationFn: () => quotesApi.create({
      customerName: form.customerName.trim(),
      customerPhone: form.customerPhone.trim(),
      customerEmail: form.customerEmail.trim() || null,
    }),
    onSuccess: (quote) => {
      void queryClient.invalidateQueries({ queryKey: ['quotes'] });
      navigate(`/quotes/${quote.id}`);
    },
    onError: (err) => setFormError(getApiErrorMessage(err, 'No se pudo empezar la cotización.')),
  });

  const saveValidity = useMutation({
    mutationFn: (days: number) => quotesApi.updateSettings(days),
    onSuccess: (settings) => {
      queryClient.setQueryData(['quotes', 'settings'], settings);
      setValidity(null);
      toast.success(`Tus cotizaciones valen ${settings.validityDays} días desde que las envías.`);
    },
    onError: (err) => toast.error(getApiErrorMessage(err, 'No se pudo guardar la vigencia.')),
  });

  // Fase 29G: whether customers answer from the link, saved as soon as it changes (with the validity as it is saved). Turned
  // off, the deposit goes off with it (Fase 31N): it is paid from the link.
  const saveOnlineReplies = useMutation({
    mutationFn: ({ validityDays, onlineReplies, dropDeposit }: { validityDays: number; onlineReplies: boolean; dropDeposit: boolean }) =>
      quotesApi.updateSettings(validityDays, onlineReplies, dropDeposit ? 0 : undefined),
    onSuccess: (settings, { dropDeposit }) => {
      queryClient.setQueryData(['quotes', 'settings'], settings);
      toast.success(settings.onlineReplies
        ? 'Listo: tus clientes pueden aceptar o rechazar la cotización desde su enlace.'
        : `Listo: tus clientes te responden por mensaje o en persona, y tú registras su respuesta${dropDeposit ? '; ya no pides anticipo' : ''}.`);
    },
    onError: (err) => toast.error(getApiErrorMessage(err, 'No se pudo guardar cómo te responden tus clientes.')),
  });

  // Fase 31N: the share of what is left to pay the customer pays online to accept from the link; 0 asks none.
  const saveDeposit = useMutation({
    mutationFn: ({ validityDays, percent }: { validityDays: number; percent: number }) =>
      quotesApi.updateSettings(validityDays, undefined, percent),
    onSuccess: (settings) => {
      queryClient.setQueryData(['quotes', 'settings'], settings);
      toast.success(settings.depositPercent
        ? `Listo: para aceptar desde su enlace, tus clientes pagan en línea el ${settings.depositPercent} % de lo que queda por pagar.`
        : 'Listo: tus clientes aceptan desde su enlace sin pagar anticipo.');
    },
    onError: (err) => toast.error(getApiErrorMessage(err, 'No se pudo guardar el anticipo.')),
  });
  const settingsBusy = saveValidity.isPending || saveOnlineReplies.isPending || saveDeposit.isPending;

  const saveTemplate = useMutation({
    mutationFn: (templateId: string) => quotesApi.setWhatsAppTemplate(templateId || null),
    onSuccess: (settings) => {
      queryClient.setQueryData(['quotes', 'settings'], settings);
      setTemplate(null);
      toast.success('Listo: así salen tus cotizaciones por WhatsApp.');
    },
    onError: (err) => toast.error(getApiErrorMessage(err, 'No se pudo guardar la plantilla.')),
  });

  const page = quotesQuery.data;
  const settings = settingsQuery.data;
  const validityDays = validity ?? String(settings?.validityDays ?? '');
  const templateId = template ?? settings?.whatsAppTemplateId ?? '';
  const savedTemplate = settings?.whatsAppTemplates.find((t) => t.id === settings.whatsAppTemplateId);

  // Fase 29Z: this device's sound with each answer; the top bar plays it on any screen.
  const chime = useArrivalChime(QUOTES_SOUND_KEY);

  const openNew = () => {
    setForm(EMPTY_FORM);
    setFormError('');
    setShowNew(true);
  };

  return (
    <>
      <PageTitle title="Cotizaciones" />

      {quotesQuery.error && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(quotesQuery.error, 'No se pudieron cargar las cotizaciones.')}</div>
      )}

      {/* Always there, so a screen reader announces the note when it appears: the browser plays nothing until it is touched
          after it opens. */}
      <div role="status">
        {chime.needsTouch && (
          <p className="text-muted small mb-2">Toca la pantalla o presiona una tecla para que suenen las respuestas.</p>
        )}
      </div>

      <div className="card">
        <div className="card-body">
          <DataTable
            data={page?.items ?? []}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={quotesQuery.isLoading}
            emptyMessage={filter === QuoteListFilter.Open
              ? 'No tienes cotizaciones abiertas. Empieza una aquí o desde la cita de una visita en la Agenda.'
              : 'No hay cotizaciones con ese estado.'}
            filterValues={[filter === QuoteListFilter.All ? null : filter]}
            filters={(
              <DataTableFilter label="Estado" htmlFor="quotes-filter">
                <select
                  id="quotes-filter"
                  className="form-select form-select-sm"
                  value={filter}
                  onChange={(e) => {
                    setFilter(Number(e.target.value) as QuoteListFilter);
                    setPageNumber(1);
                    // A filter picked by hand drops the one the link named: the bell sets it again, and a reload keeps this one.
                    if (linked) {
                      setSearchParams((params) => {
                        params.delete('estado');
                        return params;
                      }, { replace: true });
                    }
                  }}
                >
                  {QUOTE_FILTERS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
              </DataTableFilter>
            )}
            toolbarEnd={(
              <>
                <button
                  type="button"
                  className="btn btn-light btn-sm"
                  onClick={chime.toggle}
                  aria-pressed={chime.enabled}
                  title="Suena en este navegador cuando un cliente acepta o rechaza una cotización desde su enlace"
                >
                  <i className={`${chime.enabled ? 'ri-volume-up-line' : 'ri-volume-mute-line'} me-1`} aria-hidden="true"></i>
                  Sonido para respuestas
                </button>
                <ChimeSettingsButton />
                <button type="button" className="btn btn-primary btn-sm" onClick={openNew}>
                  <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva cotización
                </button>
              </>
            )}
            serverSide={{
              totalCount: page?.totalCount ?? 0,
              page: pageNumber,
              pageSize: 20,
              onPageChange: setPageNumber,
              onPageSizeChange: () => setPageNumber(1),
              onSearchChange: (term) => {
                setSearch(term);
                setPageNumber(1);
              },
            }}
            pageSizeOptions={[20]}
            searchPlaceholder="Folio, cliente o teléfono"
            renderRow={(item) => (
              <>
                <td className="fw-semibold text-nowrap">
                  <Link to={`/quotes/${item.id}`}>{quoteFolio(item.number, item.revision)}</Link>
                  {item.fromVisit && <span className="d-block small text-muted fw-normal">De una visita</span>}
                </td>
                <td>
                  {item.customerName}
                  <span className="d-block small text-muted">{item.customerPhone}</span>
                </td>
                <td className="text-center">
                  <span className={quoteStatusChip(item.status)}>{quoteStatusLabel(item.status)}</span>
                  {item.repliedFromLink && <span className="d-block small text-muted">Desde su enlace</span>}
                </td>
                <td className="text-center text-nowrap">
                  {item.validUntil ? formatCalendarDate(item.validUntil, { day: 'numeric', month: 'short' }) : '—'}
                </td>
                <td className="text-end">{formatMoney(item.total)}</td>
                <td className="text-center">
                  <Link
                    to={`/quotes/${item.id}`}
                    className="btn btn-sm btn-light"
                    aria-label={`Abrir la cotización ${quoteFolio(item.number, item.revision)} de ${item.customerName}`}
                  >
                    Abrir
                  </Link>
                </td>
              </>
            )}
          />
        </div>
      </div>

      {settings && (
        <div className="card">
          <div className="card-body">
            <h2 className="h6 mb-1">Vigencia</h2>
            <p className="text-muted small mb-2">
              Una cotización vale {settings.validityDays} días desde que la envías; en cada borrador puedes cambiarla.
              {' '}Sus precios {settings.pricesIncludeTax ? 'incluyen' : 'no incluyen'} IVA, como los de tu negocio.
            </p>
            {isAdmin && (
              <form
                className="d-flex flex-wrap align-items-end gap-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  saveValidity.mutate(Number(validityDays));
                }}
              >
                <div>
                  <label className="form-label small mb-1" htmlFor="quotes-validity">Días de vigencia</label>
                  <input
                    id="quotes-validity"
                    type="number"
                    min={1}
                    max={90}
                    className="form-control form-control-sm"
                    style={{ width: '7rem' }}
                    value={validityDays}
                    onChange={(e) => setValidity(e.target.value)}
                  />
                </div>
                <button
                  type="submit"
                  className="btn btn-sm btn-light"
                  disabled={settingsBusy || validity === null || Number(validityDays) === settings.validityDays}
                >
                  Guardar vigencia
                </button>
              </form>
            )}

            <h2 className="h6 mt-3 mb-1">Respuesta del cliente</h2>
            <p className="text-muted small mb-2">
              {settings.onlineReplies
                ? 'Tus clientes aceptan o rechazan la cotización desde su enlace, y su respuesta aparece aquí con «Desde su enlace». También puedes registrar lo que te respondan por otro medio.'
                : 'Tus clientes ven la cotización en su enlace y te responden por mensaje o en persona; tú registras su respuesta.'}
            </p>
            {isAdmin && (
              <div className="form-check form-switch">
                <input
                  id={onlineRepliesId}
                  type="checkbox"
                  role="switch"
                  className="form-check-input"
                  checked={saveOnlineReplies.isPending ? saveOnlineReplies.variables.onlineReplies : settings.onlineReplies}
                  // Not «disabled» while saving: a disabled control drops the keyboard's focus. A change meanwhile waits.
                  aria-disabled={settingsBusy}
                  onChange={(e) => {
                    if (settingsBusy) return;
                    saveOnlineReplies.mutate({
                      validityDays: settings.validityDays,
                      onlineReplies: e.target.checked,
                      dropDeposit: !e.target.checked && settings.depositPercent != null,
                    });
                  }}
                />
                <label className="form-check-label small" htmlFor={onlineRepliesId}>
                  Tus clientes responden desde su enlace
                </label>
              </div>
            )}

            {settings.onlineReplies && (
              <QuoteDepositSetting
                id={depositId}
                settings={settings}
                canChange={isAdmin}
                busy={settingsBusy}
                pending={saveDeposit.isPending ? saveDeposit.variables.percent : null}
                onChange={(percent) => saveDeposit.mutate({ validityDays: settings.validityDays, percent })}
              />
            )}

            {settings.whatsAppConnected && (
              <>
                <h2 className="h6 mt-3 mb-1">Mensaje de WhatsApp</h2>
                <p className="text-muted small mb-2">
                  Tus cotizaciones salen del WhatsApp de tu negocio con{' '}
                  {savedTemplate ? `la plantilla «${savedTemplate.name}»` : 'el texto de TienditApp'}.
                  {!savedTemplate?.providerTemplateName && ' Sin una plantilla aprobada en Meta, solo llegan a quien te escribió en las últimas 24 horas.'}
                </p>
                {isAdmin && (
                  <form
                    className="d-flex flex-wrap align-items-end gap-2"
                    onSubmit={(e) => {
                      e.preventDefault();
                      saveTemplate.mutate(templateId);
                    }}
                  >
                    <div>
                      <label className="form-label small mb-1" htmlFor="quotes-template">Plantilla</label>
                      <select
                        id="quotes-template"
                        className="form-select form-select-sm"
                        value={templateId}
                        onChange={(e) => setTemplate(e.target.value)}
                      >
                        <option value="">Texto de TienditApp</option>
                        {settings.whatsAppTemplates.map((t) => (
                          <option key={t.id} value={t.id}>{t.name}{t.providerTemplateName ? ' (aprobada en Meta)' : ''}</option>
                        ))}
                      </select>
                    </div>
                    <button
                      type="submit"
                      className="btn btn-sm btn-light"
                      disabled={saveTemplate.isPending || template === null || templateId === (settings.whatsAppTemplateId ?? '')}
                    >
                      Guardar plantilla
                    </button>
                  </form>
                )}
                {isAdmin && settings.whatsAppTemplates.length === 0 && (
                  <p className="text-muted small mt-2 mb-0">
                    Para usar la tuya, crea una plantilla «Cotización por WhatsApp» en{' '}
                    <Link to="/campaigns?tab=templates">Campañas › Plantillas</Link>.
                  </p>
                )}
              </>
            )}
          </div>
        </div>
      )}

      <Modal show={showNew} onHide={() => !create.isPending && setShowNew(false)} centered aria-labelledby={titleId}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setFormError('');
            create.mutate();
          }}
        >
          <Modal.Header closeButton>
            <Modal.Title id={titleId} as="h2" className="h5">Nueva cotización</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <p className="text-muted small">
              ¿Viene de una visita? Ábrela desde la cita en la Agenda con «Cotizar»: toma al cliente y, si se descuenta, lo que pagó.
            </p>
            <div className="mb-2">
              <label className="form-label" htmlFor="quote-new-name">Cliente *</label>
              <input id="quote-new-name" className="form-control" maxLength={200} required value={form.customerName}
                onChange={(e) => setForm((x) => ({ ...x, customerName: e.target.value }))} />
            </div>
            <div className="mb-2">
              <label className="form-label" htmlFor="quote-new-phone">Teléfono *</label>
              <input id="quote-new-phone" type="tel" className="form-control" maxLength={50} required value={form.customerPhone}
                onChange={(e) => setForm((x) => ({ ...x, customerPhone: e.target.value }))} />
            </div>
            <div className="mb-2">
              <label className="form-label" htmlFor="quote-new-email">Correo</label>
              <input id="quote-new-email" type="email" className="form-control" maxLength={254} value={form.customerEmail}
                onChange={(e) => setForm((x) => ({ ...x, customerEmail: e.target.value }))} />
            </div>
            {formError && <div className="alert alert-danger small mb-0" role="alert">{formError}</div>}
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" onClick={() => setShowNew(false)} disabled={create.isPending}>Cancelar</button>
            <button type="submit" className="btn btn-primary" disabled={create.isPending}>
              {create.isPending ? 'Empezando…' : 'Empezar cotización'}
            </button>
          </Modal.Footer>
        </form>
      </Modal>
    </>
  );
}
