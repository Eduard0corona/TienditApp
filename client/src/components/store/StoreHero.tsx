import { useEffect, useState, type ReactNode } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { BusinessCategory } from '../../api/generated/enums';
import type { PublicMenu } from '../../api/publicMenuApi';
import type { RatingSummary } from '../../api/reviewsApi';
import { categoryInfo } from '../../utils/businessCategories';
import { formatAverage, reviewCountLabel, storeRatingName } from '../../utils/reviews';
import PhotoOrInitial from '../common/PhotoOrInitial';

export interface StoreDeliveryChoice {
  /** The menu takes orders to an address (not from a table). */
  offered: boolean;
  /** Only to an address: a kitchen without premises. */
  required: boolean;
  toAddress: boolean;
  onChange: (toAddress: boolean) => void;
  /** What delivery costs to the customer's postal code, or why it does not reach it, once known. */
  hint: ReactNode | null;
}

interface StoreHeroProps {
  menu: PublicMenu;
  status: { isOpen: boolean; label: string; detail: string | null } | null;
  /** The table of the QR the menu was opened from; '' elsewhere. */
  tableLabel: string;
  /** «Hoy: 9:00 a 15:00», the hours in one line; null without hours. */
  hoursSummary: string | null;
  upcomingNotice: string | null;
  /** Opens the hours, address and contact; null when the business has none of them to show. */
  onOpenInfo: (() => void) | null;
  delivery: StoreDeliveryChoice;
  /** The booking page, when the business books appointments online (not from a table). */
  bookingPath: string | null;
  contactUrl: string | null;
  /** The heart of the business over its cover (Fase 24F); none where it does not apply. */
  favorite?: ReactNode;
  /** Its rating (Fase 24H), which takes to its reviews; null without reviews. */
  rating?: { summary: RatingSummary; onOpen: () => void } | null;
}

/**
 * The top of the online menu as the delivery apps open a store (Fase 24E): the business's cover across the page (its
 * colors without one), its logo over it, its name, what it is and its line, whether it is open, a button for its hours
 * and address, its rating (Fase 24H), and how the customer wants the order. The arrow goes back only to where the customer came from in the
 * app: a menu opened from a QR or a link never leads to other businesses.
 */
export default function StoreHero({
  menu, status, tableLabel, hoursSummary, upcomingNotice, onOpenInfo, delivery, bookingPath, contactUrl, favorite, rating,
}: StoreHeroProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const [shareNotice, setShareNotice] = useState('');
  const canGoBack = location.key !== 'default';
  // «Otros» says nothing about the business: its line goes alone.
  const kind = menu.category === BusinessCategory.Other ? undefined : categoryInfo(menu.category)?.label;
  const line = [kind, menu.description].filter(Boolean).join(' · ');
  const cover = menu.coverImageUrl;
  const shared = shareNotice === 'Enlace copiado';

  useEffect(() => {
    if (!shareNotice) return;
    const timer = window.setTimeout(() => setShareNotice(''), 2500);
    return () => window.clearTimeout(timer);
  }, [shareNotice]);

  const share = async () => {
    const url = `${window.location.origin}/m/${menu.slug}`;
    if (navigator.share) {
      try {
        await navigator.share({ title: menu.businessName, url });
      } catch {
        // Cancelled: nothing to tell.
      }
      return;
    }
    try {
      await navigator.clipboard.writeText(url);
      setShareNotice('Enlace copiado');
    } catch {
      setShareNotice('No se pudo copiar el enlace');
    }
  };

  return (
    <header className="pm-hero">
      <div className={`pm-cover ${cover ? 'has-photo' : ''}`}>
        {cover && (
          <img
            className="pm-cover-photo"
            src={cover}
            srcSet={menu.coverImageWideUrl ? `${cover} 1024w, ${menu.coverImageWideUrl} 2048w` : undefined}
            sizes="(min-width: 72rem) 72rem, 100vw"
            alt=""
          />
        )}
        <div className="pm-cover-bar">
          {canGoBack ? (
            <button type="button" className="pm-round" onClick={() => navigate(-1)} aria-label="Regresar">
              <i className="ri-arrow-left-line" aria-hidden="true"></i>
            </button>
          ) : <span />}
          <div className="pm-cover-actions">
            {favorite}
            <button type="button" className="pm-round" onClick={() => void share()} aria-label="Compartir menú">
              <i className={shared ? 'ri-check-line' : 'ri-share-forward-line'} aria-hidden="true"></i>
            </button>
            <span className="visually-hidden" aria-live="polite">{shareNotice}</span>
            {contactUrl && (
              <a className="pm-round" href={contactUrl} target="_blank" rel="noopener noreferrer" aria-label="Escribir por WhatsApp">
                <i className="ri-whatsapp-line" aria-hidden="true"></i>
              </a>
            )}
          </div>
        </div>
      </div>

      <div className="pm-store">
        <PhotoOrInitial src={menu.logoUrl} name={menu.businessName} className="pm-logo" loading="eager" />
        <h1 className="pm-store-name">{menu.businessName}</h1>
        {line && <p className="pm-store-line">{line}</p>}
        {(status || tableLabel) && (
          <p className="pm-status">
            {status && <span className={`ta-chip ${status.isOpen ? 'ta-chip-ready' : 'ta-chip-done'}`}>{status.label}</span>}
            {status?.detail && <span className="text-muted">{status.detail}</span>}
            {tableLabel && <span className="pm-table"><i className="ri-restaurant-line" aria-hidden="true"></i>Pedido para {tableLabel}</span>}
          </p>
        )}
        {!status && !tableLabel && <p className="pm-store-line">{menu.acceptsOrders ? 'Pide en línea' : 'Menú'}</p>}
        {upcomingNotice && (
          <p className="pm-upcoming">
            <i className="ri-calendar-event-line" aria-hidden="true"></i>
            <span>{upcomingNotice}</span>
          </p>
        )}

        {(onOpenInfo || menu.deliveryOnly || rating) && (
          <div className="pm-pills">
            {rating && (
              <button
                type="button"
                className="pm-pill"
                onClick={rating.onOpen}
                aria-label={`${storeRatingName(rating.summary)}. Ver reseñas`}
              >
                <i className={rating.summary.average != null ? 'ri-star-fill pm-pill-star' : 'ri-star-line'} aria-hidden="true"></i>
                {rating.summary.average != null ? (
                  <>
                    <span className="ta-money">{formatAverage(rating.summary.average)}</span>
                    <span className="pm-pill-muted ta-money">({rating.summary.count.toLocaleString('es-MX')})</span>
                  </>
                ) : <span>{reviewCountLabel(rating.summary.count)}</span>}
              </button>
            )}
            {onOpenInfo && (
              <button type="button" className="pm-pill" onClick={onOpenInfo}>
                <i className={hoursSummary ? 'ri-time-line' : 'ri-information-line'} aria-hidden="true"></i>
                <span>{hoursSummary ?? 'Información'}</span>
                <i className="ri-arrow-right-s-line" aria-hidden="true"></i>
              </button>
            )}
            {menu.deliveryOnly && (
              <span className="pm-pill static"><i className="ri-e-bike-2-line" aria-hidden="true"></i>Solo a domicilio</span>
            )}
          </div>
        )}

        {/* How the customer wants it matters only while the menu takes orders. */}
        {menu.acceptsOrders && delivery.offered && !delivery.required && (
          <div className="pm-mode" role="radiogroup" aria-label="¿Cómo lo quieres?">
            {[true, false].map((option) => (
              <button
                key={String(option)}
                type="button"
                role="radio"
                aria-checked={delivery.toAddress === option}
                className={delivery.toAddress === option ? 'active' : ''}
                onClick={() => delivery.onChange(option)}
              >
                <i className={option ? 'ri-e-bike-2-line' : 'ri-shopping-bag-3-line'} aria-hidden="true"></i>
                {option ? 'A domicilio' : 'Para recoger'}
              </button>
            ))}
          </div>
        )}
        {menu.acceptsOrders && delivery.offered && delivery.hint && <p className="pm-delivery-hint" role="status">{delivery.hint}</p>}

        {bookingPath && (
          <Link className={`btn btn-touch w-100 pm-booking ${menu.acceptsOrders ? 'btn-light' : 'btn-primary'}`} to={bookingPath}>
            <i className="ri-calendar-event-line me-1" aria-hidden="true"></i>Agenda tu cita
          </Link>
        )}
      </div>
    </header>
  );
}
