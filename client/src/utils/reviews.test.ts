import { describe, expect, it } from 'vitest';
import { ReviewConcern, ReviewStatus } from '../api/generated/enums';
import type { BusinessReview } from '../api/reviewsApi';
import { canReportReview, formatAverage, ratingDescription, REVIEW_CONCERN_LABELS, REVIEW_CONCERNS, starsLabel, storeRatingName } from './reviews';

function review(overrides: Partial<BusinessReview> = {}): BusinessReview {
  return {
    id: 'r1',
    authorName: 'Mariana O.',
    rating: 4,
    writtenAt: '2026-10-10T18:00:00Z',
    status: ReviewStatus.Published,
    ...overrides,
  };
}

describe('reviews', () => {
  it('says the rating only from three reviews, as the API sends it', () => {
    expect(ratingDescription(undefined)).toBeNull();
    expect(ratingDescription({ count: 0 })).toBeNull();
    expect(ratingDescription({ count: 1 })).toBe('1 reseña');
    expect(ratingDescription({ count: 2, average: null })).toBe('2 reseñas');
    expect(ratingDescription({ count: 1234, average: 4.6 })).toBe('Calificación 4.6 de 5, 1,234 reseñas');
    expect(ratingDescription({ count: 3, average: 5 })).toBe('Calificación 5.0 de 5, 3 reseñas');
  });

  it('names the store\'s rating starting with what it shows', () => {
    expect(storeRatingName({ count: 23, average: 4.6 })).toBe('4.6 de 5 (23 reseñas)');
    expect(storeRatingName({ count: 2 })).toBe('2 reseñas');
  });

  it('writes the average with one decimal and the stars in words', () => {
    expect(formatAverage(4)).toBe('4.0');
    expect(formatAverage(3.5)).toBe('3.5');
    expect(starsLabel(4)).toBe('4 de 5 estrellas');
    expect(starsLabel(4.6)).toBe('4.6 de 5 estrellas');
  });

  it('names every reason of the list the API takes', () => {
    expect(REVIEW_CONCERNS).toEqual([1, 2, 3, 4, 5]);
    expect(REVIEW_CONCERNS.every((concern) => REVIEW_CONCERN_LABELS[concern])).toBe(true);
  });

  it('lets the business report a published review once, and a kept one again only after its author changed it', () => {
    expect(canReportReview(review())).toBe(true);
    expect(canReportReview(review({ reportedAt: '2026-10-11T10:00:00Z', reportReason: ReviewConcern.Spam }))).toBe(false);
    expect(canReportReview(review({ status: ReviewStatus.Hidden, hiddenReason: ReviewConcern.Offensive }))).toBe(false);

    const kept = review({ moderatedAt: '2026-10-12T10:00:00Z' });
    expect(canReportReview(kept)).toBe(false);
    expect(canReportReview({ ...kept, editedAt: '2026-10-11T10:00:00Z' })).toBe(false);
    expect(canReportReview({ ...kept, editedAt: '2026-10-13T10:00:00Z' })).toBe(true);
  });
});
