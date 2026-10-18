import { asOption, type DemoData } from '../fixtures';
import { uuid } from '../mocks/data.mjs';
import { demoDirectory } from '../mocks/routes-p9.mjs';
import { ratingOf } from '../mocks/routes-p14.mjs';
import { withProductPhotos } from './catalogo-inventario.mocks';

// The reviews of Café La Esquina (Fase 24H, ADR 0030): seven published, one with the owner's answer and one edited, one
// the owner reported and one the platform hid. The store, Explorar, the owner's Reseñas and the platform's console read
// them from `data.reviews` (mocks/routes-p14.mjs).

// ReviewStatus and ReviewConcern as the contract numbers them.
const PUBLISHED = 0;
const HIDDEN = 1;
const OFFENSIVE = 1;
const NOT_AN_EXPERIENCE = 4;

interface DemoReview {
  id: string;
  authorName: string;
  rating: number;
  comment: string | null;
  writtenAt: string;
  editedAt?: string | null;
  reply?: string | null;
  repliedAt?: string | null;
  status: number;
  reportedAt?: string | null;
  reportReason?: number | null;
  hiddenReason?: number | null;
  moderatedAt?: string | null;
}

/** A review of Café La Esquina, as the fake backend keeps it: with its business for the platform's console. */
function review(id: string, fields: Omit<DemoReview, 'id' | 'status'> & { status?: number }) {
  return {
    id: uuid(`review:${id}`), tenantId: uuid('tenant:cafe-la-esquina'), slug: 'cafe-la-esquina', businessName: 'Café La Esquina',
    status: PUBLISHED, ...fields,
  };
}

export function demoReviews() {
  return [
    review('lucia', {
      authorName: 'Lucía R.', rating: 5, writtenAt: '2026-10-03T16:10:00.000Z',
      comment: 'Los chilaquiles verdes son los mejores de la Roma. Llegaron calientes y bien empacados.',
      reply: '¡Gracias, Lucía! Te esperamos el domingo para el pan recién salido.', repliedAt: '2026-10-03T18:00:00.000Z',
    }),
    review('carla', {
      authorName: 'Carla V.', rating: 1, writtenAt: '2026-10-02T21:40:00.000Z',
      comment: 'Vengan mejor a mi cafetería de la otra cuadra, aquí todo es caro.',
      reportedAt: '2026-10-03T09:15:00.000Z', reportReason: NOT_AN_EXPERIENCE,
    }),
    review('jorge', {
      authorName: 'Jorge M.', rating: 4, writtenAt: '2026-10-01T14:05:00.000Z',
      comment: 'Buen café y rápido. El pan dulce se acabó temprano.',
    }),
    review('ana', {
      authorName: 'Ana P.', rating: 5, writtenAt: '2026-09-29T17:30:00.000Z', editedAt: '2026-09-30T08:00:00.000Z',
      comment: 'El latte con leche de almendra, perfecto. Ya es mi lugar de cada mañana.',
    }),
    review('pedro', {
      authorName: 'Pedro G.', rating: 1, writtenAt: '2026-09-28T20:00:00.000Z', comment: 'Son unos rateros y unos inútiles.',
      status: HIDDEN, hiddenReason: OFFENSIVE, moderatedAt: '2026-09-29T10:00:00.000Z',
    }),
    review('roberto', {
      authorName: 'Roberto S.', rating: 3, writtenAt: '2026-09-26T19:45:00.000Z',
      comment: 'Rico, pero mi pedido tardó 50 minutos.',
      reply: 'Lo sentimos, Roberto. Ese día tuvimos mucha demanda; ya sumamos un repartidor.', repliedAt: '2026-09-27T09:00:00.000Z',
    }),
    review('sofia', { authorName: 'Sofía L.', rating: 5, writtenAt: '2026-09-21T15:20:00.000Z', comment: null }),
    review('miguel', {
      authorName: 'Miguel A.', rating: 4, writtenAt: '2026-09-14T13:00:00.000Z',
      comment: 'Las conchas de vainilla, suaves y del día.',
    }),
  ];
}

/** The store dressed (its cover and photos), with its reviews. */
function storeWithReviews(data: DemoData) {
  withProductPhotos(data);
  data.menuStore = true;
  data.reviews = demoReviews();
}

/** María Fernanda received an order of Café La Esquina: she may rate it, and has not yet. */
function customerMayReview(data: DemoData) {
  storeWithReviews(data);
  data.reviewable = ['cafe-la-esquina'];
}

/** The platform hid what María Fernanda wrote for having someone's personal data: her order says «Tu reseña». */
function customerReviewHidden(data: DemoData) {
  customerMayReview(data);
  data.myReviews = [{
    slug: 'cafe-la-esquina', businessName: 'Café La Esquina', logoUrl: '/visual-img/logo.svg', rating: 2,
    comment: 'La cajera Rocío Hernández nos atendió de malas.', writtenAt: '2026-09-30T18:00:00.000Z',
    hidden: true, hiddenReason: 2,
  }];
}

/** Café La Esquina in Explorar with its rating, and Taquería El Güero with too few reviews for one. */
function directoryWithRatings(data: DemoData) {
  data.reviews = demoReviews();
  data.directory = demoDirectory().map((business: { slug: string }) => (business.slug === 'cafe-la-esquina'
    ? { ...business, rating: ratingOf(data.reviews) }
    : business.slug === 'taqueria-el-guero' ? { ...business, rating: { count: 2, average: null } } : business));
}

export const TIENDA_CON_RESENAS = asOption(storeWithReviews);
export const PUEDE_CALIFICAR = asOption(customerMayReview);
export const RESENA_OCULTA = asOption(customerReviewHidden);
export const EXPLORAR_CON_CALIFICACION = asOption(directoryWithRatings);
