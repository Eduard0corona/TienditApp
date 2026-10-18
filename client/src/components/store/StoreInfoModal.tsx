import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import type { PublicMenu } from '../../api/publicMenuApi';
import { WEEK, dayLabel, describeDay, describeSpecialDay } from '../../utils/openingHours';
import { mapsUrl } from '../../utils/storeMenu';
import PhotoOrInitial from '../common/PhotoOrInitial';

interface StoreInfoModalProps {
  show: boolean;
  onHide: () => void;
  menu: PublicMenu;
  /** The chip of the menu's header: open or closed now, and why or until when. */
  status: { isOpen: boolean; label: string; detail: string | null } | null;
  /** The menu takes orders to an address (not from a table). */
  deliveryOffered: boolean;
  contactUrl: string | null;
}

/**
 * Everything about the business that is not its menu (Fase 24E), in one window as the delivery apps show a store's
 * information: whether it is open now, its hours of the week and its special days ahead, where it is with a map, whether
 * it delivers, and its WhatsApp.
 */
export default function StoreInfoModal({ show, onHide, menu, status, deliveryOffered, contactUrl }: StoreInfoModalProps) {
  const titleId = useId();
  const hours = menu.openingHours;
  const specialDays = hours?.specialDays ?? [];
  return (
    <Modal show={show} onHide={onHide} centered scrollable fullscreen="sm-down" aria-labelledby={titleId} contentClassName="pm-info-sheet">
      <Modal.Header closeButton>
        <div className="d-flex align-items-center gap-2 min-w-0">
          <PhotoOrInitial src={menu.logoUrl} name={menu.businessName} className="pm-info-logo" />
          <Modal.Title id={titleId} as="h2" className="h5 mb-0 text-truncate">{menu.businessName}</Modal.Title>
        </div>
      </Modal.Header>
      <Modal.Body>
        {menu.description && <p className="text-muted">{menu.description}</p>}
        {hours && (
          <section className="pm-info-section" aria-label="Horario">
            <h3 className="pm-info-title"><i className="ri-time-line" aria-hidden="true"></i>Horario</h3>
            {status && (
              <p className="pm-status mb-2">
                <span className={`ta-chip ${status.isOpen ? 'ta-chip-ready' : 'ta-chip-done'}`}>{status.label}</span>
                {status.detail && <span className="text-muted small">{status.detail}</span>}
              </p>
            )}
            {hours.intervals.length > 0 && (
              <ul className="pm-week list-unstyled" aria-label="Horario de la semana">
                {WEEK.map((day) => (
                  <li key={day} aria-current={day === hours.today ? 'date' : undefined}>
                    <span>{dayLabel(day)}</span>
                    <span>{describeDay(hours.intervals, day)}</span>
                  </li>
                ))}
              </ul>
            )}
            {specialDays.length > 0 && (
              <ul className="pm-special-days list-unstyled" aria-label="Días especiales">
                {specialDays.map((day) => <li key={day.from}>{describeSpecialDay(day)}</li>)}
              </ul>
            )}
          </section>
        )}
        {(menu.address || menu.deliveryOnly || deliveryOffered) && (
          <section className="pm-info-section" aria-label="Ubicación">
            <h3 className="pm-info-title"><i className="ri-map-pin-line" aria-hidden="true"></i>Ubicación</h3>
            {menu.deliveryOnly && <p className="mb-1">Solo a domicilio: no tenemos local para visitar.</p>}
            {deliveryOffered && !menu.deliveryOnly && (
              <p className="mb-1">También a domicilio: al pedir te decimos si llegamos a tu código postal.</p>
            )}
            {menu.address && !menu.deliveryOnly && (
              <p className="pm-info-address mb-0">
                <span>{menu.address}</span>
                <a className="text-nowrap" href={mapsUrl(menu.address)} target="_blank" rel="noopener noreferrer">Cómo llegar</a>
              </p>
            )}
          </section>
        )}
        {contactUrl && (
          <a className="btn btn-soft-success btn-touch w-100" href={contactUrl} target="_blank" rel="noopener noreferrer">
            <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Escribir por WhatsApp
          </a>
        )}
      </Modal.Body>
    </Modal>
  );
}
