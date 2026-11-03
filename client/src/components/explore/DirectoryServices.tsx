import { useId, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { PublicDirectoryServiceKind } from '../../api/generated/enums';
import type { DirectoryService } from '../../api/publicDirectoryApi';
import { getBusinessTheme } from '../../utils/businessThemes';
import { formatDistance } from '../../utils/geo';
import { formatMoney } from '../../utils/money';
import { bookingPath, describeDuration } from '../../utils/onlineBooking';
import PhotoOrInitial from '../common/PhotoOrInitial';
import './DirectoryProducts.css';

/** Where a service opens: chosen on its booking page, or the store of a business that goes to homes, where the visit is asked. */
function servicePath(service: DirectoryService): string {
  return service.kind === PublicDirectoryServiceKind.BooksOnline
    ? `${bookingPath(service.businessSlug)}?servicio=${service.id}`
    : `/m/${service.businessSlug}`;
}

/**
 * The services a search of Explorar found (Fase 29I, ADR 0063), after its products: each as the page of its business says
 * it, booked online at its price and length, or at the customer's home from what it starts at, with the business it is at.
 */
export default function DirectoryServices({ services }: { services: DirectoryService[] }) {
  const titleId = useId();
  return (
    <section className="dp-section" aria-labelledby={titleId}>
      <h2 id={titleId} className="ex-section-title">Servicios</h2>
      <ul className="dp-list">
        {services.map((service) => {
          const theme = getBusinessTheme(service.theme);
          const tint = {
            '--dp-tint': theme.light.accentSubtle,
            '--dp-ink': theme.light.accentText,
            '--dp-tint-dark': theme.dark.accentSubtle,
            '--dp-ink-dark': theme.dark.accentText,
          } as CSSProperties;
          const atHome = service.kind === PublicDirectoryServiceKind.HomeVisit;
          return (
            <li key={service.id}>
              <Link className="dp-row" style={tint} to={servicePath(service)}>
                <PhotoOrInitial src={service.imageUrl} name={service.name} className="dp-photo" />
                <span className="dp-body">
                  <span className="dp-name">{service.name}</span>
                  <span className="dp-business">
                    {service.businessName}
                    {service.distanceKm != null && ` · ${formatDistance(service.distanceKm)}`}
                    <span className="dp-taking"> · {atHome ? 'Visita a domicilio' : 'Agenda en línea'}</span>
                  </span>
                  <span className="dp-price-row">
                    <span className="ta-money dp-price">{atHome ? 'desde ' : ''}{formatMoney(service.price)}</span>
                    {service.addsTax && <span className="dp-tax">más IVA</span>}
                    {service.durationMinutes != null && <span className="dp-tax">{describeDuration(service.durationMinutes)}</span>}
                  </span>
                </span>
              </Link>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
