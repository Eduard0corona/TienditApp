import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { type CreditReminders, useCreditReminders, useUpdateCreditReminders } from '../../api/customerCredit';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { describeTerm, previewReminder, reminderEveryDaysProblem } from './customerAccount';

interface CreditRemindersModalProps {
  onClose: () => void;
}

/**
 * The WhatsApp reminders of what customers owe past their term (Fase 26C; owners and admins): on or off, every how many
 * days and with which message. The server sends them only during the business's day and never to who wrote BAJA.
 */
export default function CreditRemindersModal({ onClose }: CreditRemindersModalProps) {
  const titleId = useId();
  const { data, error } = useCreditReminders();

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">Recordatorios del fiado</Modal.Title>
      </Modal.Header>
      {data
        ? <RemindersForm settings={data} onClose={onClose} />
        : (
          <Modal.Body>
            {error
              ? <div className="alert alert-danger mb-0">{getApiErrorMessage(error, 'No se pudieron cargar los recordatorios.')}</div>
              : <p className="text-muted mb-0">Cargando…</p>}
          </Modal.Body>
        )}
    </Modal>
  );
}

function RemindersForm({ settings, onClose }: { settings: CreditReminders; onClose: () => void }) {
  const ids = { enabled: useId(), every: useId(), template: useId(), problem: useId(), error: useId() };
  const toast = useToast();
  const { tenant } = useTenant();
  const update = useUpdateCreditReminders();
  const [enabled, setEnabled] = useState(settings.enabled);
  const [typedDays, setTypedDays] = useState(String(settings.everyDays));
  const [templateId, setTemplateId] = useState(settings.templateId ?? '');
  const [error, setError] = useState('');
  const problem = reminderEveryDaysProblem(typedDays);
  const selected = settings.templates.find((template) => template.id === templateId);
  // Turning them off never needs WhatsApp; turning them on does.
  const blocked = enabled && !settings.whatsAppConnected;
  const nothingDue = settings.creditTermDays == null && settings.customersWithOwnTerm === 0;

  const save = async () => {
    setError('');
    try {
      const saved = await update.mutateAsync({ enabled, everyDays: Number(typedDays.trim()), templateId: templateId || null });
      toast.success(saved.enabled
        ? `Se recordará lo vencido cada ${describeTerm(saved.everyDays)}.`
        : 'Los recordatorios del fiado están apagados.');
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron guardar los recordatorios.'));
    }
  };

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (!problem && !blocked) void save();
      }}
    >
      <Modal.Body>
        <p className="text-muted small">
          Por WhatsApp, a quien tenga algo vencido: lo que debe y desde cuándo. Se mandan de {settings.sendFrom} a{' '}
          {settings.sendUntil}, nunca a quien escribió BAJA, y dejan de mandarse en cuanto paga.
        </p>
        {!settings.whatsAppConnected && (
          <div className="alert alert-warning py-2">
            Conecta WhatsApp para mandar recordatorios. <Link to="/settings/whatsapp">Ir a WhatsApp</Link>
          </div>
        )}
        {nothingDue && (
          <div className="alert alert-info py-2">Sin plazo nada vence: pon un plazo para que haya a quién recordarle.</div>
        )}
        <div className="form-check form-switch mb-3">
          <input
            id={ids.enabled}
            type="checkbox"
            role="switch"
            className="form-check-input"
            checked={enabled}
            onChange={(event) => setEnabled(event.target.checked)}
            disabled={update.isPending}
          />
          <label className="form-check-label" htmlFor={ids.enabled}>Recordar lo vencido por WhatsApp</label>
        </div>
        <div className="row g-2 mb-3">
          <div className="col-6 col-sm-4">
            <label className="form-label" htmlFor={ids.every}>Cada</label>
            <div className="input-group">
              <input
                id={ids.every}
                className={`form-control ${problem ? 'is-invalid' : ''}`}
                inputMode="numeric"
                autoComplete="off"
                value={typedDays}
                onChange={(event) => setTypedDays(event.target.value)}
                disabled={update.isPending}
                aria-invalid={!!problem}
                aria-describedby={problem ? ids.problem : undefined}
              />
              <span className="input-group-text">días</span>
            </div>
          </div>
          <div className="col-sm-8">
            <label className="form-label" htmlFor={ids.template}>Mensaje</label>
            <select
              id={ids.template}
              className="form-select"
              value={templateId}
              onChange={(event) => setTemplateId(event.target.value)}
              disabled={update.isPending}
            >
              <option value="">Texto predeterminado</option>
              {settings.templates.map((template) => (
                <option key={template.id} value={template.id}>{template.name}</option>
              ))}
            </select>
          </div>
        </div>
        {problem && <div id={ids.problem} className="invalid-feedback d-block mb-2">{problem}</div>}
        <div className="border rounded p-2 bg-body-tertiary small">
          {previewReminder(selected?.body ?? settings.defaultBody, tenant?.name ?? 'Tu negocio')}
          {!selected?.providerTemplateName && (
            <div className="text-warning-emphasis mt-1">
              Sin plantilla aprobada en Meta solo llega a quien te escribió en las últimas 24 horas. Créala en Mensajes ›
              Plantillas, con el uso «Recordatorio del fiado».
            </div>
          )}
        </div>
        {error && <div id={ids.error} className="alert alert-danger py-2 mt-3 mb-0" role="alert">{error}</div>}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose} disabled={update.isPending}>Cancelar</button>
        <button type="submit" className="btn btn-primary" disabled={update.isPending || !!problem || blocked}>
          {update.isPending ? 'Guardando…' : 'Guardar'}
        </button>
      </Modal.Footer>
    </form>
  );
}
