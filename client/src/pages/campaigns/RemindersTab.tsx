import { type ReactNode, useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { messagingApi } from '../../api/messagingApi';
import { getApiErrorMessage } from '../../api/errors';
import { useToast } from '../../hooks/useToast';
import {
  MessagePurpose,
  type MessageTemplateDto,
  type MessagingSettingsDto,
  type OutgoingMessageDto,
  type PaginatedList,
} from '../../types';
import { formatDateTime } from '../../utils/dates';
import { MESSAGE_STATUS } from './campaignLabels';

/** The last messages of one purpose and what happened to each: sent, failed or skipped, with why. */
export function MessageHistory({ title, hint, empty, history }: {
  title: string;
  hint?: ReactNode;
  empty: string;
  history: PaginatedList<OutgoingMessageDto> | null;
}) {
  return (
    <div className="card">
      <div className="card-body">
        <h4 className={`header-title ${hint ? 'mb-1' : 'mb-3'}`}>{title}</h4>
        {hint && <p className="text-muted small mb-3">{hint}</p>}
        {!history || history.items.length === 0 ? (
          <p className="text-muted mb-0">{empty}</p>
        ) : (
          <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead>
                <tr>
                  <th>Cliente</th>
                  <th>Estado</th>
                  <th>Mensaje</th>
                </tr>
              </thead>
              <tbody>
                {history.items.map((message) => (
                  <tr key={message.id}>
                    <td>
                      {message.recipientName ?? 'Cliente'}
                      <div className="small text-muted">{formatDateTime(message.sentAt ?? message.createdAt)}</div>
                    </td>
                    <td>
                      <span className={MESSAGE_STATUS[message.status].className}>{MESSAGE_STATUS[message.status].label}</span>
                      {message.lastError && <div className="small text-muted">{message.lastError}</div>}
                    </td>
                    <td className="small" style={{ maxWidth: 380 }}>{message.body}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * Appointment reminders by WhatsApp: when they go out, with which text, and what happened to each; below them, the
 * notices sent when an appointment is booked, moved, confirmed or cancelled.
 */
export default function RemindersTab() {
  const toast = useToast();
  const [settings, setSettings] = useState<MessagingSettingsDto | null>(null);
  const [templates, setTemplates] = useState<MessageTemplateDto[]>([]);
  const [enabled, setEnabled] = useState(false);
  const [hours, setHours] = useState('24');
  const [templateId, setTemplateId] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [history, setHistory] = useState<PaginatedList<OutgoingMessageDto> | null>(null);
  const [notices, setNotices] = useState<PaginatedList<OutgoingMessageDto> | null>(null);

  const load = useCallback(async () => {
    try {
      const [{ data: current }, { data: list }, { data: sent }, { data: sentNotices }] = await Promise.all([
        messagingApi.getSettings(),
        messagingApi.getTemplates({ purpose: MessagePurpose.AppointmentReminder, includeInactive: false }),
        messagingApi.getMessages({ purpose: MessagePurpose.AppointmentReminder, pageNumber: 1, pageSize: 20 }),
        messagingApi.getMessages({ purpose: MessagePurpose.AppointmentNotice, pageNumber: 1, pageSize: 20 }),
      ]);
      setSettings(current);
      setTemplates(list);
      setHistory(sent);
      setNotices(sentNotices);
      setEnabled(current.appointmentRemindersEnabled);
      setHours(String(current.appointmentReminderHoursBefore));
      setTemplateId(current.appointmentReminderTemplateId ?? '');
      setError('');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron cargar los recordatorios.'));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async () => {
    if (!settings || saving) return;
    setSaving(true);
    setError('');
    try {
      const { data } = await messagingApi.updateReminders({
        enabled,
        hoursBefore: Number(hours) || settings.appointmentReminderHoursBefore,
        templateId: templateId || undefined,
      });
      setSettings(data);
      toast.success(enabled ? 'Recordatorios activados.' : 'Recordatorios guardados.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron guardar los recordatorios.'));
    } finally {
      setSaving(false);
    }
  };

  if (!settings) return error ? <div className="alert alert-danger">{error}</div> : <p className="text-muted">Cargando…</p>;

  const selected = templates.find((template) => template.id === templateId);
  return (
    <>
      <div className="card">
        <div className="card-body">
          <h4 className="header-title mb-1">Recordatorios de citas</h4>
          <p className="text-muted small mb-3">Se envían por WhatsApp antes de cada cita agendada o confirmada.</p>
          {!settings.whatsAppConnected && (
            <div className="alert alert-warning">
              Conecta WhatsApp para enviar recordatorios. <Link to="/settings/whatsapp">Ir a WhatsApp</Link>
            </div>
          )}
          <div className="form-check form-switch mb-3">
            <input id="reminders-enabled" type="checkbox" role="switch" className="form-check-input" checked={enabled}
              onChange={(event) => setEnabled(event.target.checked)} />
            <label className="form-check-label" htmlFor="reminders-enabled">Enviar recordatorios</label>
          </div>
          <div className="row g-2 mb-3">
            <div className="col-6 col-sm-3">
              <label className="form-label" htmlFor="reminders-hours">Horas antes</label>
              <input id="reminders-hours" type="number" className="form-control" min={settings.minReminderHoursBefore}
                max={settings.maxReminderHoursBefore} value={hours} onChange={(event) => setHours(event.target.value)} />
            </div>
            <div className="col-sm-9">
              <label className="form-label" htmlFor="reminders-template">Mensaje</label>
              <select id="reminders-template" className="form-select" value={templateId} onChange={(event) => setTemplateId(event.target.value)}>
                <option value="">Texto predeterminado</option>
                {templates.map((template) => (
                  <option key={template.id} value={template.id}>{template.name}</option>
                ))}
              </select>
            </div>
          </div>
          <div className="border rounded p-2 bg-body-tertiary small mb-3">
            {selected?.body ?? settings.defaultReminderBody}
            {!selected?.providerTemplateName && (
              <div className="text-warning-emphasis mt-1">
                Sin plantilla aprobada en Meta solo llega a quien escribió en las últimas 24 horas.
              </div>
            )}
          </div>
          {error && <div className="alert alert-danger">{error}</div>}
          <div className="d-flex justify-content-end">
            <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={saving}>
              {saving ? 'Guardando…' : 'Guardar'}
            </button>
          </div>
        </div>
      </div>

      <MessageHistory title="Últimos recordatorios" empty="Todavía no se ha enviado ningún recordatorio." history={history} />
      <MessageHistory
        title="Avisos de citas"
        hint="Salen solos al agendar, reprogramar, confirmar o cancelar una cita, aunque el cliente haya pedido no recibir promociones. Son texto libre: WhatsApp solo los entrega a quien escribió en las últimas 24 horas."
        empty="Todavía no se ha enviado ningún aviso."
        history={notices}
      />
    </>
  );
}
