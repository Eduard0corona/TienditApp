import { Link } from 'react-router-dom';
import type { WorkAppointmentDto } from '../../types';
import { statusBadge, statusLabel } from '../../pages/appointments/appointmentStatus';
import { formatInBusinessZone, formatTime } from '../../utils/dates';

/**
 * The appointments that do the job of a service order that came from a quote (Fase 29F): when, who and how each goes, in the
 * business's time zone, with the Agenda where they are moved or confirmed (for whoever manages it).
 */
export default function WorkAppointmentList(
  { appointments, canOpenAgenda }: { appointments: WorkAppointmentDto[]; canOpenAgenda: boolean },
) {
  return (
    <div>
      <h3 className="h6 mb-1">Citas del trabajo</h3>
      <ul className="list-unstyled small mb-1">
        {appointments.map((appointment) => (
          <li key={appointment.id} className="py-1">
            <div>
              {formatInBusinessZone(appointment.startAt, { weekday: 'short', day: 'numeric', month: 'short' })},{' '}
              {formatTime(appointment.startAt)} a {formatTime(appointment.endAt)}
            </div>
            <div className="d-flex flex-wrap align-items-center gap-1">
              <span className="text-muted">
                {appointment.staffName || 'Sin asignar'}
                {appointment.atCustomersAddress ? ' · a domicilio' : ''}
              </span>
              <span className={`badge ${statusBadge(appointment.status)}`}>{statusLabel(appointment.status)}</span>
            </div>
          </li>
        ))}
      </ul>
      {canOpenAgenda && <Link className="small" to="/appointments">Ver en la Agenda</Link>}
    </div>
  );
}
