import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { reviewsApi, type MyReview } from '../api/reviewsApi';

/**
 * The customer account's reviews by the slug of each store (Fase 24H): its orders and appointments say «Tu reseña» where
 * it already rated the business, and «Calificar» where it has not.
 */
export function useMyReviews(enabled = true): ReadonlyMap<string, MyReview> {
  const reviews = useQuery({
    queryKey: ['me', 'reviews'],
    queryFn: ({ signal }) => reviewsApi.getMyReviews(signal),
    enabled,
    staleTime: 60_000,
  });
  return useMemo(() => new Map((reviews.data ?? []).map((review) => [review.slug, review])), [reviews.data]);
}
