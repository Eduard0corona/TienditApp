import { Link } from 'react-router-dom';
import type { AppointmentStatus } from '../../types';
import { statusBadge, statusLabel } from '../../pages/appointments/appointmentStatus';
import { formatInBusinessZone, formatTime } from '../../utils/dates';

interface WorkAppointment {
  id: string;
  startAt: string;
  endAt: string;
  staffName?: string | null;
  status: AppointmentStatus;
  atCustomersAddress: boolean;
}

/**
 * The appointments that do the job of a service order that came from a quote (Fase 29F): when, who and how each goes, in the
 * business's time zone, with the Agenda where they are moved or confirmed.
 */
export default function WorkAppointmentList({ appointments }: { appointments: WorkAppointment[] }) {
  return (
    <div>
      <h3 className="h6 mb-1">Citas del trabajo</h3>
      <ul className="list-unstyled small mb-1">
        {appointments.map((appointment) => (
          <li key={appointment.id} className="d-flex flex-wrap align-items-center gap-1 py-1">
            <span>
              {formatInBusinessZone(appointment.startAt, { weekday: 'short', day: 'numeric', month: 'short' })},{' '}
              {formatTime(appointment.startAt)} a {formatTime(appointment.endAt)}
            </span>
            <span className="text-muted">
              · {appointment.staffName || 'Sin asignar'}
              {appointment.atCustomersAddress ? ' · a domicilio' : ''}
            </span>
            <span className={`badge ${statusBadge(appointment.status)}`}>{statusLabel(appointment.status)}</span>
          </li>
        ))}
      </ul>
      <Link className="small" to="/appointments">Ver en la Agenda</Link>
    </div>
  );
}
