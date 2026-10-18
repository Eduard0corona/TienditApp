import { Link, useLocation } from 'react-router-dom';
import type { FavoriteBusinesses } from '../../hooks/useFavoriteBusinesses';

interface FavoriteButtonProps {
  slug: string;
  name: string;
  favorites: FavoriteBusinesses;
  /** Its look where it is: over a card or over the cover of a store. */
  className: string;
}

/**
 * The heart of a business (Fase 24F): a customer keeps it among their favorites or takes it out; a visitor goes to sign
 * in and comes back here. The team of a business sees none.
 */
export default function FavoriteButton({ slug, name, favorites, className }: FavoriteButtonProps) {
  const location = useLocation();
  if (favorites.mode === 'none') return null;
  if (favorites.mode === 'visitor') {
    return (
      <Link className={className} to="/login" state={{ from: location }} aria-label={`Entra para guardar ${name} en tus favoritos`}>
        <i className="ri-heart-line" aria-hidden="true"></i>
      </Link>
    );
  }
  const saved = favorites.isSaved(slug);
  return (
    <button
      type="button"
      className={`${className} ${saved ? 'is-favorite' : ''}`}
      aria-pressed={saved}
      aria-label={saved ? `Quitar ${name} de tus favoritos` : `Guardar ${name} en tus favoritos`}
      disabled={favorites.isChanging(slug)}
      onClick={() => favorites.toggle(slug)}
    >
      <i className={saved ? 'ri-heart-fill' : 'ri-heart-line'} aria-hidden="true"></i>
    </button>
  );
}
