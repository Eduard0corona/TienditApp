import type { AppointmentStatus } from '../../types';
import { AppointmentStatus as AppointmentStatusConst } from '../../types';

export const statusLabel = (status: AppointmentStatus) => {
  switch (status) {
    case AppointmentStatusConst.Scheduled: return 'Programada';
    case AppointmentStatusConst.Confirmed: return 'Confirmada';
    case AppointmentStatusConst.InService: return 'En servicio';
    case AppointmentStatusConst.Completed: return 'Completada';
    case AppointmentStatusConst.NoShow: return 'No asistió';
    case AppointmentStatusConst.Cancelled: return 'Cancelada';
    default: return String(status);
  }
};

export const statusBadge = (status: AppointmentStatus) => {
  switch (status) {
    case AppointmentStatusConst.Completed: return 'bg-success';
    case AppointmentStatusConst.Confirmed: return 'ta-chip-confirmed';
    case AppointmentStatusConst.InService: return 'bg-info';
    case AppointmentStatusConst.NoShow: return 'bg-warning text-dark';
    case AppointmentStatusConst.Cancelled: return 'bg-danger';
    default: return 'bg-secondary';
  }
};

export const isClosed = (status: AppointmentStatus) =>
  status === AppointmentStatusConst.Completed
  || status === AppointmentStatusConst.NoShow
  || status === AppointmentStatusConst.Cancelled;

/**
 * Whether «Cobrar» is offered: an appointment neither cancelled nor missed, without its sale yet (the server also takes
 * one whose sale was cancelled; then the agenda says it is charged and Pedidos has it).
 */
export const canBeCharged = (appointment: { status: AppointmentStatus; saleOrderId?: string | null }) =>
  !appointment.saleOrderId
  && appointment.status !== AppointmentStatusConst.Cancelled
  && appointment.status !== AppointmentStatusConst.NoShow;
