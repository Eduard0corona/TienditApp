import { useId, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import type { DirectoryProduct } from '../../api/publicDirectoryApi';
import { getBusinessTheme } from '../../utils/businessThemes';
import { formatDistance } from '../../utils/geo';
import { formatMoney } from '../../utils/money';
import PhotoOrInitial from '../common/PhotoOrInitial';
import './DirectoryProducts.css';

/**
 * The products a search of Explorar found (Fase 24F): each with its photo (its initial in the colors of its business
 * without one), its price as its menu writes it and the business it is at, with how far it is near the customer (Fase
 * 24G). A product opens on its own sheet in its store.
 */
export default function DirectoryProducts({ products }: { products: DirectoryProduct[] }) {
  const titleId = useId();
  return (
    <section className="dp-section" aria-labelledby={titleId}>
      <h2 id={titleId} className="ex-section-title">Productos</h2>
      <ul className="dp-list">
        {products.map((product) => {
          const theme = getBusinessTheme(product.theme);
          const tint = {
            '--dp-tint': theme.light.accentSubtle,
            '--dp-ink': theme.light.accentText,
            '--dp-tint-dark': theme.dark.accentSubtle,
            '--dp-ink-dark': theme.dark.accentText,
          } as CSSProperties;
          return (
            <li key={product.id}>
              <Link className="dp-row" style={tint} to={`/m/${product.businessSlug}?producto=${product.id}`}>
                <PhotoOrInitial src={product.imageUrl} name={product.name} className="dp-photo" />
                <span className="dp-body">
                  <span className="dp-name">{product.name}</span>
                  <span className="dp-business">
                    {product.businessName}
                    {product.distanceKm != null && ` · ${formatDistance(product.distanceKm)}`}
                    {product.acceptsOrders && <span className="dp-taking"> · Pide en línea</span>}
                  </span>
                  <span className="dp-price-row">
                    <span className="ta-money dp-price">{formatMoney(product.price)}</span>
                    {product.compareAtPrice != null && (
                      <s className="ta-money dp-compare"><span className="visually-hidden">antes </span>{formatMoney(product.compareAtPrice)}</s>
                    )}
                    {product.addsTax && <span className="dp-tax">más IVA</span>}
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
