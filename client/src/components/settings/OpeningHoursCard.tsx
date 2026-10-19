import { useState } from 'react';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, TenantCapability, type OpeningIntervalDto } from '../../types';
import { BUSINESS_TIME_ZONES, DEFAULT_TIME_ZONE } from '../../utils/dates';
import { dayName, firstDayMissingTime, fromDraft, sameHours, toDraft, type DayDraft } from '../../utils/openingHours';
import { hasCapability } from '../../utils/tenantCapabilities';
import WeeklyHoursFields from './WeeklyHoursFields';

interface OpeningHoursCardProps {
  /** Special days ahead (SpecialDaysCard): without weekly hours the menu still shows as closed on them. */
  upcomingSpecialDays?: number;
  /** After the hours are saved or removed: the special days read them for their suggestions. */
  onSaved?: () => void;
}

/**
 * Horario in Mi negocio (owners and admins): the days and hours the business attends, up to two openings a day, in
 * its time zone. The public menu shows them and, outside them, takes no pickup or delivery orders (the API decides).
 * Without hours the menu shows as closed only on its special days.
 */
export default function OpeningHoursCard({ upcomingSpecialDays = 0, onSaved }: OpeningHoursCardProps) {
  const { tenant, capabilities, refreshTenant } = useTenant();
  const { confirm } = useConfirm();
  const toast = useToast();
  const [draft, setDraft] = useState<DayDraft[] | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  if (!tenant) return null;

  const saved = tenant.openingHours ?? [];
  const week = draft ?? toDraft(saved);
  const changed = draft !== null && !sameHours(fromDraft(draft), saved);
  const hasHours = saved.length > 0;
  const zone = BUSINESS_TIME_ZONES.find((item) => item.id === (tenant.timeZone || DEFAULT_TIME_ZONE))?.label;
  const menuOn = (tenant.onlineMenuMode ?? OnlineMenuMode.Off) !== OnlineMenuMode.Off;
  const takesOrders = tenant.onlineMenuMode === OnlineMenuMode.Orders;
  const hasTables = hasCapability(capabilities, TenantCapability.Food);

  const edit = (next: DayDraft[]) => {
    setDraft(next);
    setError('');
  };

  const send = async (intervals: OpeningIntervalDto[], success: string) => {
    setSaving(true);
    setError('');
    try {
      await apiRequest('put', '/api/v1/auth/my-tenant/opening-hours', { body: { intervals } });
      await refreshTenant();
      onSaved?.();
      setDraft(null);
      toast.success(success);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar el horario.'));
    } finally {
      setSaving(false);
    }
  };

  const save = async () => {
    const missing = firstDayMissingTime(week);
    if (missing !== null) {
      setError(`Escribe a qué hora abres y cierras el ${dayName(missing)}.`);
      return;
    }
    const intervals = fromDraft(week);
    if (intervals.length === 0) {
      setError('Marca al menos un día abierto. Para dejar de mostrar tu horario, usa Quitar horario.');
      return;
    }
    await send(intervals, menuOn ? 'Horario guardado: tu menú en línea ya lo muestra.' : 'Horario guardado.');
  };

  const remove = async () => {
    const accepted = await confirm({
      title: 'Quitar el horario',
      message: upcomingSpecialDays === 0
        ? 'Tu menú en línea dejará de mostrar tu horario y ya no aparecerá como cerrado.'
        : 'Tu menú en línea dejará de mostrar tu horario de la semana y solo se verá cerrado en tus días especiales.',
      confirmLabel: 'Quitar horario',
      danger: true,
    });
    if (accepted) await send([], 'Horario quitado: tu menú en línea ya no lo muestra.');
  };

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-1">Horario</h4>
        <p className="text-muted small mb-3">
          Los días y horas en que atiendes, en la hora de tu negocio{zone ? `: ${zone}` : ''}. Tus clientes los ven en tu
          menú en línea.
        </p>
        {!hasHours && draft === null && (
          <p className="small mb-3">
            {upcomingSpecialDays === 0
              ? 'Aún no tienes horario: tu menú en línea no muestra horas y nunca aparece como cerrado.'
              : 'Aún no tienes horario: tu menú en línea no muestra horas y solo aparece como cerrado en tus días especiales.'}
            {' '}Enciende los días que abres.
          </p>
        )}

        <WeeklyHoursFields week={week} onChange={edit} idPrefix="opening-hours" disabled={saving} />

        {takesOrders && (
          <p className="small text-muted mt-3 mb-0">
            Fuera de tu horario el menú se puede ver, pero no recibe pedidos para recoger o a domicilio
            {hasTables
              ? '; los pedidos desde el QR de una mesa sí llegan fuera de horario, porque el cliente ya está en tu negocio, '
                + 'pero no en los días especiales que cierras'
              : ''}.
          </p>
        )}
        {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}

        <div className="d-flex flex-wrap justify-content-end gap-2 mt-3">
          {hasHours && (
            <button type="button" className="btn btn-outline-danger me-auto" onClick={() => void remove()} disabled={saving}>
              Quitar horario
            </button>
          )}
          {changed && (
            <button type="button" className="btn btn-light" onClick={() => { setDraft(null); setError(''); }} disabled={saving}>
              Descartar
            </button>
          )}
          <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={!changed || saving}>
            {saving ? 'Guardando…' : 'Guardar horario'}
          </button>
        </div>
      </div>
    </div>
  );
}
