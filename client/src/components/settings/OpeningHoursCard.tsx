import { useState } from 'react';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, TenantCapability, type DayOfWeek, type OpeningIntervalDto } from '../../types';
import { BUSINESS_TIME_ZONES, DEFAULT_TIME_ZONE } from '../../utils/dates';
import {
  MAX_RANGES_PER_DAY,
  dayLabel,
  dayName,
  firstDayMissingTime,
  fromDraft,
  sameHours,
  setDayOpen,
  toDraft,
  type DayDraft,
  type TimeRange,
} from '../../utils/openingHours';
import { hasCapability } from '../../utils/tenantCapabilities';
import './OpeningHoursCard.css';

/** What an opening means when it is not plain: the API reads a closing at or before the opening as the next day. */
function rangeHint(range: TimeRange): string | null {
  if (!range.opens || !range.closes) return null;
  if (range.opens === range.closes) return 'Abierto las 24 horas';
  if (range.closes === '00:00') return 'Cierra a medianoche';
  return range.closes < range.opens ? 'Cierra al día siguiente' : null;
}

/**
 * Horario in Mi negocio (owners and admins): the days and hours the business attends, up to two openings a day, in
 * its time zone. The public menu shows them and, outside them, takes no pickup or delivery orders (the API decides).
 * Without hours the menu never shows as closed.
 */
export default function OpeningHoursCard() {
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
  const changeDay = (day: DayOfWeek, change: (ranges: TimeRange[]) => TimeRange[]) =>
    edit(week.map((item) => (item.day === day ? { ...item, ranges: change(item.ranges) } : item)));
  const changeRange = (day: DayOfWeek, index: number, patch: Partial<TimeRange>) =>
    changeDay(day, (ranges) => ranges.map((range, position) => (position === index ? { ...range, ...patch } : range)));

  const send = async (intervals: OpeningIntervalDto[], success: string) => {
    setSaving(true);
    setError('');
    try {
      await apiRequest('put', '/api/v1/auth/my-tenant/opening-hours', { body: { intervals } });
      await refreshTenant();
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
      message: 'Tu menú en línea dejará de mostrar tu horario y ya no aparecerá como cerrado.',
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
            Aún no tienes horario: tu menú en línea no muestra horas y nunca aparece como cerrado. Enciende los días que
            abres.
          </p>
        )}

        <fieldset disabled={saving}>
          <legend className="visually-hidden">Días y horas</legend>
          <ul className="opening-hours-week list-unstyled mb-0">
            {week.map((item) => {
              const name = dayName(item.day);
              const switchId = `opening-hours-day-${item.day}`;
              return (
                <li key={item.day} className="opening-hours-day">
                  <div className="form-check form-switch opening-hours-day-name">
                    <input
                      id={switchId}
                      className="form-check-input"
                      type="checkbox"
                      role="switch"
                      checked={item.open}
                      onChange={(event) => edit(setDayOpen(week, item.day, event.target.checked))}
                    />
                    <label className="form-check-label" htmlFor={switchId}>{dayLabel(item.day)}</label>
                  </div>
                  {item.open ? (
                    <div className="opening-hours-ranges">
                      {item.ranges.map((range, index) => {
                        const which = index === 0 ? '' : ', segundo horario';
                        const hint = rangeHint(range);
                        return (
                          <div key={index} className="opening-hours-range">
                            <input
                              type="time"
                              className="form-control form-control-sm"
                              aria-label={`Abre el ${name}${which}`}
                              value={range.opens}
                              onChange={(event) => changeRange(item.day, index, { opens: event.target.value })}
                            />
                            <span className="text-muted small" aria-hidden="true">a</span>
                            <input
                              type="time"
                              className="form-control form-control-sm"
                              aria-label={`Cierra el ${name}${which}`}
                              value={range.closes}
                              onChange={(event) => changeRange(item.day, index, { closes: event.target.value })}
                            />
                            {index > 0 && (
                              <button
                                type="button"
                                className="btn btn-sm btn-light"
                                aria-label={`Quitar el segundo horario del ${name}`}
                                title="Quitar este horario"
                                onClick={() => changeDay(item.day, (ranges) => ranges.filter((_, position) => position !== index))}
                              >
                                <i className="ri-close-line" aria-hidden="true"></i>
                              </button>
                            )}
                            {hint && <span className="small text-muted opening-hours-hint">{hint}</span>}
                            {index === item.ranges.length - 1 && item.ranges.length < MAX_RANGES_PER_DAY && (
                              <button
                                type="button"
                                className="btn btn-link btn-sm opening-hours-add"
                                aria-label={`Agregar horario al ${name}`}
                                onClick={() => changeDay(item.day, (ranges) => [...ranges, { opens: '', closes: '' }])}
                              >
                                <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar horario
                              </button>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  ) : (
                    <span className="small text-muted opening-hours-closed">Cerrado</span>
                  )}
                </li>
              );
            })}
          </ul>
        </fieldset>

        {takesOrders && (
          <p className="small text-muted mt-3 mb-0">
            Fuera de tu horario el menú se puede ver, pero no recibe pedidos para recoger o a domicilio
            {hasTables ? '; los pedidos desde el QR de una mesa sí llegan, porque el cliente ya está en tu negocio' : ''}.
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
