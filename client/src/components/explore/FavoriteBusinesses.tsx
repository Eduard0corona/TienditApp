import { useEffect, useId, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { FavoriteBusinesses as Favorites } from '../../hooks/useFavoriteBusinesses';
import { useScrollFade } from '../../hooks/useScrollFade';
import PhotoOrInitial from '../common/PhotoOrInitial';
import DirectoryBusinessCard from './DirectoryBusinessCard';
import FavoriteButton from './FavoriteButton';

/** The favorites a wide screen shows before «Ver todos»: two rows of cards. */
const SHOWN_ON_A_WIDE_SCREEN = 6;

/**
 * «Tus favoritos» at the top of Explorar (Fase 24F): the businesses the customer keeps, the last one first. Sideways on a
 * phone (the edge that hides more fades), and on a wide screen the first rows with «Ver todos». One that does not show in
 * Explorar now keeps its name and opens its menu.
 */
export default function FavoriteBusinesses({ favorites }: { favorites: Favorites }) {
  const titleId = useId();
  const titleRef = useRef<HTMLHeadingElement>(null);
  const [showAll, setShowAll] = useState(false);
  const { ref, fadeClass } = useScrollFade<HTMLUListElement>(favorites.favorites.length);
  // A business taken out from here leaves the row: the focus goes to its title, not to the top of the page.
  const removing = useRef<string | null>(null);
  const slugs = favorites.favorites.map((favorite) => favorite.slug);
  useEffect(() => {
    if (removing.current && !slugs.includes(removing.current)) {
      removing.current = null;
      titleRef.current?.focus();
    }
  });

  if (slugs.length === 0) return null;
  const hearts: Favorites = {
    ...favorites,
    toggle: (slug) => {
      if (favorites.isSaved(slug) && !favorites.isBusy(slug)) removing.current = slug;
      favorites.toggle(slug);
    },
  };
  const hidden = slugs.length - SHOWN_ON_A_WIDE_SCREEN;
  return (
    <section className="ex-favorites" aria-labelledby={titleId}>
      <h2 id={titleId} ref={titleRef} className="ex-section-title" tabIndex={-1}>Tus favoritos</h2>
      <ul ref={ref} className={`ex-favorites-row ta-scroll-fade ${fadeClass} ${showAll ? 'is-all' : ''}`}>
        {favorites.favorites.map((favorite, index) => {
          const heart = <FavoriteButton slug={favorite.slug} name={favorite.name} favorites={hearts} className="dc-favorite" />;
          return (
            <li key={favorite.slug} className={index >= SHOWN_ON_A_WIDE_SCREEN ? 'ex-favorite-more' : undefined}>
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
      {hidden > 0 && (
        <div className="ex-favorites-all">
          <button type="button" className="btn btn-light" aria-expanded={showAll} onClick={() => setShowAll(!showAll)}>
            {showAll ? 'Ver menos' : `Ver todos tus favoritos (${slugs.length})`}
          </button>
        </div>
      )}
    </section>
  );
}
