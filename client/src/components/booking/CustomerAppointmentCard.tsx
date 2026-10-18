import { Link } from 'react-router-dom';
import type { CustomerAppointment } from '../../api/publicMenuApi';
import ReviewButton from '../reviews/ReviewButton';
import { AppointmentStatus } from '../../types';
import { addressMapUrl, bookingPath, customerAppointmentChip, upperFirst } from '../../utils/onlineBooking';
import { businessWhatsAppUrl } from '../../utils/whatsApp';

interface CustomerAppointmentCardProps {
  appointment: CustomerAppointment;
  /** Cancels it; offered only while the server says the customer still may (before it starts). */
  onCancel?: () => void;
  cancelling?: boolean;
  /** The level of its title: h2 alone on its page, h3 inside a list with its heading. */
  titleAs?: 'h2' | 'h3';
  /** Rating its business once the appointment was attended (Fase 24H): only for a customer account. */
  review?: { reviewed: boolean; onReview: () => void };
}

/**
 * An appointment booked online, as its customer sees it (Fase 24D): the service, the business, when, where, its status in
 * the business's words, and what the customer can do with it: cancel it while it has not started, write to the business,
 * book another one, or rate the business once it was attended (Fase 24H).
 */
export default function CustomerAppointmentCard({
  appointment, onCancel, cancelling = false, titleAs: Title = 'h3', review,
}: CustomerAppointmentCardProps) {
  const contact = businessWhatsAppUrl(
    appointment.businessContactPhone,
    `Hola, les escribo por mi cita de ${appointment.serviceName} del ${appointment.dateLabel} a las ${appointment.timeLabel}.`,
  );

  return (
    <div className="card h-100 mb-0">
      <div className="card-body">
        <div className="d-flex justify-content-between align-items-start gap-2 mb-2">
          <div className="min-w-0">
            <Title className="h6 mb-0">{appointment.serviceName}</Title>
            <small className="text-muted">{appointment.businessName}</small>
          </div>
          <span className={`ta-chip ${customerAppointmentChip(appointment.status)}`}>{appointment.statusLabel}</span>
        </div>
        <p className="mb-1">
          <i className="ri-calendar-event-line me-1" aria-hidden="true"></i>
          {upperFirst(appointment.dateLabel)} · {appointment.timeLabel}
        </p>
        {appointment.address && (
          <p className="small mb-1">
            <i className="ri-map-pin-line me-1" aria-hidden="true"></i>
            {appointment.address}{' '}
            <a className="text-nowrap" href={addressMapUrl(appointment.address)} target="_blank" rel="noopener noreferrer">Cómo llegar</a>
          </p>
        )}
        {appointment.status === AppointmentStatus.Requested && (
          <p className="small text-muted mb-1">{appointment.businessName} te confirma por WhatsApp.</p>
        )}
        <div className="d-flex flex-wrap gap-2 mt-3">
          {review && appointment.status === AppointmentStatus.Completed && (
            <ReviewButton businessName={appointment.businessName} reviewed={review.reviewed} onClick={review.onReview} />
          )}
          {onCancel && appointment.canCancel && (
            <button
              type="button"
              className="btn btn-sm btn-light"
              disabled={cancelling}
              onClick={onCancel}
              aria-label={`Cancelar la cita de ${appointment.serviceName} del ${appointment.dateLabel}`}
            >
              {cancelling ? 'Cancelando…' : 'Cancelar cita'}
            </button>
          )}
          {contact && (
            <a
              className="btn btn-sm btn-light"
              href={contact}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Escribir a ${appointment.businessName} por WhatsApp`}
            >
              <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Escribir al negocio
            </a>
          )}
          <Link
            className="btn btn-sm btn-light"
            to={bookingPath(appointment.businessSlug)}
            aria-label={`Agendar otra cita en ${appointment.businessName}`}
          >
            Agendar otra
          </Link>
        </div>
      </div>
    </div>
  );
}
