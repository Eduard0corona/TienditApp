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

export const TIENDA = asOption(dressedStore);
export const CON_PORTADA = asOption(withCover);
