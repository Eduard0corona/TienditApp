import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { messagingApi } from '../../api/messagingApi';
import { getApiErrorMessage } from '../../api/errors';
import { useCampaignMessages, useCampaigns, useCancelCampaign, useCreateCampaign } from '../../api/queries';
import type { Schemas } from '../../api/typedClient';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import {
  CampaignAudienceKind,
  CampaignStatus,
  MessagePurpose,
  type CampaignPreviewDto,
  type MessageTemplateDto,
} from '../../types';

type CampaignDto = Schemas['CampaignDto'];
import { formatDateTime, toDateTimeLocalInput } from '../../utils/dates';
import { CAMPAIGN_STATUS, MESSAGE_STATUS, describeAudience } from './campaignLabels';

const PAGE_SIZE = 10;

type Draft = {
  name: string;
  templateId: string;
  audienceKind: CampaignAudienceKind;
  minimumOrders: string;
  days: string;
  sendNow: boolean;
  scheduledAt: string;
};

function newDraft(templates: MessageTemplateDto[]): Draft {
  const later = new Date(Date.now() + 60 * 60 * 1000);
  return {
    name: '',
    templateId: templates[0]?.id ?? '',
    audienceKind: CampaignAudienceKind.Buyers,
    minimumOrders: '1',
    days: '90',
    sendNow: true,
    scheduledAt: toDateTimeLocalInput(later),
  };
}

function progressText(campaign: CampaignDto): string {
  const { progress } = campaign;
  const parts = [`${progress.sent} enviados`];
  if (progress.delivered > 0) parts.push(`${progress.delivered} entregados`);
  if (progress.read > 0) parts.push(`${progress.read} leídos`);
  if (progress.failed > 0) parts.push(`${progress.failed} fallaron`);
  if (progress.skipped > 0) parts.push(`${progress.skipped} no enviados`);
  if (progress.pending > 0) parts.push(`${progress.pending} pendientes`);
  return parts.join(' · ');
}

export default function CampaignsTab({ onCreateTemplate }: { onCreateTemplate: () => void }) {
  const toast = useToast();
  const { confirm } = useConfirm();
  const [page, setPage] = useState(1);
  const campaigns = useCampaigns(page, PAGE_SIZE);
  const cancelMutation = useCancelCampaign();
  const createMutation = useCreateCampaign();
  const data = campaigns.data;
  const [error, setError] = useState('');
  const [templates, setTemplates] = useState<MessageTemplateDto[]>([]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [preview, setPreview] = useState<CampaignPreviewDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState('');
  const [detail, setDetail] = useState<CampaignDto | null>(null);

  const openNew = async () => {
    setFormError('');
    setPreview(null);
    try {
      const { data: list } = await messagingApi.getTemplates({ purpose: MessagePurpose.Campaign, includeInactive: false });
      setTemplates(list);
      setDraft(newDraft(list));
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar las plantillas.'));
    }
  };

  const audienceRequest = (value: Draft) => ({
    templateId: value.templateId,
    audienceKind: value.audienceKind,
    minimumOrders: Math.max(1, Number(value.minimumOrders) || 1),
    days: value.audienceKind === CampaignAudienceKind.AllCustomers || value.days.trim() === ''
      ? undefined
      : Math.max(1, Number(value.days) || 1),
  });

  const runPreview = async () => {
    if (!draft || busy) return;
    setBusy(true);
    setFormError('');
    try {
      const { data: result } = await messagingApi.previewCampaign(audienceRequest(draft));
      setPreview(result);
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, 'No se pudo calcular a quién llega.'));
    } finally {
      setBusy(false);
    }
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!draft || busy) return;
    const accepted = await confirm({
      title: draft.sendNow ? 'Enviar la campaña ahora' : 'Programar la campaña',
      message: preview
        ? `Llegará a ${preview.recipientCount} clientes${preview.excludedCount > 0 ? ` (${preview.excludedCount} pidieron no recibir mensajes)` : ''}. Cada mensaje puede tener costo en WhatsApp.`
        : 'Los clientes se eligen cuando empiece. Cada mensaje puede tener costo en WhatsApp.',
      confirmLabel: draft.sendNow ? 'Enviar' : 'Programar',
    });
    if (!accepted) return;

    setBusy(true);
    setFormError('');
    try {
      await createMutation.mutateAsync({
        ...audienceRequest(draft),
        name: draft.name.trim(),
        scheduledAt: draft.sendNow ? null : new Date(draft.scheduledAt).toISOString(),
      });
      toast.success(draft.sendNow ? 'Campaña en camino: se envía en segundo plano.' : 'Campaña programada.');
      setDraft(null);
      setPage(1);
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, 'No se pudo crear la campaña.'));
    } finally {
      setBusy(false);
    }
  };

  const cancelCampaign = async (campaign: CampaignDto) => {
    const accepted = await confirm({
      title: `Cancelar «${campaign.name}»`,
      message: 'Los mensajes que no se hayan enviado se cancelan. Los enviados no se pueden recuperar.',
      confirmLabel: 'Cancelar campaña',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    try {
      const updated = await cancelMutation.mutateAsync(campaign.id);
      toast.success('Campaña cancelada.');
      if (detail?.id === updated.id) setDetail(updated);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar la campaña.'));
    }
  };

  const updateDraft = (changes: Partial<Draft>) => {
    if (!draft) return;
    setDraft({ ...draft, ...changes });
    // The audience or the text changed: the preview no longer applies.
    if ('templateId' in changes || 'audienceKind' in changes || 'minimumOrders' in changes || 'days' in changes) setPreview(null);
  };

  return (
    <div className="card">
      <div className="card-body">
        <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
          <div>
            <h4 className="header-title mb-1">Campañas</h4>
            <p className="text-muted small mb-0">Se envían en segundo plano y nunca llegan a quien escribió BAJA.</p>
          </div>
          <button type="button" className="btn btn-primary" onClick={() => void openNew()}>
            <i className="ri-megaphone-line me-1" aria-hidden="true"></i>Nueva campaña
          </button>
        </div>

        {error && <div className="alert alert-danger">{error}</div>}
        {campaigns.error && <div className="alert alert-danger">{getApiErrorMessage(campaigns.error, 'No se pudieron cargar las campañas.')}</div>}
        {campaigns.isPending ? (
          <p className="text-muted mb-0">Cargando…</p>
        ) : !data || (data.items ?? []).length === 0 ? (
          <p className="text-muted mb-0">Todavía no has enviado campañas.</p>
        ) : (
          <>
            <div className="table-responsive">
              <table className="table table-sm align-middle mb-0">
                <thead>
                  <tr>
                    <th>Campaña</th>
                    <th className="d-none d-sm-table-cell">Estado</th>
                    <th className="d-none d-md-table-cell">Envío</th>
                    <th className="d-none d-md-table-cell">Resultado</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {(data.items ?? []).map((campaign) => (
                    <tr key={campaign.id}>
                      <td>
                        <button type="button" className="btn btn-link p-0 fw-semibold text-decoration-none text-start" onClick={() => setDetail(campaign)}>
                          {campaign.name}
                        </button>
                        <span className={`${CAMPAIGN_STATUS[campaign.status].className} ms-2 d-sm-none`}>{CAMPAIGN_STATUS[campaign.status].label}</span>
                        <div className="small text-muted">{describeAudience(campaign)}</div>
                        {/* On a phone the date and the result go under the name. */}
                        <div className="small text-muted d-md-none">
                          {formatDateTime(campaign.startedAt ?? campaign.scheduledAt)}
                          {campaign.status !== CampaignStatus.Scheduled && <> · {progressText(campaign)}</>}
                        </div>
                      </td>
                      <td className="d-none d-sm-table-cell"><span className={CAMPAIGN_STATUS[campaign.status].className}>{CAMPAIGN_STATUS[campaign.status].label}</span></td>
                      <td className="small d-none d-md-table-cell">{formatDateTime(campaign.startedAt ?? campaign.scheduledAt)}</td>
                      <td className="small d-none d-md-table-cell">
                        {campaign.status === CampaignStatus.Scheduled ? '-' : progressText(campaign)}
                        {campaign.excludedCount > 0 && <div className="text-muted">{campaign.excludedCount} con baja, excluidos</div>}
                      </td>
                      <td className="text-end">
                        {(campaign.status === CampaignStatus.Scheduled || campaign.status === CampaignStatus.Sending) && (
                          <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => void cancelCampaign(campaign)}>
                            Cancelar
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {data.totalPages > 1 && (
              <div className="d-flex justify-content-end gap-2 mt-3">
                <button type="button" className="btn btn-sm btn-light" disabled={!data.hasPreviousPage} onClick={() => setPage(page - 1)}>Anterior</button>
                <span className="small align-self-center">Página {data.pageNumber} de {data.totalPages}</span>
                <button type="button" className="btn btn-sm btn-light" disabled={!data.hasNextPage} onClick={() => setPage(page + 1)}>Siguiente</button>
              </div>
            )}
          </>
        )}
      </div>

      {draft && (
        <Modal show onHide={() => !busy && setDraft(null)} centered size="lg" fullscreen="sm-down">
          <form onSubmit={(event) => void submit(event)}>
            <Modal.Header closeButton={!busy}>
              <Modal.Title as="h2" className="h5">Nueva campaña</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              {templates.length === 0 ? (
                <div className="alert alert-info mb-0">
                  Primero crea una plantilla de campaña con el mensaje.
                  <button type="button" className="btn btn-sm btn-primary ms-2" onClick={() => { setDraft(null); onCreateTemplate(); }}>
                    Crear plantilla
                  </button>
                </div>
              ) : (
                <>
                  <div className="row g-2 mb-3">
                    <div className="col-sm-6">
                      <label className="form-label" htmlFor="campaign-name">Nombre</label>
                      <input id="campaign-name" className="form-control" maxLength={120} required autoFocus
                        placeholder="Ej. 2x1 de fin de semana" value={draft.name}
                        onChange={(event) => updateDraft({ name: event.target.value })} />
                    </div>
                    <div className="col-sm-6">
                      <label className="form-label" htmlFor="campaign-template">Mensaje</label>
                      <select id="campaign-template" className="form-select" value={draft.templateId}
                        onChange={(event) => updateDraft({ templateId: event.target.value })}>
                        {templates.map((template) => (
                          <option key={template.id} value={template.id}>{template.name}</option>
                        ))}
                      </select>
                    </div>
                  </div>

                  <fieldset className="mb-3">
                    <legend className="form-label fs-6">¿A quién?</legend>
                    {[
                      { kind: CampaignAudienceKind.Buyers, label: 'Clientes que han comprado' },
                      { kind: CampaignAudienceKind.Lapsed, label: 'Clientes que dejaron de venir' },
                      { kind: CampaignAudienceKind.AllCustomers, label: 'Todos los clientes con teléfono' },
                    ].map((option) => (
                      <div className="form-check" key={option.kind}>
                        <input className="form-check-input" type="radio" name="campaign-audience" id={`audience-${option.kind}`}
                          checked={draft.audienceKind === option.kind}
                          onChange={() => updateDraft({ audienceKind: option.kind, days: option.kind === CampaignAudienceKind.Lapsed ? '60' : '90' })} />
                        <label className="form-check-label" htmlFor={`audience-${option.kind}`}>{option.label}</label>
                      </div>
                    ))}
                    {draft.audienceKind !== CampaignAudienceKind.AllCustomers && (
                      <div className="row g-2 mt-1">
                        <div className="col-6 col-sm-4">
                          <label className="form-label small" htmlFor="campaign-min">Compras mínimas</label>
                          <input id="campaign-min" type="number" min={1} max={1000} className="form-control" value={draft.minimumOrders}
                            onChange={(event) => updateDraft({ minimumOrders: event.target.value })} />
                        </div>
                        <div className="col-6 col-sm-4">
                          <label className="form-label small" htmlFor="campaign-days">
                            {draft.audienceKind === CampaignAudienceKind.Lapsed ? 'Días sin comprar' : 'En los últimos días'}
                          </label>
                          <input id="campaign-days" type="number" min={1} max={3650} className="form-control" value={draft.days}
                            required={draft.audienceKind === CampaignAudienceKind.Lapsed}
                            placeholder={draft.audienceKind === CampaignAudienceKind.Buyers ? 'Siempre' : undefined}
                            onChange={(event) => updateDraft({ days: event.target.value })} />
                        </div>
                      </div>
                    )}
                  </fieldset>

                  <fieldset className="mb-3">
                    <legend className="form-label fs-6">¿Cuándo?</legend>
                    <div className="d-flex flex-wrap align-items-center gap-3">
                      <div className="form-check">
                        <input className="form-check-input" type="radio" name="campaign-when" id="when-now" checked={draft.sendNow}
                          onChange={() => updateDraft({ sendNow: true })} />
                        <label className="form-check-label" htmlFor="when-now">Ahora</label>
                      </div>
                      <div className="form-check">
                        <input className="form-check-input" type="radio" name="campaign-when" id="when-later" checked={!draft.sendNow}
                          onChange={() => updateDraft({ sendNow: false })} />
                        <label className="form-check-label" htmlFor="when-later">Programar</label>
                      </div>
                      {!draft.sendNow && (
                        <input type="datetime-local" className="form-control w-auto" aria-label="Fecha y hora de envío" required
                          value={draft.scheduledAt} onChange={(event) => updateDraft({ scheduledAt: event.target.value })} />
                      )}
                    </div>
                  </fieldset>

                  <div className="border rounded p-2 bg-body-tertiary">
                    <div className="d-flex flex-wrap justify-content-between align-items-center gap-2">
                      <span className="fw-semibold">Vista previa</span>
                      <button type="button" className="btn btn-sm btn-soft-primary" onClick={() => void runPreview()} disabled={busy || !draft.templateId}>
                        {busy && !preview ? 'Calculando…' : preview ? 'Actualizar' : '¿A cuántos llega?'}
                      </button>
                    </div>
                    {preview && (
                      <div className="mt-2" aria-live="polite">
                        <p className="mb-2">
                          Llega a <strong>{preview.recipientCount}</strong> clientes
                          {preview.excludedCount > 0 && <> · {preview.excludedCount} excluidos por baja</>}.
                        </p>
                        {preview.truncated && (
                          <div className="alert alert-warning py-2">
                            Coinciden más de {preview.maxRecipients} clientes: se enviará a los primeros {preview.maxRecipients}.
                          </div>
                        )}
                        {!preview.usesApprovedTemplate && (
                          <div className="alert alert-warning py-2">
                            Esta plantilla no tiene nombre de Meta: solo llegará a quien te escribió en las últimas 24 horas.
                          </div>
                        )}
                        {preview.samples.map((sample) => (
                          <div key={sample.phone} className="border rounded bg-body p-2 mb-1 small">
                            <div className="text-muted">{sample.recipientName ?? 'Cliente'} · {sample.phone}</div>
                            {sample.text}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </>
              )}
              {formError && <div className="alert alert-danger mt-3 mb-0">{formError}</div>}
            </Modal.Body>
            {templates.length > 0 && (
              <Modal.Footer>
                <button type="button" className="btn btn-light" onClick={() => setDraft(null)} disabled={busy}>Cancelar</button>
                <button type="submit" className="btn btn-primary" disabled={busy}>
                  {draft.sendNow ? 'Enviar campaña' : 'Programar'}
                </button>
              </Modal.Footer>
            )}
          </form>
        </Modal>
      )}

      {detail && <CampaignDetail campaign={detail} onClose={() => setDetail(null)} />}
    </div>
  );
}

function CampaignDetail({ campaign, onClose }: { campaign: CampaignDto; onClose: () => void }) {
  const [page, setPage] = useState(1);
  const query = useCampaignMessages(campaign.id, page);
  const messages = query.data;
  const error = query.error ? getApiErrorMessage(query.error, 'No se pudieron cargar los mensajes.') : '';

  return (
    <Modal show onHide={onClose} centered size="lg" fullscreen="sm-down">
      <Modal.Header closeButton>
        <Modal.Title as="h2" className="h5">{campaign.name}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <p className="mb-1"><span className={CAMPAIGN_STATUS[campaign.status].className}>{CAMPAIGN_STATUS[campaign.status].label}</span></p>
        <p className="small text-muted mb-3">
          {describeAudience(campaign)} · plantilla {campaign.templateName ?? '-'} · {campaign.createdBy ?? ''}
          {' · '}programada {formatDateTime(campaign.scheduledAt)}
        </p>
        {error && <div className="alert alert-danger">{error}</div>}
        {!messages ? (
          <p className="text-muted mb-0">Cargando…</p>
        ) : (messages.items ?? []).length === 0 ? (
          <p className="text-muted mb-0">
            {campaign.status === CampaignStatus.Scheduled ? 'Los clientes se eligen cuando empiece.' : 'No hubo clientes que cumplieran el filtro.'}
          </p>
        ) : (
          <>
            <div className="table-responsive">
              <table className="table table-sm align-middle mb-0">
                <thead>
                  <tr>
                    <th>Cliente</th>
                    <th>Estado</th>
                    <th>Detalle</th>
                  </tr>
                </thead>
                <tbody>
                  {(messages.items ?? []).map((message) => (
                    <tr key={message.id}>
                      <td>
                        {message.recipientName ?? 'Cliente'}
                        <div className="small text-muted">{message.phone}</div>
                      </td>
                      <td><span className={MESSAGE_STATUS[message.status].className}>{MESSAGE_STATUS[message.status].label}</span></td>
                      <td className="small">
                        {message.readAt ? `Leído ${formatDateTime(message.readAt)}`
                          : message.deliveredAt ? `Entregado ${formatDateTime(message.deliveredAt)}`
                            : message.sentAt ? `Enviado ${formatDateTime(message.sentAt)}`
                              : message.lastError ?? (message.attempts > 0 ? `Reintento ${formatDateTime(message.nextAttemptAt)}` : '-')}
                        {message.lastError && message.sentAt && <div className="text-danger">{message.lastError}</div>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {messages.totalPages > 1 && (
              <div className="d-flex justify-content-end gap-2 mt-2">
                <button type="button" className="btn btn-sm btn-light" disabled={!messages.hasPreviousPage} onClick={() => setPage(page - 1)}>Anterior</button>
                <span className="small align-self-center">{messages.pageNumber} / {messages.totalPages}</span>
                <button type="button" className="btn btn-sm btn-light" disabled={!messages.hasNextPage} onClick={() => setPage(page + 1)}>Siguiente</button>
              </div>
            )}
          </>
        )}
      </Modal.Body>
    </Modal>
  );
}
