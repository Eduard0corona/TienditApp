import type { CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import type { DirectoryBusiness } from '../../api/publicDirectoryApi';
import { categoryInfo, deliveryToPostalCode, orderStatus } from '../../utils/businessCategories';
import { getBusinessTheme } from '../../utils/businessThemes';
import PhotoOrInitial from '../common/PhotoOrInitial';
import './DirectoryBusinessCard.css';

interface DirectoryBusinessCardProps {
  business: DirectoryBusiness;
  /** A preview (the owner's settings): the same card, without the link. */
  preview?: boolean;
}

/**
 * A business in Explorar: its logo (or its initial in its colors), name, what it sells, its category and city, and
 * whether it takes orders now. With the customer's postal code (Fase 24B) it says whether it delivers there and what it
 * costs, and a business that books appointments online says so (Fase 24D). The whole card opens its menu.
 */
export default function DirectoryBusinessCard({ business, preview = false }: DirectoryBusinessCardProps) {
  const theme = getBusinessTheme(business.theme);
  const tint = {
    '--dc-tint': theme.light.accentSubtle,
    '--dc-ink': theme.light.accentText,
    '--dc-tint-dark': theme.dark.accentSubtle,
    '--dc-ink-dark': theme.dark.accentText,
  } as CSSProperties;
  const status = orderStatus(business);
  const delivery = deliveryToPostalCode(business);
  const place = [categoryInfo(business.category)?.label, business.deliveryOnly ? 'Solo a domicilio' : business.city]
    .filter(Boolean)
    .join(' · ');

  const content = (
    <>
      <PhotoOrInitial src={business.logoUrl} name={business.name} className="dc-logo" />
      <div className="dc-body">
        <h3 className="dc-name">{business.name}</h3>
        {business.description && <p className="dc-description">{business.description}</p>}
        <p className="dc-place">{place}</p>
        <p className="dc-status">
          <span className={`dc-badge dc-badge-${status.tone}`}>
            {status.tone === 'taking' && <i className="ri-shopping-bag-3-line" aria-hidden="true"></i>}
            {status.text}
          </span>
          {business.booksOnline && (
            <span className="dc-badge dc-badge-info">
              <i className="ri-calendar-event-line" aria-hidden="true"></i>Agenda en línea
            </span>
          )}
          {delivery && (
            <span className={`dc-badge ${delivery.reaches ? 'dc-badge-info' : 'dc-badge-menu'}`}>
              <i className={delivery.reaches ? 'ri-e-bike-2-line' : 'ri-map-pin-line'} aria-hidden="true"></i>{delivery.text}
            </span>
          )}
        </p>
      </div>
    </>
  );

  return preview ? (
    <div className="dc-card" style={tint}>{content}</div>
  ) : (
    <Link className="dc-card" style={tint} to={`/m/${business.slug}`}>{content}</Link>
  );
}
