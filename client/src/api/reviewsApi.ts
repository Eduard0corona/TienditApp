import { apiRequest, type Schemas } from './typedClient';

export type RatingSummary = Schemas['RatingSummaryDto'];
export type PublicReview = Schemas['PublicReviewDto'];
export type PublicReviews = Schemas['PublicReviewsDto'];
export type MyReview = Schemas['MyReviewDto'];
/** Without a review yet it is null: the contract loses that a reference can be null. */
export type MyReviewOfBusiness = Omit<Schemas['MyReviewOfBusinessDto'], 'review'> & { review?: MyReview | null };
export type MyReviewInput = Schemas['MyReviewRequest'];
export type BusinessReview = Schemas['BusinessReviewDto'];
export type BusinessReviews = Schemas['BusinessReviewsDto'];
export type PlatformReview = Schemas['PlatformReviewDto'];
export type BusinessReviewsFilter = Schemas['BusinessReviewsFilter'];
export type PlatformReviewsFilter = Schemas['PlatformReviewsFilter'];
export type ReviewConcern = Schemas['ReviewConcern'];

/**
 * The reviews of the businesses (Fase 24H, ADR 0030): the ones a store shows to everyone, the customer's own, the ones
 * its business answers and reports, and the ones the platform moderates. The API decides who may do each.
 */
export const reviewsApi = {
  /** The published reviews of a store, newest first and ten at a time, with its rating. No sign-in. */
  getStoreReviews: (slug: string, page: number, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/public/menu/{slug}/reviews', { path: { slug }, query: { page }, signal }),

  /** The customer account's reviews in every business, the last one first, hidden ones included. */
  getMyReviews: (signal?: AbortSignal) => apiRequest('get', '/api/v1/me/reviews', { signal }),

  /** Whether the account may review this store (it received an order or attended an appointment) and its review. */
  getMyReview: (slug: string, signal?: AbortSignal): Promise<MyReviewOfBusiness> =>
    apiRequest('get', '/api/v1/me/reviews/{slug}', { path: { slug }, signal }),

  /** Writes the account's review of the store, or changes it. */
  saveMyReview: (slug: string, body: MyReviewInput) =>
    apiRequest('put', '/api/v1/me/reviews/{slug}', { path: { slug }, body }),

  deleteMyReview: (slug: string) => apiRequest('delete', '/api/v1/me/reviews/{slug}', { path: { slug } }),

  /** The reviews of the signed-in business with what is waiting for it: the unanswered and the reported ones. */
  getBusinessReviews: (filter: BusinessReviewsFilter, page: number, pageSize: number, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/reviews', { query: { filter, page, pageSize }, signal }),

  /** The business answers once; answering again changes its answer. */
  reply: (id: string, text: string) => apiRequest('put', '/api/v1/reviews/{id}/reply', { path: { id }, body: { text } }),

  removeReply: (id: string) => apiRequest('delete', '/api/v1/reviews/{id}/reply', { path: { id } }),

  /** Asks the platform to look at it: the review stays published until the platform decides. */
  report: (id: string, reason: ReviewConcern) =>
    apiRequest('post', '/api/v1/reviews/{id}/report', { path: { id }, body: { reason } }),

  /** The platform's queue: the reported reviews, the oldest report first, or the hidden ones. */
  getPlatformReviews: (filter: PlatformReviewsFilter, page: number, pageSize: number, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/platform/reviews', { query: { filter, page, pageSize }, signal }),

  hide: (id: string, reason: ReviewConcern) =>
    apiRequest('post', '/api/v1/platform/reviews/{id}/hide', { path: { id }, body: { reason } }),

  keep: (id: string) => apiRequest('post', '/api/v1/platform/reviews/{id}/keep', { path: { id } }),

  restore: (id: string) => apiRequest('post', '/api/v1/platform/reviews/{id}/restore', { path: { id } }),
};
