import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { appointmentsApi, type OnlineBookingSettings } from '../../api/appointmentsApi';
import { getApiErrorMessage } from '../../api/errors';
import { onlineBookingSettingsKey, useOnlineBookingSettings } from '../../api/onlineBookingSettings';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { bookingPath } from '../../utils/onlineBooking';

/** Whether customers book now, and when they do not, what is missing (the server's words). */
function BookingNotice({ settings }: { settings: OnlineBookingSettings }) {
  if (settings.booksOnline) {
    return (
      <div className="alert alert-success py-2 small">
        <i className="ri-checkbox-circle-line me-1" aria-hidden="true"></i>
        Tus clientes ya agendan desde tu menú en línea.{' '}
        <Link to={bookingPath(settings.slug)}>Ver tu página de citas</Link>
      </div>
    );
  }
  if (settings.missing.length > 0) {
    return (
      <div className="alert alert-warning py-2 small">
        {settings.enabled ? 'Tus clientes todavía no pueden agendar en línea. Falta:' : 'Para recibir citas en línea, falta también:'}
        <ul className="mb-0 mt-1 ps-3">
          {settings.missing.map((item) => <li key={item}>{item}</li>)}
        </ul>
      </div>
    );
  }
  return <div className="alert alert-secondary py-2 small">Las citas en línea están apagadas: tu menú no muestra «Agenda tu cita».</div>;
}

/** How long before its limit a request still waiting is recalled, as the owner reads it (Fase 30B). */
function reminderLabel(minutes: number): string {
  if (minutes === 0) return 'No recordarme';
  if (minutes === 30) return 'Media hora antes del límite';
  if (minutes === 60) return 'Una hora antes del límite';
  return minutes % 60 === 0 ? `${minutes / 60} horas antes del límite` : `${minutes} minutos antes del límite`;
}

function OnlineBookingForm({ settings }: { settings: OnlineBookingSettings }) {
  const { tenant } = useTenant();
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = useId();
  const [enabled, setEnabled] = useState(settings.enabled);
  const [capacity, setCapacity] = useState(String(settings.capacity));
  // An API of before (while a version is published) sends neither: the hour of always.
  const savedReminder = settings.answerReminderMinutes ?? 60;
  const [reminder, setReminder] = useState(savedReminder);
  const [saving, setSaving] = useState(false);
  const parsed = Number(capacity);
  const capacityValid = Number.isInteger(parsed) && parsed >= 1 && parsed <= settings.maxCapacity;
  const dirty = enabled !== settings.enabled || parsed !== settings.capacity || reminder !== savedReminder;
  // Its own choice stays listed even if this version no longer offers it.
  const reminderChoices = [...new Set([...(settings.answerReminderChoices ?? [30, 60, 120, 180]), savedReminder, 0])];

  const save = async () => {
    if (!capacityValid) return;
    setSaving(true);
    try {
      const saved = await appointmentsApi.updateOnlineBooking(enabled, parsed, reminder);
      queryClient.setQueryData(onlineBookingSettingsKey(tenant), saved);
      toast.success(saved.booksOnline ? 'Guardado: tus clientes ya pueden agendar en línea.' : 'Guardado.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudieron guardar las citas en línea.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <BookingNotice settings={settings} />

      <div className="form-check form-switch mb-3">
        <input
          id={`${ids}-enabled`}
          className="form-check-input"
          type="checkbox"
          role="switch"
          checked={enabled}
          onChange={(event) => setEnabled(event.target.checked)}
        />
        <label className="form-check-label" htmlFor={`${ids}-enabled`}>Recibir citas en línea</label>
        <div className="form-text">Cada solicitud llega a la Agenda como «Por confirmar» hasta que la aceptas o la rechazas.</div>
      </div>

      {settings.staffCount > 0 ? (
        <p className="small mb-3">
          <i className="ri-team-line me-1" aria-hidden="true"></i>
          {settings.staffCount === 1 ? 'Una persona agenda en línea' : `${settings.staffCount} personas agendan en línea`}: las
          horas libres salen de «Quién atiende», abajo, y las citas a la vez ya no se usan.
        </p>
      ) : (
      <div className="mb-3">
        <label className="form-label" htmlFor={`${ids}-capacity`}>Citas a la vez</label>
        <input
          id={`${ids}-capacity`}
          className={`form-control ${capacityValid ? '' : 'is-invalid'}`}
          style={{ maxWidth: '8rem' }}
          type="number"
          inputMode="numeric"
          min={1}
          max={settings.maxCapacity}
          step={1}
          value={capacity}
          onChange={(event) => setCapacity(event.target.value)}
          aria-describedby={`${ids}-capacity-help`}
        />
        <div id={`${ids}-capacity-help`} className={capacityValid ? 'form-text' : 'invalid-feedback'}>
          {capacityValid
            ? 'Cuántas citas atiendes al mismo tiempo: con 2, dos clientes pueden pedir la misma hora.'
            : `Las citas a la vez van de 1 a ${settings.maxCapacity}.`}
        </div>
      </div>
      )}

      <div className="mb-3">
        <label className="form-label" htmlFor={`${ids}-reminder`}>Recordatorio de solicitudes sin responder</label>
        <select
          id={`${ids}-reminder`}
          className="form-select"
          style={{ maxWidth: '20rem' }}
          value={reminder}
          onChange={(event) => setReminder(Number(event.target.value))}
          aria-describedby={`${ids}-reminder-help`}
        >
          {reminderChoices.map((minutes) => (
            <option key={minutes} value={minutes}>{reminderLabel(minutes)}</option>
          ))}
        </select>
        <div id={`${ids}-reminder-help`} className="form-text">
          {reminder === 0
            ? 'Solo te llega el correo de cada solicitud cuando llega.'
            : 'Un correo a quienes reciben los avisos de citas en línea, si una solicitud sigue sin respuesta, para que la respondas a tiempo.'}
        </div>
      </div>

      <div className="d-flex gap-2">
        <button type="button" className="btn btn-primary" disabled={!dirty || !capacityValid || saving} onClick={() => void save()}>
          {saving ? 'Guardando…' : 'Guardar'}
        </button>
        {dirty && (
          <button
            type="button"
            className="btn btn-light"
            disabled={saving}
            onClick={() => {
              setEnabled(settings.enabled);
              setCapacity(String(settings.capacity));
              setReminder(savedReminder);
            }}
          >
            Descartar
          </button>
        )}
      </div>
    </>
  );
}

/**
 * «Citas en línea» (Fase 24D, ADR 0025): whether customers book appointments from the online menu, how many the business
 * attends at once, how long before its limit a request still waiting is recalled by email or not at all (Fase 30B, ADR 0082),
 * and what is missing for it (owners and admins of a business with the agenda). The free times come from
 * the business's hours and the duration of each service; the business accepts or declines each request in the Agenda.
 */
export default function OnlineBookingCard() {
  const { tenant } = useTenant();
  const titleId = useId();
  const settings = useOnlineBookingSettings();
  if (!tenant || !settings.withAgenda) return null;

  return (
    <section className="card" aria-labelledby={titleId}>
      <div className="card-body">
        <h4 id={titleId} className="header-title mt-0 mb-1">Citas en línea</h4>
        <p className="text-muted small mb-3">
          Tus clientes eligen el servicio, el día y una hora libre desde tu menú en línea. Las horas libres salen de tu horario,
          de la duración de cada servicio y de las citas que ya tienes.
        </p>
        {settings.isLoading && <div className="spinner-border spinner-border-sm text-primary" role="status"><span className="visually-hidden">Cargando…</span></div>}
        {settings.isError && (
          <div className="alert alert-danger py-2 small">{getApiErrorMessage(settings.error, 'No se pudieron cargar las citas en línea.')}</div>
        )}
        {settings.data && (
          <OnlineBookingForm
            key={`${settings.data.enabled}|${settings.data.capacity}|${settings.data.answerReminderMinutes}|${settings.data.booksOnline}|${settings.data.staffCount}|${settings.data.missing.join('|')}`}
            settings={settings.data}
          />
        )}
      </div>
    </section>
  );
}
