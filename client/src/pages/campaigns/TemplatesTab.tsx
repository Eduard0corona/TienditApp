import { useCallback, useEffect, useRef, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { messagingApi } from '../../api/messagingApi';
import { getApiErrorMessage } from '../../api/errors';
import { useToast } from '../../hooks/useToast';
import { MessagePurpose, type MessageTemplateDto } from '../../types';
import { PLACEHOLDER_HELP } from './campaignLabels';

const PURPOSE_LABEL: Record<MessagePurpose, string> = {
  [MessagePurpose.Campaign]: 'Campaña',
  [MessagePurpose.AppointmentReminder]: 'Recordatorio de cita',
  [MessagePurpose.AppointmentNotice]: 'Aviso de cita',
};

// Appointment notices are written by the system: no template uses them.
const PLACEHOLDERS: Record<MessagePurpose, string[]> = {
  [MessagePurpose.Campaign]: ['nombre', 'negocio'],
  [MessagePurpose.AppointmentReminder]: ['nombre', 'negocio', 'servicio', 'fecha', 'hora'],
  [MessagePurpose.AppointmentNotice]: [],
};

type Draft = {
  id?: string;
  name: string;
  purpose: MessagePurpose;
  body: string;
  providerTemplateName: string;
  languageCode: string;
  isActive: boolean;
};

const EMPTY: Draft = { name: '', purpose: MessagePurpose.Campaign, body: '', providerTemplateName: '', languageCode: 'es_MX', isActive: true };

/** The order the placeholders reach a template approved in Meta: {nombre} → {{1}}… */
function parameterOrder(body: string): string[] {
  const seen: string[] = [];
  for (const match of body.matchAll(/\{([a-zA-Z_]+)\}/g)) {
    const name = match[1].toLowerCase();
    if (!seen.includes(name)) seen.push(name);
  }
  return seen;
}

export default function TemplatesTab() {
  const toast = useToast();
  const [templates, setTemplates] = useState<MessageTemplateDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [draft, setDraft] = useState<Draft | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');
  const bodyRef = useRef<HTMLTextAreaElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await messagingApi.getTemplates();
      setTemplates(data);
      setError('');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar las plantillas.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const insertPlaceholder = (name: string) => {
    if (!draft) return;
    const token = `{${name}}`;
    const input = bodyRef.current;
    const start = input?.selectionStart ?? draft.body.length;
    const end = input?.selectionEnd ?? draft.body.length;
    const body = `${draft.body.slice(0, start)}${token}${draft.body.slice(end)}`;
    setDraft({ ...draft, body });
    requestAnimationFrame(() => {
      input?.focus();
      input?.setSelectionRange(start + token.length, start + token.length);
    });
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!draft || saving) return;
    setSaving(true);
    setFormError('');
    try {
      const payload = {
        name: draft.name.trim(),
        purpose: draft.purpose,
        body: draft.body.trim(),
        providerTemplateName: draft.providerTemplateName.trim() || undefined,
        languageCode: draft.languageCode.trim() || undefined,
        isActive: draft.isActive,
      };
      if (draft.id) await messagingApi.updateTemplate(draft.id, payload);
      else await messagingApi.createTemplate(payload);
      toast.success(draft.id ? 'Plantilla guardada.' : 'Plantilla creada.');
      setDraft(null);
      await load();
    } catch (err: unknown) {
      setFormError(getApiErrorMessage(err, 'No se pudo guardar la plantilla.'));
    } finally {
      setSaving(false);
    }
  };

  const order = draft ? parameterOrder(draft.body) : [];

  return (
    <div className="card">
      <div className="card-body">
        <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
          <div>
            <h4 className="header-title mb-1">Plantillas</h4>
            <p className="text-muted small mb-0">
              Fuera de las 24 horas desde el último mensaje del cliente, WhatsApp solo entrega plantillas aprobadas en Meta.
            </p>
          </div>
          <button type="button" className="btn btn-primary" onClick={() => { setDraft({ ...EMPTY }); setFormError(''); }}>
            <i className="ri-add-line me-1" aria-hidden="true"></i>Nueva plantilla
          </button>
        </div>

        {error && <div className="alert alert-danger">{error}</div>}
        {loading ? (
          <p className="text-muted mb-0">Cargando…</p>
        ) : templates.length === 0 ? (
          <p className="text-muted mb-0">Todavía no hay plantillas. Crea una para tu primera campaña.</p>
        ) : (
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead>
                <tr>
                  <th>Nombre</th>
                  <th>Uso</th>
                  <th>Mensaje</th>
                  <th>Meta</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {templates.map((template) => (
                  <tr key={template.id} className={template.isActive ? '' : 'text-muted'}>
                    <td className="fw-semibold">
                      {template.name}
                      {!template.isActive && <span className="badge bg-secondary-subtle text-secondary-emphasis ms-2">Inactiva</span>}
                    </td>
                    <td>{PURPOSE_LABEL[template.purpose]}</td>
                    <td className="small" style={{ maxWidth: 360 }}>{template.body}</td>
                    <td className="small">
                      {template.providerTemplateName
                        ? <code>{template.providerTemplateName}</code>
                        : <span className="text-warning-emphasis">Solo 24 h</span>}
                    </td>
                    <td className="text-end">
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        onClick={() => {
                          setFormError('');
                          setDraft({
                            id: template.id,
                            name: template.name,
                            purpose: template.purpose,
                            body: template.body,
                            providerTemplateName: template.providerTemplateName ?? '',
                            languageCode: template.languageCode,
                            isActive: template.isActive,
                          });
                        }}
                      >
                        Editar
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {draft && (
        <Modal show onHide={() => !saving && setDraft(null)} centered size="lg" fullscreen="sm-down">
          <form onSubmit={(event) => void save(event)}>
            <Modal.Header closeButton={!saving}>
              <Modal.Title as="h2" className="h5">{draft.id ? 'Editar plantilla' : 'Nueva plantilla'}</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              <div className="row g-2 mb-3">
                <div className="col-sm-7">
                  <label className="form-label" htmlFor="template-name">Nombre</label>
                  <input id="template-name" className="form-control" maxLength={100} required value={draft.name}
                    onChange={(event) => setDraft({ ...draft, name: event.target.value })} autoFocus />
                </div>
                <div className="col-sm-5">
                  <label className="form-label" htmlFor="template-purpose">Uso</label>
                  <select id="template-purpose" className="form-select" value={draft.purpose} disabled={!!draft.id}
                    onChange={(event) => setDraft({ ...draft, purpose: Number(event.target.value) as MessagePurpose })}>
                    <option value={MessagePurpose.Campaign}>Campaña</option>
                    <option value={MessagePurpose.AppointmentReminder}>Recordatorio de cita</option>
                  </select>
                </div>
              </div>
              <label className="form-label" htmlFor="template-body">Mensaje</label>
              <div className="d-flex flex-wrap gap-1 mb-1">
                {PLACEHOLDERS[draft.purpose].map((name) => (
                  <button key={name} type="button" className="btn btn-sm btn-soft-primary" title={PLACEHOLDER_HELP[name]}
                    onClick={() => insertPlaceholder(name)}>
                    {`{${name}}`}
                  </button>
                ))}
              </div>
              <textarea id="template-body" ref={bodyRef} className="form-control" rows={4} maxLength={1000} required
                value={draft.body} onChange={(event) => setDraft({ ...draft, body: event.target.value })}
                placeholder={draft.purpose === MessagePurpose.Campaign
                  ? 'Hola {nombre}, este fin de semana tenemos 2x1 en {negocio}.'
                  : 'Hola {nombre}, te recordamos tu cita de {servicio} el {fecha} a las {hora}.'} />
              <div className="form-text">{draft.body.length}/1000. Incluye cómo darse de baja: «Escribe BAJA si no quieres recibir mensajes».</div>

              <div className="row g-2 mt-2">
                <div className="col-sm-7">
                  <label className="form-label" htmlFor="template-provider">Plantilla aprobada en Meta (opcional)</label>
                  <input id="template-provider" className="form-control" maxLength={100} value={draft.providerTemplateName}
                    placeholder="promo_fin_de_semana" pattern="[a-z0-9_]+"
                    onChange={(event) => setDraft({ ...draft, providerTemplateName: event.target.value })} />
                </div>
                <div className="col-sm-5">
                  <label className="form-label" htmlFor="template-language">Idioma</label>
                  <input id="template-language" className="form-control" maxLength={10} value={draft.languageCode}
                    onChange={(event) => setDraft({ ...draft, languageCode: event.target.value })} />
                </div>
              </div>
              {order.length > 0 && (
                <div className="form-text">
                  En Meta, las variables van en este orden: {order.map((name, index) => `{{${index + 1}}} = {${name}}`).join(', ')}.
                </div>
              )}
              {draft.id && (
                <div className="form-check form-switch mt-3">
                  <input id="template-active" type="checkbox" role="switch" className="form-check-input" checked={draft.isActive}
                    onChange={(event) => setDraft({ ...draft, isActive: event.target.checked })} />
                  <label className="form-check-label" htmlFor="template-active">Activa (se puede elegir en campañas nuevas)</label>
                </div>
              )}
              {formError && <div className="alert alert-danger mt-3 mb-0">{formError}</div>}
            </Modal.Body>
            <Modal.Footer>
              <button type="button" className="btn btn-light" onClick={() => setDraft(null)} disabled={saving}>Cancelar</button>
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? 'Guardando…' : 'Guardar'}</button>
            </Modal.Footer>
          </form>
        </Modal>
      )}
    </div>
  );
}
