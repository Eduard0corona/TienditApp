import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { type CreditReminders, useCreditReminders, useUpdateCreditReminders } from '../../api/customerCredit';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { describeTerm, dueSoonDaysBeforeProblem, previewReminder, reminderEveryDaysProblem } from './customerAccount';

interface CreditRemindersModalProps {
  onClose: () => void;
}

/**
 * The WhatsApp messages of the fiado (owners and admins): the notice a few days before something comes due (Fase 26E) and
 * the reminders of what customers owe past their term (Fase 26C), each on or off, when and with which message. The server
 * sends them only during the business's day and never to who wrote BAJA.
 */
export default function CreditRemindersModal({ onClose }: CreditRemindersModalProps) {
  const titleId = useId();
  const { data, error } = useCreditReminders();
  const update = useUpdateCreditReminders();
  // Closing while saving would lose a refusal without a word.
  const close = () => !update.isPending && onClose();

  return (
    <Modal show onHide={close} centered aria-labelledby={titleId}>
      <Modal.Header closeButton={!update.isPending}>
        <Modal.Title id={titleId} as="h2" className="h5">Recordatorios del fiado</Modal.Title>
      </Modal.Header>
      {data
        ? <RemindersForm settings={data} update={update} onClose={onClose} />
        : (
          <Modal.Body>
            {error
              ? <div className="alert alert-danger mb-0" role="alert">{getApiErrorMessage(error, 'No se pudieron cargar los recordatorios.')}</div>
              : <p className="text-muted mb-0">Cargando…</p>}
          </Modal.Body>
        )}
    </Modal>
  );
}

/** What the owner chose to say: the saved choices, in their words. */
function describeSaved(saved: CreditReminders): string {
  const parts = [
    saved.dueSoon.enabled ? `se avisará ${describeTerm(saved.dueSoon.daysBefore)} antes de que venza` : null,
    saved.enabled ? `se recordará lo vencido cada ${describeTerm(saved.everyDays)}` : null,
  ].filter((part): part is string => part != null);
  if (parts.length === 0) return 'Los recordatorios del fiado están apagados.';
  const text = parts.join(' y ');
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}.`;
}

function RemindersForm({ settings, update, onClose }: {
  settings: CreditReminders;
  update: ReturnType<typeof useUpdateCreditReminders>;
  onClose: () => void;
}) {
  const ids = { dueSoon: useId(), reminders: useId(), error: useId() };
  const toast = useToast();
  const { tenant } = useTenant();
  const businessName = tenant?.name ?? 'Tu negocio';
  const [enabled, setEnabled] = useState(settings.enabled);
  const [typedDays, setTypedDays] = useState(String(settings.everyDays));
  // Only a template that can still be chosen: any other one reads as the default text.
  const [templateId, setTemplateId] = useState(
    settings.templates.some((template) => template.id === settings.templateId) ? settings.templateId ?? '' : '');
  const dueSoon = settings.dueSoon;
  const [dueSoonEnabled, setDueSoonEnabled] = useState(dueSoon.enabled);
  const [typedDaysBefore, setTypedDaysBefore] = useState(String(dueSoon.daysBefore));
  const [dueSoonTemplateId, setDueSoonTemplateId] = useState(
    dueSoon.templates.some((template) => template.id === dueSoon.templateId) ? dueSoon.templateId ?? '' : '');
  const [error, setError] = useState('');
  const daysProblem = reminderEveryDaysProblem(typedDays, settings.minEveryDays, settings.maxEveryDays);
  const daysBeforeProblem = dueSoonDaysBeforeProblem(typedDaysBefore, dueSoon.minDaysBefore, dueSoon.maxDaysBefore);
  // A message that is off keeps its saved days when what is typed cannot be saved: it neither warns nor blocks the other.
  const problem = enabled ? daysProblem : null;
  const dueSoonProblem = dueSoonEnabled ? daysBeforeProblem : null;
  // Turning them off never needs WhatsApp; turning either on does.
  const blocked = (enabled || dueSoonEnabled) && !settings.whatsAppConnected;
  const nothingDue = settings.creditTermDays == null && settings.customersWithOwnTerm === 0;
  const invalid = !!problem || !!dueSoonProblem;

  const save = async () => {
    setError('');
    try {
      const saved = await update.mutateAsync({
        enabled,
        everyDays: daysProblem ? settings.everyDays : Number(typedDays.trim()),
        templateId: templateId || null,
        dueSoon: {
          enabled: dueSoonEnabled,
          daysBefore: daysBeforeProblem ? dueSoon.daysBefore : Number(typedDaysBefore.trim()),
          templateId: dueSoonTemplateId || null,
        },
      });
      toast.success(describeSaved(saved));
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron guardar los recordatorios.'));
    }
  };

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (!invalid && !blocked) void save();
      }}
    >
      <Modal.Body>
        <p className="text-muted small">
          Se mandan por WhatsApp de {settings.sendFrom} a {settings.sendUntil}, nunca a quien escribió BAJA.{' '}
          <Link to="/campaigns?tab=creditReminders">Ver los que se mandaron</Link>
        </p>
        {!settings.whatsAppConnected && (
          <div className="alert alert-warning py-2">
            Conecta WhatsApp para mandar recordatorios. <Link to="/settings/whatsapp">Ir a WhatsApp</Link>
          </div>
        )}
        {nothingDue && (
          <div className="alert alert-info py-2">Sin plazo, nada vence: pon un plazo para que haya a quién recordarle.</div>
        )}
        <MessageSettings
          idPrefix={ids.dueSoon}
          legend="Antes de que venza"
          hint="A quien va al corriente, unos días antes de su último día para pagar: cuánto vence y cuándo. Una vez por fecha, nunca el mismo día en que se le fió."
          switchLabel="Avisar por WhatsApp antes de que venza"
          checked={dueSoonEnabled}
          onCheck={setDueSoonEnabled}
          daysLabel="Anticipación"
          typedDays={typedDaysBefore}
          onDays={setTypedDaysBefore}
          problem={dueSoonProblem}
          templates={dueSoon.templates}
          templateId={dueSoonTemplateId}
          onTemplate={setDueSoonTemplateId}
          defaultBody={dueSoon.defaultBody}
          businessName={businessName}
          purposeLabel="Aviso antes de vencer"
          disabled={update.isPending}
        />
        <MessageSettings
          idPrefix={ids.reminders}
          legend="Lo vencido"
          hint="A quien tiene algo vencido: lo que debe y desde cuándo. Deja de mandarse en cuanto paga."
          switchLabel="Recordar lo vencido por WhatsApp"
          checked={enabled}
          onCheck={setEnabled}
          daysLabel="Cada"
          typedDays={typedDays}
          onDays={setTypedDays}
          problem={problem}
          templates={settings.templates}
          templateId={templateId}
          onTemplate={setTemplateId}
          defaultBody={settings.defaultBody}
          businessName={businessName}
          purposeLabel="Recordatorio del fiado"
          disabled={update.isPending}
        />
        {error && <div id={ids.error} className="alert alert-danger py-2 mt-3 mb-0" role="alert">{error}</div>}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose} disabled={update.isPending}>Cancelar</button>
        <button type="submit" className="btn btn-primary" disabled={update.isPending || invalid || blocked}>
          {update.isPending ? 'Guardando…' : 'Guardar'}
        </button>
      </Modal.Footer>
    </form>
  );
}

/** One of the two messages: on or off, its days, its template and how it reads. */
function MessageSettings({
  idPrefix, legend, hint, switchLabel, checked, onCheck, daysLabel, typedDays, onDays, problem, templates, templateId,
  onTemplate, defaultBody, businessName, purposeLabel, disabled,
}: {
  idPrefix: string;
  legend: string;
  hint: string;
  switchLabel: string;
  checked: boolean;
  onCheck: (checked: boolean) => void;
  daysLabel: string;
  typedDays: string;
  onDays: (typed: string) => void;
  problem: string | null;
  templates: CreditReminders['templates'];
  templateId: string;
  onTemplate: (id: string) => void;
  defaultBody: string;
  businessName: string;
  /** The use of the template in Campañas › Plantillas. */
  purposeLabel: string;
  disabled: boolean;
}) {
  const ids = {
    enabled: `${idPrefix}-enabled`,
    days: `${idPrefix}-days`,
    template: `${idPrefix}-template`,
    problem: `${idPrefix}-problem`,
    preview: `${idPrefix}-preview`,
  };
  const selected = templates.find((template) => template.id === templateId);

  return (
    <fieldset className="border-top pt-3 mt-3">
      <legend className="h6 mb-1">{legend}</legend>
      <p className="text-muted small mb-2">{hint}</p>
      <div className="form-check form-switch mb-3">
        <input
          id={ids.enabled}
          type="checkbox"
          role="switch"
          className="form-check-input"
          checked={checked}
          onChange={(event) => onCheck(event.target.checked)}
          disabled={disabled}
        />
        <label className="form-check-label" htmlFor={ids.enabled}>{switchLabel}</label>
      </div>
      <div className="row g-2 mb-3">
        <div className="col-6 col-sm-4">
          <label className="form-label" htmlFor={ids.days}>{daysLabel}</label>
          <div className="input-group">
            <input
              id={ids.days}
              className={`form-control ${problem ? 'is-invalid' : ''}`}
              inputMode="numeric"
              autoComplete="off"
              value={typedDays}
              onChange={(event) => onDays(event.target.value)}
              disabled={disabled}
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
            onChange={(event) => onTemplate(event.target.value)}
            disabled={disabled}
            aria-describedby={ids.preview}
          >
            <option value="">Texto predeterminado</option>
            {templates.map((template) => (
              <option key={template.id} value={template.id}>{template.name}</option>
            ))}
          </select>
        </div>
      </div>
      {problem && <div id={ids.problem} className="invalid-feedback d-block mb-2">{problem}</div>}
      <div id={ids.preview} className="border rounded p-2 bg-body-tertiary small">
        <div className="fw-semibold mb-1">Así le llega:</div>
        {previewReminder(selected?.body ?? defaultBody, businessName)}
        {!selected?.providerTemplateName && (
          <div className="text-warning-emphasis mt-1">
            Sin plantilla aprobada en Meta, solo llega a quien te escribió en las últimas 24 horas. Créala en{' '}
            <Link to="/campaigns?tab=templates">Campañas › Plantillas</Link>, con el uso «{purposeLabel}».
          </div>
        )}
      </div>
    </fieldset>
  );
}
