import { appointmentSaleProblem } from '../../utils/homeVisits';
import type { AppointmentStatus } from '../../types';
import { AppointmentStatus as AppointmentStatusConst } from '../../types';

export const statusLabel = (status: AppointmentStatus) => {
  switch (status) {
    case AppointmentStatusConst.Requested: return 'Por confirmar';
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
    case AppointmentStatusConst.Requested:
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
 * Whether «Cobrar» is offered: an appointment neither cancelled nor missed, without its sale yet or whose sale was
 * cancelled (the server charges it again then).
 */
export const canBeCharged = (appointment: { status: AppointmentStatus; saleOrderId?: string | null; saleCancelled?: boolean }) =>
  appointmentSaleProblem(appointment) === null;

/** «Con venta» once it has its sale (paid or not: Pedidos has it), «Venta cancelada» when that sale was cancelled. */
export const saleLabel = (appointment: { saleOrderId?: string | null; saleCancelled?: boolean }) =>
  !appointment.saleOrderId ? null : appointment.saleCancelled ? 'Venta cancelada' : 'Con venta';
