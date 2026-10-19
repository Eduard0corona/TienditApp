// Phase 24H endpoints for the fake backend: the reviews of the businesses (ADR 0030). The store's reviews
// (GET /public/menu/{slug}/reviews, ten a page), the customer account's own (GET /me/reviews, GET, PUT and DELETE
// /me/reviews/{slug}), the business's (GET /reviews with its filter, the answer and the report) and the platform's
// (GET /platform/reviews, hide, keep and restore). They are `data.reviews` (BusinessReviewDto of Café La Esquina, with
// the slug and name of its business), `data.reviewable` (the slugs where the account bought) and `data.myReviews`; a
// scene file fills them in its `prepareData`. Without them nobody has reviews, and the screens show none.
import { NOW_ISO } from './data.mjs';

// ReviewStatus and the filters as the contract numbers them.
const PUBLISHED = 0;
const HIDDEN = 1;
const ALL = 0;
const UNANSWERED = 1;
const REPORTED = 2;

/** A page of the reviews asked with `page` and `pageSize`, as the reviews' endpoints take them. */
function pageOf(items, q, defaultSize) {
  const pageNumber = Math.max(1, parseInt(q.get('page') ?? '1', 10) || 1);
  const pageSize = Math.max(1, parseInt(q.get('pageSize') ?? String(defaultSize), 10) || defaultSize);
  const totalCount = items.length;
  const totalPages = Math.ceil(totalCount / pageSize);
  return {
    items: items.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
    pageNumber,
    totalPages,
    totalCount,
    hasPreviousPage: pageNumber > 1,
    hasNextPage: pageNumber < totalPages,
  };
}

/** The rating as the API sends it: the average to one decimal only from three reviews. */
export function ratingOf(reviews) {
  const published = reviews.filter((review) => review.status === PUBLISHED);
  const count = published.length;
  const average = count >= 3 ? Math.round((published.reduce((sum, review) => sum + review.rating, 0) / count) * 10) / 10 : null;
  return { count, average };
}

/** What the business sees of a review: the platform's fields go out. */
function forBusiness(review) {
  const { slug: _slug, businessName: _businessName, tenantId: _tenantId, ...rest } = review;
  return rest;
}

export function phase24hRoutes(data, { ok }) {
  data.reviews ??= [];
  data.reviewable ??= [];
  data.myReviews ??= [];
  const notFound = () => ({ status: 404, json: { status: 404, title: 'Not Found', detail: 'La reseña no existe.', code: 'not_found' } });
  const find = (id) => data.reviews.find((review) => review.id === id);

  return [
    ['GET', /^\/public\/menu\/([^/]+)\/reviews$/, (m, q) => {
      const published = data.reviews.filter((review) => review.slug === m[1] && review.status === PUBLISHED);
      const page = pageOf(published, q, 10);
      return ok({
        summary: ratingOf(published),
        items: page.items.map((review) => ({
          id: review.id, authorName: review.authorName, rating: review.rating, comment: review.comment ?? null,
          writtenAt: review.writtenAt, edited: !!review.editedAt, reply: review.reply ?? null, repliedAt: review.repliedAt ?? null,
        })),
        pageNumber: page.pageNumber,
        hasNextPage: page.hasNextPage,
      });
    }],
    ['GET', /^\/me\/reviews$/, () => ok(data.myReviews)],
    ['GET', /^\/me\/reviews\/([^/]+)$/, (m) => ok({
      canReview: data.reviewable.includes(m[1]),
      review: data.myReviews.find((review) => review.slug === m[1]) ?? null,
    })],
    ['PUT', /^\/me\/reviews\/([^/]+)$/, (m, _q, body) => {
      const kept = data.myReviews.find((review) => review.slug === m[1]);
      const review = { ...(kept ?? { slug: m[1], businessName: data.tenant.name, writtenAt: NOW_ISO, hidden: false }), rating: body.rating, comment: body.comment ?? null };
      if (kept) review.editedAt = NOW_ISO;
      data.myReviews = [review, ...data.myReviews.filter((other) => other.slug !== m[1])];
      return ok(review);
    }],
    ['DELETE', /^\/me\/reviews\/([^/]+)$/, (m) => {
      data.myReviews = data.myReviews.filter((review) => review.slug !== m[1]);
      return { status: 204, json: null };
    }],
    ['GET', /^\/reviews$/, (_m, q) => {
      const filter = Number(q.get('filter') ?? ALL);
      const mine = data.reviews.filter((review) => review.slug === data.tenant.slug);
      const shown = mine.filter((review) => (filter === ALL ? true
        : filter === UNANSWERED ? review.status === PUBLISHED && !review.reply
        : filter === REPORTED ? !!review.reportedAt
        : review.status === HIDDEN));
      return ok({
        summary: ratingOf(mine),
        unanswered: mine.filter((review) => review.status === PUBLISHED && !review.reply).length,
        reported: mine.filter((review) => review.reportedAt).length,
        reviews: { ...pageOf(shown, q, 20), items: pageOf(shown, q, 20).items.map(forBusiness) },
      });
    }],
    ['PUT', /^\/reviews\/([^/]+)\/reply$/, (m, _q, body) => {
      const review = find(m[1]);
      if (!review) return notFound();
      Object.assign(review, { reply: body.text, repliedAt: NOW_ISO });
      return ok(forBusiness(review));
    }],
    ['DELETE', /^\/reviews\/([^/]+)\/reply$/, (m) => {
      const review = find(m[1]);
      if (!review) return notFound();
      Object.assign(review, { reply: null, repliedAt: null });
      return ok(forBusiness(review));
    }],
    ['POST', /^\/reviews\/([^/]+)\/report$/, (m, _q, body) => {
      const review = find(m[1]);
      if (!review) return notFound();
      Object.assign(review, { reportedAt: NOW_ISO, reportReason: body.reason });
      return ok(forBusiness(review));
    }],
    ['GET', /^\/platform\/reviews$/, (_m, q) => {
      const hidden = Number(q.get('filter') ?? 0) === 1;
      const shown = data.reviews
        .filter((review) => (hidden ? review.status === HIDDEN : !!review.reportedAt))
        .map((review) => ({ ...forBusiness(review), tenantId: review.tenantId, businessName: review.businessName, businessSlug: review.slug }));
      return ok(pageOf(shown, q, 20));
    }],
    ['POST', /^\/platform\/reviews\/([^/]+)\/(hide|keep|restore)$/, (m, _q, body) => {
      const review = find(m[1]);
      if (!review) return notFound();
      if (m[2] === 'hide') Object.assign(review, { status: HIDDEN, hiddenReason: body.reason, reportedAt: null, reportReason: null, moderatedAt: NOW_ISO });
      if (m[2] === 'keep') Object.assign(review, { reportedAt: null, reportReason: null, moderatedAt: NOW_ISO });
      if (m[2] === 'restore') Object.assign(review, { status: PUBLISHED, hiddenReason: null, moderatedAt: NOW_ISO });
      return { status: 204, json: null };
    }],
  ];
}
