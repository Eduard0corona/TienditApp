import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link, useNavigate } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { getApiErrorMessage } from '../../api/errors';
import { quotesApi } from '../../api/quotesApi';
import { useAuth } from '../../hooks/useAuth';
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
  const [filter, setFilter] = useState<QuoteListFilter>(QuoteListFilter.Open);
  const [search, setSearch] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
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
            filters={(
              <DataTableFilter label="Estado" htmlFor="quotes-filter">
                <select
                  id="quotes-filter"
                  className="form-select form-select-sm"
                  value={filter}
                  onChange={(e) => {
                    setFilter(Number(e.target.value) as QuoteListFilter);
                    setPageNumber(1);
                  }}
                >
                  {QUOTE_FILTERS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
              </DataTableFilter>
            )}
            toolbarEnd={(
              <button type="button" className="btn btn-primary btn-sm" onClick={openNew}>
                <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva cotización
              </button>
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
                  disabled={saveValidity.isPending || validity === null || Number(validityDays) === settings.validityDays}
                >
                  Guardar vigencia
                </button>
              </form>
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
