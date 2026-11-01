import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import CopyTrackingLink from '../../components/tracking/CopyTrackingLink';
import { getApiErrorMessage } from '../../api/errors';
import { productsApi } from '../../api/productsApi';
import { quotesApi, type Quote, type QuoteLineRequest } from '../../api/quotesApi';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { QuoteReplyChannel, QuoteStatus } from '../../types';
import { formatCalendarDate, formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';
import {
  REPLY_CHANNELS,
  quoteFolio,
  quoteLink,
  quoteMailUrl,
  quoteMessage,
  quoteStatusChip,
  quoteStatusLabel,
  quoteWhatsAppUrl,
  replyChannelPhrase,
} from '../../utils/quotes';

interface LineForm {
  key: string;
  productId: string | null;
  description: string;
  quantity: string;
  unitPrice: string;
}

interface DraftForm {
  customerName: string;
  customerPhone: string;
  customerEmail: string;
  validityDays: string;
  note: string;
  lines: LineForm[];
}

let nextKey = 0;
const newKey = () => `line-${nextKey++}`;

function toForm(quote: Quote): DraftForm {
  return {
    customerName: quote.customerName,
    customerPhone: quote.customerPhone,
    customerEmail: quote.customerEmail ?? '',
    validityDays: String(quote.validityDays),
    note: quote.note ?? '',
    lines: quote.lines.map((line) => ({
      key: newKey(),
      productId: line.productId ?? null,
      description: line.description,
      quantity: String(line.quantity),
      unitPrice: String(line.unitPrice),
    })),
  };
}

/** Until when it is valid, as it stands: still, no longer, or until it was answered or replaced. */
function validityText(status: QuoteStatus, validUntil: string): string {
  const day = formatCalendarDate(validUntil);
  if (status === QuoteStatus.Sent) return `Vale hasta el ${day}`;
  if (status === QuoteStatus.Expired) return `Venció el ${day}`;
  return `Valía hasta el ${day}`;
}

function toLines(lines: LineForm[]): QuoteLineRequest[] {
  return lines.map((line) => ({
    productId: line.productId,
    description: line.description.trim() || null,
    quantity: Number(line.quantity),
    unitPrice: Number(line.unitPrice),
  }));
}

type Action = 'save' | 'send' | 'revise' | 'discard' | 'reply';

/**
 * A version of a quote (Fase 29C, ADR 0053). A draft is edited here and the server totals it when saved; sent, it says the
 * same forever: its link goes to the customer by WhatsApp or by email, the business records what they answered, and
 * correcting it starts the next version. The history lists every version of the quote. Each version opens fresh: nothing
 * typed or shown on another one stays.
 */
export default function QuoteDetailPage() {
  const { id = '' } = useParams();
  return <QuoteDetail key={id} id={id} />;
}

function QuoteDetail({ id }: { id: string }) {
  const navigate = useNavigate();
  const toast = useToast();
  const { confirm } = useConfirm();
  const queryClient = useQueryClient();
  const { tenant } = useTenant();
  const replyTitleId = useId();
  const [form, setForm] = useState<DraftForm | null>(null);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState('');
  const [catalogSearch, setCatalogSearch] = useState('');
  const [showReply, setShowReply] = useState(false);
  const [replyError, setReplyError] = useState('');
  const [reply, setReply] = useState<{ accepted: boolean; channel: QuoteReplyChannel; response: string }>(
    { accepted: true, channel: QuoteReplyChannel.WhatsApp, response: '' });
  // One action at a time: every button waits for the one running, and the draft's fields too.
  const [pending, setPending] = useState<Action | null>(null);

  const quoteQuery = useQuery({ queryKey: ['quotes', 'detail', id], queryFn: ({ signal }) => quotesApi.get(id, signal), enabled: !!id });
  const quote = quoteQuery.data;
  const isDraft = quote?.status === QuoteStatus.Draft;

  // The form follows what the server has while nothing is typed: a refresh never takes away what the person is writing.
  const [seen, setSeen] = useState<Quote | undefined>(undefined);
  if (quote && quote !== seen) {
    setSeen(quote);
    if (!dirty || !seen) {
      setForm(toForm(quote));
      setDirty(false);
    }
  }

  const catalogQuery = useQuery({
    queryKey: ['quotes', 'catalog', catalogSearch.trim()],
    queryFn: async () => (await productsApi.getAll({ search: catalogSearch.trim() || undefined, pageSize: 20 })).data.items,
    enabled: isDraft && catalogSearch.trim().length >= 2,
  });

  const show = (next: Quote) => {
    queryClient.setQueryData(['quotes', 'detail', next.id], next);
    void queryClient.invalidateQueries({ queryKey: ['quotes'], refetchType: 'none' });
    if (next.id !== id) {
      navigate(`/quotes/${next.id}`);
      return;
    }
    // What the server saved, while the fields waited for it.
    setForm(toForm(next));
    setDirty(false);
  };

  const save = useMutation({
    mutationFn: (current: DraftForm) => quotesApi.updateDraft(id, {
      customerId: quote?.customerId ?? null,
      customerName: current.customerName.trim(),
      customerPhone: current.customerPhone.trim(),
      customerEmail: current.customerEmail.trim() || null,
      validityDays: Number(current.validityDays),
      note: current.note.trim() || null,
      lines: toLines(current.lines),
    }),
  });

  /** Runs one action at a time; the message of a refusal goes to <paramref name="onError"/> (the page's alert by default). */
  const run = async (
    kind: Action, action: () => Promise<Quote>, done: string, fallback: string, onError: (message: string) => void = setError,
  ): Promise<boolean> => {
    if (pending) return false;
    setPending(kind);
    setError('');
    try {
      const next = await action();
      show(next);
      if (done) toast.success(done);
      return true;
    } catch (err: unknown) {
      onError(getApiErrorMessage(err, fallback));
      return false;
    } finally {
      setPending(null);
    }
  };

  const saveDraft = () => form && run('save', () => save.mutateAsync(form), 'Borrador guardado.', 'No se pudo guardar el borrador.');

  const send = () => form && run('send', async () => {
    if (dirty) show(await save.mutateAsync(form));
    return quotesApi.send(id);
  }, 'Cotización lista: mándale el enlace al cliente.', 'No se pudo terminar la cotización.');

  const revise = () => run('revise', () => quotesApi.revise(id), '', 'No se pudo empezar la corrección.');

  const discard = async () => {
    const accepted = await confirm({
      title: 'Descartar el borrador',
      message: 'El borrador queda en el historial como descartado y su folio no se vuelve a usar.',
      confirmLabel: 'Descartar',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (accepted) await run('discard', () => quotesApi.discard(id), 'Borrador descartado.', 'No se pudo descartar el borrador.');
  };

  const recordReply = async () => {
    setReplyError('');
    const recorded = await run(
      'reply',
      () => quotesApi.reply(id, { accepted: reply.accepted, channel: reply.channel, response: reply.response.trim() || null }),
      reply.accepted ? 'Cotización aceptada.' : 'Cotización rechazada.',
      'No se pudo registrar la respuesta.',
      setReplyError);
    if (recorded) setShowReply(false);
  };

  const change = (update: (current: DraftForm) => DraftForm) => {
    setForm((current) => (current ? update(current) : current));
    setDirty(true);
  };

  const changeLine = (key: string, field: keyof Omit<LineForm, 'key' | 'productId'>, value: string) =>
    change((current) => ({ ...current, lines: current.lines.map((line) => (line.key === key ? { ...line, [field]: value } : line)) }));

  if (quoteQuery.isLoading || (quote && !form)) {
    return <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>;
  }
  if (quoteQuery.error || !quote || !form) {
    return (
      <>
        <PageTitle title="Cotización" parent={{ label: 'Cotizaciones', to: '/quotes' }} />
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(quoteQuery.error, 'No encontramos esa cotización.')}</div>
      </>
    );
  }

  const folio = quoteFolio(quote.number, quote.revision);
  const link = quote.trackingToken ? quoteLink(quote.trackingToken) : null;
  const message = link ? quoteMessage(quote, tenant?.name ?? '', link) : '';
  const whatsApp = link ? quoteWhatsAppUrl(quote.customerPhone, message) : null;
  const mail = link ? quoteMailUrl(quote.customerEmail, `Cotización ${folio}`, message) : null;
  const busy = pending !== null;
  const canReply = quote.status === QuoteStatus.Sent;
  const canRevise = (quote.status === QuoteStatus.Sent || quote.status === QuoteStatus.Expired || quote.status === QuoteStatus.Rejected)
    && !quote.draftRevisionId;
  // A correction of a version the customer already accepted is not sent: the server refuses it.
  const acceptedVersion = isDraft ? quote.versions.find((version) => version.status === QuoteStatus.Accepted) : undefined;
  const ivaNote = quote.pricesIncludeTax ? 'Los precios incluyen IVA.' : 'El IVA se suma a los precios.';

  return (
    <>
      <PageTitle title={`Cotización ${folio}`} parent={{ label: 'Cotizaciones', to: '/quotes' }} />

      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {quote.draftRevisionId && (
        <div className="alert alert-info" role="status">
          Hay una corrección en borrador. <Link to={`/quotes/${quote.draftRevisionId}`}>Abrir la corrección</Link>
        </div>
      )}

      <div className="row">
        <div className="col-lg-8">
          <div className="card">
            <div className="card-body">
              <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
                <span className={quoteStatusChip(quote.status)}>{quoteStatusLabel(quote.status)}</span>
                {quote.validUntil && <span className="text-muted small">{validityText(quote.status, quote.validUntil)}</span>}
                {quote.appointment && (
                  <span className="text-muted small">
                    Visita del {formatDateTime(quote.appointment.startAt)}
                    {quote.appointment.postalCode ? ` · CP ${quote.appointment.postalCode}` : ''}
                  </span>
                )}
              </div>

              {isDraft ? (
                <fieldset disabled={busy}>
                <div className="row">
                  <div className="col-md-6 mb-2">
                    <label className="form-label" htmlFor="quote-customer">Cliente *</label>
                    <input id="quote-customer" className="form-control" maxLength={200} value={form.customerName}
                      onChange={(e) => change((x) => ({ ...x, customerName: e.target.value }))} />
                  </div>
                  <div className="col-md-6 mb-2">
                    <label className="form-label" htmlFor="quote-phone">Teléfono *</label>
                    <input id="quote-phone" type="tel" className="form-control" maxLength={50} value={form.customerPhone}
                      onChange={(e) => change((x) => ({ ...x, customerPhone: e.target.value }))} />
                  </div>
                  <div className="col-md-8 mb-2">
                    <label className="form-label" htmlFor="quote-email">Correo</label>
                    <input id="quote-email" type="email" className="form-control" maxLength={254} value={form.customerEmail}
                      onChange={(e) => change((x) => ({ ...x, customerEmail: e.target.value }))} />
                  </div>
                  <div className="col-md-4 mb-2">
                    <label className="form-label" htmlFor="quote-validity">Vigencia (días)</label>
                    <input id="quote-validity" type="number" min={1} max={90} className="form-control" value={form.validityDays}
                      onChange={(e) => change((x) => ({ ...x, validityDays: e.target.value }))} />
                  </div>
                </div>
                </fieldset>
              ) : (
                <dl className="row small mb-0">
                  <dt className="col-4 col-sm-3 text-muted fw-normal">Cliente</dt>
                  <dd className="col-8 col-sm-9">{quote.customerName}</dd>
                  <dt className="col-4 col-sm-3 text-muted fw-normal">Teléfono</dt>
                  <dd className="col-8 col-sm-9">{quote.customerPhone}</dd>
                  {quote.customerEmail && (
                    <>
                      <dt className="col-4 col-sm-3 text-muted fw-normal">Correo</dt>
                      <dd className="col-8 col-sm-9">{quote.customerEmail}</dd>
                    </>
                  )}
                  {quote.sentAt && (
                    <>
                      <dt className="col-4 col-sm-3 text-muted fw-normal">Enviada</dt>
                      <dd className="col-8 col-sm-9 mb-0">{formatDateTime(quote.sentAt)}</dd>
                    </>
                  )}
                </dl>
              )}
            </div>
          </div>

          <div className="card">
            <div className="card-body">
              <h2 className="h6">Renglones</h2>
              {isDraft ? (
                <fieldset disabled={busy}>
                  {form.lines.length === 0 && <p className="text-muted small">Agrega lo que incluye el trabajo: del catálogo o escrito a mano.</p>}
                  {form.lines.map((line, index) => (
                    <div key={line.key} className="row g-2 align-items-end mb-2">
                      <div className="col-12 col-md-6">
                        <label className="form-label small mb-1" htmlFor={`quote-line-${index}-description`}>Qué incluye</label>
                        <input id={`quote-line-${index}-description`} className="form-control form-control-sm" maxLength={200}
                          value={line.description} onChange={(e) => changeLine(line.key, 'description', e.target.value)} />
                      </div>
                      <div className="col-4 col-md-2">
                        <label className="form-label small mb-1" htmlFor={`quote-line-${index}-quantity`}>Cantidad</label>
                        <input id={`quote-line-${index}-quantity`} type="number" min={0.001} step="any" className="form-control form-control-sm"
                          value={line.quantity} onChange={(e) => changeLine(line.key, 'quantity', e.target.value)} />
                      </div>
                      <div className="col-5 col-md-3">
                        <label className="form-label small mb-1" htmlFor={`quote-line-${index}-price`}>Precio</label>
                        <input id={`quote-line-${index}-price`} type="number" min={0} step="0.01" className="form-control form-control-sm"
                          value={line.unitPrice} onChange={(e) => changeLine(line.key, 'unitPrice', e.target.value)} />
                      </div>
                      <div className="col-3 col-md-1 text-end">
                        <button
                          type="button"
                          className="btn btn-sm btn-light"
                          aria-label={`Quitar el renglón ${line.description || index + 1}`}
                          onClick={() => change((x) => ({ ...x, lines: x.lines.filter((l) => l.key !== line.key) }))}
                        >
                          <i className="ri-delete-bin-line" aria-hidden="true"></i>
                        </button>
                      </div>
                    </div>
                  ))}
                  <div className="d-flex flex-wrap gap-2 align-items-end mt-3">
                    <button
                      type="button"
                      className="btn btn-sm btn-light"
                      onClick={() => change((x) => ({ ...x, lines: [...x.lines, { key: newKey(), productId: null, description: '', quantity: '1', unitPrice: '0' }] }))}
                    >
                      <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar renglón
                    </button>
                    <div className="flex-grow-1" style={{ minWidth: '14rem' }}>
                      <label className="form-label small mb-1" htmlFor="quote-catalog">Del catálogo</label>
                      <input id="quote-catalog" className="form-control form-control-sm" placeholder="Busca un servicio o producto"
                        value={catalogSearch} onChange={(e) => setCatalogSearch(e.target.value)} />
                    </div>
                  </div>
                  {catalogSearch.trim().length >= 2 && (
                    <ul className="list-group list-group-flush mt-2" aria-label="Resultados del catálogo">
                      {(catalogQuery.data ?? []).map((product) => (
                        <li key={product.id} className="list-group-item d-flex justify-content-between align-items-center px-0">
                          <span>{product.name} <span className="text-muted small">{formatMoney(product.priceAmount)}</span></span>
                          <button
                            type="button"
                            className="btn btn-sm btn-light"
                            aria-label={`Agregar ${product.name}`}
                            onClick={() => {
                              change((x) => ({
                                ...x,
                                lines: [...x.lines, { key: newKey(), productId: product.id, description: product.name, quantity: '1', unitPrice: String(product.priceAmount) }],
                              }));
                              setCatalogSearch('');
                            }}
                          >
                            Agregar
                          </button>
                        </li>
                      ))}
                      {catalogQuery.data?.length === 0 && <li className="list-group-item px-0 text-muted small">Nada en tu catálogo con ese nombre.</li>}
                    </ul>
                  )}
                  <div className="mt-3">
                    <label className="form-label" htmlFor="quote-note">Nota para el cliente</label>
                    <textarea id="quote-note" className="form-control" rows={2} maxLength={1000} value={form.note}
                      onChange={(e) => change((x) => ({ ...x, note: e.target.value }))} />
                  </div>
                </fieldset>
              ) : (
                <div className="table-responsive">
                  <table className="table table-sm mb-0">
                    <thead>
                      <tr><th>Qué incluye</th><th className="text-end">Cantidad</th><th className="text-end">Precio</th><th className="text-end">Importe</th></tr>
                    </thead>
                    <tbody>
                      {quote.lines.map((line) => (
                        <tr key={line.position}>
                          <td>{line.description}{line.isTaxExempt && <span className="text-muted small"> · exento</span>}</td>
                          <td className="text-end">{line.quantity}</td>
                          <td className="text-end">{formatMoney(line.unitPrice)}</td>
                          <td className="text-end">{formatMoney(line.total)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {quote.note && <p className="small text-muted mt-2 mb-0">{quote.note}</p>}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="col-lg-4">
          <div className="card">
            <div className="card-body">
              <h2 className="h6">Total</h2>
              {dirty ? (
                <p className="text-muted small mb-0">Guarda los cambios para ver el total: lo calcula el servidor con el IVA de cada renglón.</p>
              ) : (
                <dl className="row small mb-0">
                  <dt className="col-7 text-muted fw-normal">Subtotal</dt>
                  <dd className="col-5 text-end">{formatMoney(quote.subTotal)}</dd>
                  <dt className="col-7 text-muted fw-normal">IVA</dt>
                  <dd className="col-5 text-end">{formatMoney(quote.tax)}</dd>
                  <dt className="col-7">Total</dt>
                  <dd className="col-5 text-end fw-semibold">{formatMoney(quote.total)}</dd>
                  {quote.visitCredit && (
                    <>
                      <dt className="col-7 text-muted fw-normal">Visita (se descuenta)</dt>
                      <dd className="col-5 text-end">{quote.visitCredit.applies ? `−${formatMoney(quote.visitCredit.amount)}` : 'Ya no aplica'}</dd>
                      <dt className="col-7">Por pagar</dt>
                      <dd className="col-5 text-end fw-semibold mb-0">{formatMoney(quote.toPay)}</dd>
                    </>
                  )}
                </dl>
              )}
              <p className="text-muted small mt-2 mb-0">{ivaNote}</p>
              {quote.visitCredit && !quote.visitCredit.applies && (
                <p className="text-muted small mt-1 mb-0">La venta de la visita se canceló o se reembolsó: ya no se descuenta.</p>
              )}
            </div>
          </div>

          <div className="card">
            <div className="card-body d-grid gap-2">
              {isDraft && (
                <>
                  {acceptedVersion && (
                    <div className="alert alert-warning small mb-0" role="status">
                      El cliente ya aceptó la versión {acceptedVersion.revision}: esta corrección no se puede terminar. Descártala.
                    </div>
                  )}
                  <button type="button" className="btn btn-primary" disabled={busy || !!acceptedVersion} onClick={() => void send()}>
                    <i className="ri-link me-1" aria-hidden="true"></i>{pending === 'send' ? 'Terminando…' : 'Terminar y obtener enlace'}
                  </button>
                  <button type="button" className="btn btn-light" disabled={busy || !dirty} onClick={() => void saveDraft()}>
                    {pending === 'save' ? 'Guardando…' : 'Guardar borrador'}
                  </button>
                  <button type="button" className="btn btn-link text-danger" disabled={busy} onClick={() => void discard()}>
                    Descartar borrador
                  </button>
                  <p className="text-muted small mb-0">
                    Al terminarla vale {form.validityDays} días desde hoy y ya no se edita: se corrige con una versión nueva.
                    Después le mandas su enlace al cliente por WhatsApp o por correo.
                  </p>
                </>
              )}

              {link && quote.status === QuoteStatus.Sent && (
                <>
                  {canReply && (
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={busy}
                      onClick={() => {
                        setReplyError('');
                        setShowReply(true);
                      }}
                    >
                      Registrar respuesta
                    </button>
                  )}
                  {whatsApp && (
                    <a className="btn btn-light" href={whatsApp} target="_blank" rel="noreferrer">
                      <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Enviar por WhatsApp
                    </a>
                  )}
                  {mail && (
                    <a className="btn btn-light" href={mail}>
                      <i className="ri-mail-line me-1" aria-hidden="true"></i>Enviar por correo
                    </a>
                  )}
                  {quote.trackingToken && <CopyTrackingLink token={quote.trackingToken} path="/cotizacion" />}
                </>
              )}

              {canRevise && (
                <button
                  type="button"
                  className={quote.status === QuoteStatus.Sent ? 'btn btn-light' : 'btn btn-primary'}
                  disabled={busy}
                  onClick={() => void revise()}
                >
                  {pending === 'revise' ? 'Empezando…' : 'Corregir'}
                </button>
              )}

              {(quote.status === QuoteStatus.Accepted || quote.status === QuoteStatus.Rejected) && quote.replyChannel != null && (
                <p className="small mb-0">
                  {quote.status === QuoteStatus.Accepted ? 'Aceptó' : 'Rechazó'} {replyChannelPhrase(quote.replyChannel)}
                  {quote.closedAt ? ` el ${formatDateTime(quote.closedAt)}` : ''}.
                  {quote.customerResponse && <span className="d-block text-muted">«{quote.customerResponse}»</span>}
                </p>
              )}
              {quote.status === QuoteStatus.Expired && <p className="text-muted small mb-0">Ya venció: corrígela para mandarle una versión nueva con otra vigencia.</p>}
            </div>
          </div>

          {quote.versions.length > 1 && (
            <div className="card">
              <div className="card-body">
                <h2 className="h6">Versiones</h2>
                <ul className="list-unstyled small mb-0">
                  {quote.versions.map((version) => (
                    <li key={version.id} className="d-flex justify-content-between align-items-center py-1">
                      {version.id === quote.id ? (
                        <span className="fw-semibold">Versión {version.revision}</span>
                      ) : (
                        <Link to={`/quotes/${version.id}`}>Versión {version.revision}</Link>
                      )}
                      <span>
                        <span className={`${quoteStatusChip(version.status)} me-2`}>{quoteStatusLabel(version.status)}</span>
                        {formatMoney(version.total)}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          )}
        </div>
      </div>

      <Modal show={showReply} onHide={() => !busy && setShowReply(false)} centered aria-labelledby={replyTitleId}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void recordReply();
          }}
        >
          <Modal.Header closeButton>
            <Modal.Title id={replyTitleId} as="h2" className="h5">Respuesta del cliente</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            <fieldset className="mb-3">
              <legend className="form-label fs-6">¿Qué respondió?</legend>
              <div className="form-check">
                <input className="form-check-input" type="radio" id="quote-reply-accepted" name="quote-reply"
                  checked={reply.accepted} onChange={() => setReply((x) => ({ ...x, accepted: true }))} />
                <label className="form-check-label" htmlFor="quote-reply-accepted">La aceptó</label>
              </div>
              <div className="form-check">
                <input className="form-check-input" type="radio" id="quote-reply-rejected" name="quote-reply"
                  checked={!reply.accepted} onChange={() => setReply((x) => ({ ...x, accepted: false }))} />
                <label className="form-check-label" htmlFor="quote-reply-rejected">La rechazó</label>
              </div>
            </fieldset>
            <div className="mb-3">
              <label className="form-label" htmlFor="quote-reply-channel">Por dónde</label>
              <select id="quote-reply-channel" className="form-select" value={reply.channel}
                onChange={(e) => setReply((x) => ({ ...x, channel: Number(e.target.value) as QuoteReplyChannel }))}>
                {REPLY_CHANNELS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
            </div>
            <div>
              <label className="form-label" htmlFor="quote-reply-response">Lo que dijo</label>
              <textarea id="quote-reply-response" className="form-control" rows={3} maxLength={500} value={reply.response}
                onChange={(e) => setReply((x) => ({ ...x, response: e.target.value }))} />
            </div>
            {replyError && <div className="alert alert-danger small mt-3 mb-0" role="alert">{replyError}</div>}
          </Modal.Body>
          <Modal.Footer>
            <button type="button" className="btn btn-light" disabled={busy} onClick={() => setShowReply(false)}>Cancelar</button>
            <button type="submit" className="btn btn-primary" disabled={busy}>{pending === 'reply' ? 'Guardando…' : 'Guardar respuesta'}</button>
          </Modal.Footer>
        </form>
      </Modal>
    </>
  );
}
