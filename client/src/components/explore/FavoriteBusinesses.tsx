import { useId } from 'react';
import { Link } from 'react-router-dom';
import type { FavoriteBusinesses as Favorites } from '../../hooks/useFavoriteBusinesses';
import PhotoOrInitial from '../common/PhotoOrInitial';
import DirectoryBusinessCard from './DirectoryBusinessCard';
import FavoriteButton from './FavoriteButton';

/**
 * «Tus favoritos» at the top of Explorar (Fase 24F): the businesses the customer keeps, the last one first, sideways on a
 * phone. One that does not show in Explorar now keeps its name and opens its menu.
 */
export default function FavoriteBusinesses({ favorites }: { favorites: Favorites }) {
  const titleId = useId();
  if (favorites.favorites.length === 0) return null;
  return (
    <section className="ex-favorites" aria-labelledby={titleId}>
      <h2 id={titleId} className="ex-section-title">Tus favoritos</h2>
      <ul className="ex-favorites-row">
        {favorites.favorites.map((favorite) => {
          const heart = <FavoriteButton slug={favorite.slug} name={favorite.name} favorites={favorites} className="dc-favorite" />;
          return (
            <li key={favorite.slug}>
              {favorite.card ? (
                <DirectoryBusinessCard business={favorite.card} action={heart} />
              ) : (
                <div className="dc-wrap">
                  <Link className="ex-favorite-tile" to={`/m/${favorite.slug}`}>
                    <PhotoOrInitial src={favorite.logoUrl} name={favorite.name} className="ex-favorite-logo" />
                    <span className="ex-favorite-name">{favorite.name}</span>
                    <span className="ex-favorite-note">No aparece en Explorar ahora</span>
                  </Link>
                  {heart}
                </div>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
