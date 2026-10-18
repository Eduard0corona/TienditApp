import { ReviewConcern, ReviewStatus } from '../api/generated/enums';
import type { BusinessReview, RatingSummary } from '../api/reviewsApi';

/** The longest comment of a review and the longest answer of its business (BusinessReview in the API). */
export const REVIEW_TEXT_MAX_LENGTH = 500;

/** What is wrong with a review, in the words of the list the business reports from and the platform hides with. */
export const REVIEW_CONCERN_LABELS: Record<ReviewConcern, string> = {
  [ReviewConcern.Offensive]: 'Insultos, amenazas u odio',
  [ReviewConcern.PersonalData]: 'Datos personales de alguien',
  [ReviewConcern.Spam]: 'Publicidad o nada que ver con el negocio',
  [ReviewConcern.NotAnExperience]: 'No habla de un pedido o una cita',
  [ReviewConcern.Other]: 'Otro motivo',
};

export const REVIEW_CONCERNS = Object.values(ReviewConcern);

/** «4.6», with one decimal as the API rounds it. */
export function formatAverage(average: number): string {
  return average.toLocaleString('es-MX', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

export function reviewCountLabel(count: number): string {
  return count === 1 ? '1 reseña' : `${count.toLocaleString('es-MX')} reseñas`;
}

/** «4 de 5 estrellas», what a screen reader says of the stars. */
export function starsLabel(rating: number): string {
  return `${Number.isInteger(rating) ? rating : formatAverage(rating)} de 5 estrellas`;
}

/**
 * How a store or its card says its rating: «Calificación 4.6 de 5, 23 reseñas». The API sends the average only from three
 * reviews; with fewer there is only the count, and with none nothing.
 */
export function ratingDescription(summary: RatingSummary | null | undefined): string | null {
  if (!summary || summary.count === 0) return null;
  const count = reviewCountLabel(summary.count);
  return summary.average != null ? `Calificación ${formatAverage(summary.average)} de 5, ${count}` : count;
}

/**
 * The name of the store's rating button: it starts with what it shows («4.6 (23)»), so a voice command with the number
 * finds it: «4.6 de 5 (23 reseñas)», or only how many there are with fewer than three.
 */
export function storeRatingName(summary: RatingSummary): string {
  return summary.average != null
    ? `${formatAverage(summary.average)} de 5 (${reviewCountLabel(summary.count)})`
    : reviewCountLabel(summary.count);
}

/**
 * Whether the business can still report a review, as the API decides it: a published one without a report waiting, and
 * not one the platform already kept unless its author changed it after that. A navigation aid: the API still decides.
 */
export function canReportReview(review: BusinessReview): boolean {
  if (review.status !== ReviewStatus.Published || review.reportedAt) return false;
  if (!review.moderatedAt) return true;
  return !!review.editedAt && Date.parse(review.editedAt) > Date.parse(review.moderatedAt);
}
