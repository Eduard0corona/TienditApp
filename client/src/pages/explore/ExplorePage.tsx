import { useEffect, useId, useRef, useState } from 'react';
import { Link, Navigate, useParams, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { publicDirectoryApi, type DirectoryBusiness, type DirectorySearch } from '../../api/publicDirectoryApi';
import DirectoryBusinessCard from '../../components/explore/DirectoryBusinessCard';
import DirectoryProducts from '../../components/explore/DirectoryProducts';
import FavoriteBusinesses from '../../components/explore/FavoriteBusinesses';
import FavoriteButton from '../../components/explore/FavoriteButton';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { useFavoriteBusinesses, type FavoriteBusinesses as Favorites } from '../../hooks/useFavoriteBusinesses';
import { useNearMe, type NearMe } from '../../hooks/useNearMe';
import { useScrollFade } from '../../hooks/useScrollFade';
import { BUSINESS_CATEGORIES, categoryBySlug } from '../../utils/businessCategories';
import { readPostalCode, savePostalCode } from '../../utils/postalCodePreference';
import { isPostalCode } from '../../utils/postalCodes';
import { latestCart } from '../../utils/savedCart';
import './ExplorePage.css';

const PAGE_SIZE = 24;
// The same limit as the API (GetPublicDirectoryQuery.MaxSearchLength).
const MAX_SEARCH = 60;
const SEARCH_DELAY_MS = 300;

/** The same search but for where the customer is: what changes when «Cerca de mí» answers. */
function samePlaceSearch(a: DirectorySearch, b: DirectorySearch): boolean {
  return JSON.stringify({ ...a, latitude: undefined, longitude: undefined }) === JSON.stringify({ ...b, latitude: undefined, longitude: undefined });
}

function countLabel(count: number): string {
  return count === 1 ? '1 negocio' : `${count.toLocaleString('es-MX')} negocios`;
}

function BusinessList({ businesses, favorites }: { businesses: DirectoryBusiness[]; favorites: Favorites }) {
  return (
    <ul className="ex-grid">
      {businesses.map((business) => (
        <li key={business.slug}>
          <DirectoryBusinessCard
            business={business}
            action={<FavoriteButton slug={business.slug} name={business.name} favorites={favorites} className="dc-favorite" />}
          />
        </li>
      ))}
    </ul>
  );
}

/** A page after the first, asked when the customer wants more. */
function MoreBusinesses({ search, pageNumber, favorites }: { search: DirectorySearch; pageNumber: number; favorites: Favorites }) {
  const page = useQuery({
    queryKey: ['directory', search, pageNumber],
    queryFn: ({ signal }) => publicDirectoryApi.getDirectory({ ...search, pageNumber }, signal),
  });
  if (page.isLoading) return <p className="text-center text-muted mt-3">Cargando…</p>;
  if (page.isError) {
    return (
      <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mt-3" role="alert">
        <span className="me-auto">{getApiErrorMessage(page.error, 'No se pudieron cargar más negocios.')}</span>
        <button type="button" className="btn btn-sm btn-light" onClick={() => void page.refetch()}>Reintentar</button>
      </div>
    );
  }
  const businesses = page.data?.businesses.items ?? [];
  return businesses.length > 0 ? <div className="mt-3"><BusinessList businesses={businesses} favorites={favorites} /></div> : null;
}

/**
 * «Entregar en…» (Fase 24B): the postal code the cards answer for, asked once and remembered on this device. Never sent
 * anywhere but to Explorar's own search.
 */
function PostalCodeChip({ postalCode, onChange }: { postalCode: string; onChange: (postalCode: string) => void }) {
  const inputId = useId();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(postalCode);
  const typed = draft.trim();
  const inputRef = useRef<HTMLInputElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  // The focus follows the form: to its field when it opens, back to the button when it closes (never on loading).
  const toggled = useRef(false);
  useEffect(() => {
    if (toggled.current) (editing ? inputRef.current : toggleRef.current)?.focus();
  }, [editing]);
  const toggle = (open: boolean) => {
    toggled.current = true;
    setEditing(open);
  };

  if (!editing) {
    return (
      <p className="ex-postal">
        <i className="ri-e-bike-2-line" aria-hidden="true"></i>
        {postalCode ? <span>Entregar en <strong>{postalCode}</strong></span> : <span>¿Te lo llevan a domicilio?</span>}
        <button ref={toggleRef} type="button" className="btn btn-link btn-sm p-0" onClick={() => { setDraft(postalCode); toggle(true); }}>
          {postalCode ? 'Cambiar' : 'Escribe tu código postal'}
        </button>
      </p>
    );
  }
  return (
    <form
      className="ex-postal"
      onSubmit={(event) => {
        event.preventDefault();
        if (typed && !isPostalCode(typed)) return;
        onChange(typed);
        toggle(false);
      }}
    >
      <label className="small" htmlFor={inputId}>Código postal</label>
      <input
        ref={inputRef}
        id={inputId}
        className={`form-control form-control-sm ex-postal-input ${typed && !isPostalCode(typed) ? 'is-invalid' : ''}`}
        inputMode="numeric"
        autoComplete="postal-code"
        maxLength={5}
        pattern="\d{5}"
        value={draft}
        onChange={(event) => setDraft(event.target.value.replace(/\D/g, ''))}
        aria-describedby={`${inputId}-help`}
      />
      <button type="submit" className="btn btn-sm btn-primary" disabled={!!typed && !isPostalCode(typed)}>Listo</button>
      {postalCode && (
        <button type="button" className="btn btn-sm btn-light" onClick={() => { onChange(''); toggle(false); }}>Quitar</button>
      )}
      <span id={`${inputId}-help`} className="small text-muted w-100">Cada negocio te dice si llega y cuánto cobra.</span>
    </form>
  );
}

/**
 * «Cerca de mí» (Fase 24G): asks the browser where the customer is and orders Explorar by distance while it is on. Says
 * what it is doing, and what to do when the browser does not tell. The status stays in the page, empty when there is
 * nothing to say, so what it says next is read aloud.
 */
function NearMeChip({ near, onToggle, onRetry }: { near: NearMe; onToggle: () => void; onRetry: () => void }) {
  const chip = useRef<HTMLButtonElement>(null);
  const on = near.status !== 'off';
  const canAsk = near.status === 'ask' || near.status === 'denied' || near.status === 'unavailable';
  const messages: Partial<Record<NearMe['status'], string>> = {
    ask: 'Usa tu ubicación para ver primero lo más cercano.',
    locating: 'Buscando dónde estás…',
    on: 'Primero lo más cercano a ti.',
    denied: 'Permite tu ubicación en el navegador para ver primero lo más cercano.',
    unavailable: 'No pudimos saber dónde estás.',
    unsupported: 'Este navegador no puede decir dónde estás.',
  };
  return (
    <div className="ex-near">
      <button ref={chip} type="button" className={`ex-chip ${on ? 'active' : ''}`} aria-pressed={on} onClick={onToggle}>
        <i className="ri-map-pin-user-line" aria-hidden="true"></i>Cerca de mí
      </button>
      <span className="ex-near-status" role="status">{messages[near.status] ?? ''}</span>
      {canAsk && (
        // The button leaves while the browser answers: the focus waits on the chip.
        <button type="button" className="btn btn-link btn-sm p-0" onClick={() => { chip.current?.focus(); onRetry(); }}>
          {near.status === 'ask' ? 'Usar mi ubicación' : 'Intentar de nuevo'}
        </button>
      )}
    </div>
  );
}

/**
 * Explorar (/explorar and /explorar/:categoria): the businesses that published their menu, those that take orders now
 * first. A customer finds one by category or by words of its name or what it sells, and the card opens its menu, where
 * it orders as it always did. The search lives in the address (?q=), so a search can be shared. With the customer's
 * postal code each card says whether it delivers there, and a cart left without sending is a tap away (Fase 24B). A
 * search also finds the products of the menus, and a customer keeps the businesses they like, first on the page
 * (Fase 24F). «Cerca de mí» (?cerca=1) shows the nearest first, each card saying how far it is (Fase 24G).
 */
export default function ExplorePage() {
  const searchId = useId();
  const [postalCode, setPostalCode] = useState(readPostalCode);
  const favorites = useFavoriteBusinesses(postalCode || undefined);
  const [pendingCart] = useState(() => latestCart());
  const { categoria } = useParams();
  const [params, setParams] = useSearchParams();
  const category = categoryBySlug(categoria);
  const query = (params.get('q') ?? '').slice(0, MAX_SEARCH);
  const nearOn = params.get('cerca') === '1';
  // Each tap is an attempt that asks the browser again (after the customer allowed it); 0 is the page as it opened.
  const [nearAttempt, setNearAttempt] = useState(0);
  const near = useNearMe(nearOn, nearAttempt);
  const [draft, setDraft] = useState(query);
  // Back and forward change the address: the box shows its search again.
  const [shownQuery, setShownQuery] = useState(query);
  if (query !== shownQuery) {
    setShownQuery(query);
    if (draft.trim() !== query.trim()) setDraft(query);
  }

  // The address follows what is typed, a moment after the last key; «Cerca de mí» stays as it was.
  useEffect(() => {
    const words = draft.trim();
    if (words === query.trim()) return;
    const timer = window.setTimeout(() => {
      setParams((current) => {
        const next = new URLSearchParams(current);
        if (words) next.set('q', words);
        else next.delete('q');
        return next;
      }, { replace: true });
    }, SEARCH_DELAY_MS);
    return () => window.clearTimeout(timer);
  }, [draft, query, setParams]);

  const toggleNear = () => {
    // Turned on it asks the browser again; turned off it asks nothing.
    if (!nearOn) setNearAttempt((attempt) => attempt + 1);
    setParams((current) => {
      const next = new URLSearchParams(current);
      if (next.get('cerca') === '1') next.delete('cerca');
      else next.set('cerca', '1');
      return next;
    }, { replace: true });
  };

  // The title the server wrote for search engines (PublicPages.cs).
  useDocumentTitle(category ? `${category.label} · Explorar · TienditApp` : 'Explorar · TienditApp');

  const search: DirectorySearch = {
    category: category?.value,
    q: query.trim() || undefined,
    postalCode: postalCode || undefined,
    // Only once the browser answered: until then Explorar keeps its order.
    latitude: near.position?.latitude,
    longitude: near.position?.longitude,
    pageSize: PAGE_SIZE,
  };
  const directory = useQuery({
    queryKey: ['directory', search, 1],
    queryFn: ({ signal }) => publicDirectoryApi.getDirectory({ ...search, pageNumber: 1 }, signal),
    enabled: !categoria || !!category,
    // The same list near the customer reorders where it is, without going back to the skeleton.
    placeholderData: (previous, previousQuery) =>
      previousQuery && samePlaceSearch(previousQuery.queryKey[1] as DirectorySearch, search) ? previous : undefined,
  });
  // The categories slide sideways on a phone: the edge that hides more fades.
  const { ref, fadeClass } = useScrollFade<HTMLElement>(directory.data?.categories);
  // «Ver más» adds the next page under the ones shown; another category or search starts again at the first.
  const searchKey = JSON.stringify(search);
  const [more, setMore] = useState({ searchKey, pages: 1 });
  const pagesShown = more.searchKey === searchKey ? more.pages : 1;

  // An address with a category that does not exist goes to all of them.
  if (categoria && !category) return <Navigate to="/explorar" replace />;

  const first = directory.data;
  const firstItems = first?.businesses.items ?? [];
  const products = first?.products ?? [];
  const total = first?.businesses.totalCount ?? 0;
  const counts = new Map((first?.categories ?? []).map((item) => [item.category, item.count]));
  const anyListed = [...counts.values()].some((count) => count > 0);
  const chips = BUSINESS_CATEGORIES.filter((item) => (counts.get(item.value) ?? 0) > 0 || item.value === category?.value);
  // Another category keeps the search and «Cerca de mí».
  const kept = new URLSearchParams();
  if (query.trim()) kept.set('q', query.trim());
  if (nearOn) kept.set('cerca', '1');
  const searchSuffix = kept.toString() ? `?${kept.toString()}` : '';
  const hasMore = pagesShown < (first?.businesses.totalPages ?? 0);
  const remaining = Math.max(0, total - pagesShown * PAGE_SIZE);

  return (
    <div className="ex-page">
      <section className="ex-hero" aria-labelledby="explore-title">
        <h1 id="explore-title" className="ex-title">{category ? category.label : 'Compra en los negocios de tu ciudad'}</h1>
        <p className="ex-lead">Pide en línea a los que reciben pedidos ahora, o mira el menú de los demás.</p>
        <form className="ex-search" role="search" onSubmit={(event) => event.preventDefault()}>
          <label className="visually-hidden" htmlFor={searchId}>Buscar negocios y productos</label>
          <i className="ri-search-line ex-search-icon" aria-hidden="true"></i>
          <input
            id={searchId}
            type="search"
            className="form-control"
            placeholder="Busca un negocio o un producto"
            maxLength={MAX_SEARCH}
            autoComplete="off"
            enterKeyHint="search"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
          />
        </form>
        <PostalCodeChip
          postalCode={postalCode}
          onChange={(code) => {
            savePostalCode(code);
            setPostalCode(code);
          }}
        />
        <NearMeChip near={near} onToggle={toggleNear} onRetry={() => setNearAttempt((attempt) => attempt + 1)} />
      </section>

      {pendingCart && (
        <p className="ex-pending" role="status">
          <span className="flex-grow-1">
            <i className="ri-shopping-bag-3-line me-1" aria-hidden="true"></i>
            Tienes un pedido sin enviar en <strong>{pendingCart.businessName}</strong>
            {pendingCart.itemCount === 1 ? ' (1 producto)' : ` (${pendingCart.itemCount} productos)`}.
          </span>
          <Link className="btn btn-sm btn-primary" to={`/m/${pendingCart.slug}`}>Seguir con el pedido</Link>
        </p>
      )}

      {chips.length > 0 && (
        <nav ref={ref} className={`ex-chips ta-scroll-fade ${fadeClass}`} aria-label="Categorías">
          <Link className={`ex-chip ${category ? '' : 'active'}`} to={`/explorar${searchSuffix}`} aria-current={category ? undefined : 'page'}>
            Todos
          </Link>
          {chips.map((item) => (
            <Link
              key={item.value}
              className={`ex-chip ${item.value === category?.value ? 'active' : ''}`}
              to={`/explorar/${item.slug}${searchSuffix}`}
              aria-current={item.value === category?.value ? 'page' : undefined}
            >
              <i className={item.icon} aria-hidden="true"></i>
              {item.label}
              {counts.has(item.value) && <span className="ex-chip-count">{counts.get(item.value)}</span>}
            </Link>
          ))}
        </nav>
      )}

      {!query.trim() && !category && <FavoriteBusinesses favorites={favorites} />}

      {products.length > 0 && <DirectoryProducts products={products} />}

      {/* Products found and no business: the products are the results. */}
      {!(directory.isSuccess && total === 0 && products.length > 0) && (
        <section
          className="ex-results"
          aria-labelledby="explore-results"
          aria-busy={directory.isLoading || (directory.isPlaceholderData && directory.isFetching)}
        >
          <h2 id="explore-results" className={products.length > 0 ? 'ex-section-title' : 'visually-hidden'}>Negocios</h2>
          <p className="ex-count" aria-live="polite">{directory.isSuccess && total > 0 ? countLabel(total) : ''}</p>

          {directory.isLoading && (
            <ul className="ex-grid" aria-hidden="true">
              {Array.from({ length: 6 }, (_, index) => (
                <li key={index} className="ex-skeleton" />
              ))}
            </ul>
          )}

          {directory.isError && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">{getApiErrorMessage(directory.error, 'No se pudieron cargar los negocios.')}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void directory.refetch()}>Reintentar</button>
            </div>
          )}

          {directory.isSuccess && total === 0 && products.length === 0 && (
            <div className="ex-empty">
              <i className="ri-store-2-line" aria-hidden="true"></i>
              {query.trim() ? (
                <>
                  <h3 className="h5">No encontramos «{query.trim()}»</h3>
                  <p>Prueba con otra palabra{category ? ' o busca en todas las categorías' : ''}.</p>
                  <div className="d-flex flex-wrap justify-content-center gap-2">
                    <button type="button" className="btn btn-light" onClick={() => setDraft('')}>Borrar la búsqueda</button>
                    {category && <Link className="btn btn-light" to={`/explorar${searchSuffix}`}>Buscar en todas</Link>}
                  </div>
                </>
              ) : category && anyListed ? (
                <>
                  <h3 className="h5">Aún no hay negocios de {category.label.toLowerCase()}</h3>
                  <p>Mira los de otras categorías mientras llegan.</p>
                  <Link className="btn btn-light" to={`/explorar${searchSuffix}`}>Ver todos los negocios</Link>
                </>
              ) : (
                <>
                  <h3 className="h5">Pronto verás aquí los negocios de tu ciudad</h3>
                  <p>Cuando publiquen su menú en línea, aparecerán en Explorar.</p>
                </>
              )}
            </div>
          )}

          {firstItems.length > 0 && <BusinessList businesses={firstItems} favorites={favorites} />}
          {Array.from({ length: pagesShown - 1 }, (_, index) => (
            <MoreBusinesses key={index + 2} search={search} pageNumber={index + 2} favorites={favorites} />
          ))}

          {hasMore && (
            <div className="text-center mt-3">
              <button type="button" className="btn btn-light" onClick={() => setMore({ searchKey, pages: pagesShown + 1 })}>
                Ver más negocios ({remaining.toLocaleString('es-MX')})
              </button>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
