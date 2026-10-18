import { asOption, type DemoData } from '../fixtures';
import { withProductPhotos } from './catalogo-inventario.mocks';

// The online menu with the shape of the delivery apps (Fase 24E): Café La Esquina dressed as a store, with its cover, its
// logo and the demo photos of its products, and its owner's card for the cover in Configuración.

const COVER = '/visual-img/portada.svg';

/** The store dressed: the menu answers with its cover and logo (publico-cuenta-marco.mocks.ts) and the photos. */
function dressedStore(data: DemoData) {
  withProductPhotos(data);
  data.menuStore = true;
}

/** The owner uploaded a cover: the profile has its variants, all the same demo picture. */
function withCover(data: DemoData) {
  data.tenant.coverImages = { original: COVER, large: COVER, medium: COVER, small: COVER, thumbnail: COVER };
  data.tenant.logoUrl = '/visual-img/logo.svg';
}

/** The store dressed, and María Fernanda keeps it among her favorites (Fase 24F): its heart is full. */
function favoriteStore(data: DemoData) {
  dressedStore(data);
  data.favorites = [{ slug: 'cafe-la-esquina', name: 'Café La Esquina', logoUrl: '/visual-img/logo.svg', savedAt: '2026-09-28T16:00:00.000Z' }];
}

export const TIENDA = asOption(dressedStore);
export const TIENDA_FAVORITA = asOption(favoriteStore);
export const CON_PORTADA = asOption(withCover);
