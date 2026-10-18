import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { appointmentsApi, type OnlineBookingSettings } from '../../api/appointmentsApi';
import { getApiErrorMessage } from '../../api/errors';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { TenantCapability, type TenantProfileDto } from '../../types';
import { bookingPath } from '../../utils/onlineBooking';
import { hasCapability } from '../../utils/tenantCapabilities';

/**
 * Publishing or turning off the menu, or giving the business its hours, changes whether customers book: asked again with
 * each.
 */
const settingsKey = (tenant: TenantProfileDto | null | undefined) =>
  ['online-booking-settings', tenant?.onlineMenuMode, (tenant?.openingHours?.length ?? 0) > 0];

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

function OnlineBookingForm({ settings }: { settings: OnlineBookingSettings }) {
  const { tenant } = useTenant();
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = useId();
  const [enabled, setEnabled] = useState(settings.enabled);
  const [capacity, setCapacity] = useState(String(settings.capacity));
  const [saving, setSaving] = useState(false);
  const parsed = Number(capacity);
  const capacityValid = Number.isInteger(parsed) && parsed >= 1 && parsed <= settings.maxCapacity;
  const dirty = enabled !== settings.enabled || parsed !== settings.capacity;

  const save = async () => {
    if (!capacityValid) return;
    setSaving(true);
    try {
      const saved = await appointmentsApi.updateOnlineBooking(enabled, parsed);
      queryClient.setQueryData(settingsKey(tenant), saved);
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

      <div className="mb-3" style={{ maxWidth: '16rem' }}>
        <label className="form-label" htmlFor={`${ids}-capacity`}>Citas a la vez</label>
        <input
          id={`${ids}-capacity`}
          className={`form-control ${capacityValid ? '' : 'is-invalid'}`}
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
 * attends at once, and what is missing for it (owners and admins of a business with the agenda). The free times come from
 * the business's hours and the duration of each service; the business accepts or declines each request in the Agenda.
 */
export default function OnlineBookingCard() {
  const { tenant, capabilities } = useTenant();
  const titleId = useId();
  const withAgenda = hasCapability(capabilities, TenantCapability.Appointments);
  const settings = useQuery({
    queryKey: settingsKey(tenant),
    queryFn: ({ signal }) => appointmentsApi.getOnlineBooking(signal),
    enabled: !!tenant && withAgenda,
  });
  if (!tenant || !withAgenda) return null;

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
            key={`${settings.data.enabled}|${settings.data.capacity}|${settings.data.booksOnline}|${settings.data.missing.join('|')}`}
            settings={settings.data}
          />
        )}
      </div>
    </section>
  );
}
