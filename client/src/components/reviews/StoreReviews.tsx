import { useEffect, useRef, useState, type Ref } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { reviewsApi, type PublicReview, type RatingSummary } from '../../api/reviewsApi';
import { formatDate } from '../../utils/dates';
import { formatAverage, reviewCountLabel } from '../../utils/reviews';
import ReviewDialog, { type ReviewedBusiness } from './ReviewDialog';
import Stars from './Stars';
import './Reviews.css';

/** The element of the reviews of a store, where its rating in the header takes. */
export const STORE_REVIEWS_ID = 'pm-reviews';

function ReviewItem({ review, businessName, itemRef }: {
  review: PublicReview;
  businessName: string;
  itemRef?: Ref<HTMLLIElement>;
}) {
  return (
    <li className="rv-item" ref={itemRef} tabIndex={itemRef ? -1 : undefined}>
      <div className="rv-item-head">
        <span className="rv-author">{review.authorName}</span>
        <Stars rating={review.rating} />
        <span className="small text-muted">{formatDate(review.writtenAt)}{review.edited ? ' · Editada' : ''}</span>
      </div>
      {review.comment && <p className="rv-comment mb-0">{review.comment}</p>}
      {review.reply && (
        <div className="rv-reply mt-2">
          <div className="small fw-semibold">Respuesta de {businessName}</div>
          <p className="mb-0">{review.reply}</p>
        </div>
      )}
    </li>
  );
}

/**
 * One page of the reviews: the last one shown offers the next. Each page asks for itself (not useInfiniteQuery, whose code
 * would go into what every first visit downloads).
 */
function ReviewsPage({ slug, businessName, page, last, onMore }: {
  slug: string;
  businessName: string;
  page: number;
  last: boolean;
  onMore: () => void;
}) {
  const reviews = useQuery({
    queryKey: ['public-reviews', slug, page],
    queryFn: ({ signal }) => reviewsApi.getStoreReviews(slug, page, signal),
    staleTime: 60_000,
  });
  // A page asked for with «Ver más» takes the focus to its first review once it arrives (its button is gone): once.
  const first = useRef<HTMLLIElement>(null);
  const focused = useRef(page === 1);
  useEffect(() => {
    if (focused.current || !reviews.data) return;
    focused.current = true;
    first.current?.focus();
  }, [reviews.data]);
  if (reviews.isPending) return <li className="rv-item text-muted" aria-busy="true">Cargando reseñas…</li>;
  if (reviews.isError) {
    return (
      <li className="rv-item">
        <div className="alert alert-danger mb-0" role="alert">{getApiErrorMessage(reviews.error, 'No se pudieron cargar las reseñas.')}</div>
      </li>
    );
  }
  return (
    <>
      {reviews.data.items.map((item, index) => (
        <ReviewItem key={item.id} review={item} businessName={businessName} itemRef={index === 0 && page > 1 ? first : undefined} />
      ))}
      {last && reviews.data.hasNextPage && (
        <li className="rv-item">
          <button type="button" className="btn btn-light w-100" onClick={onMore}>Ver más reseñas</button>
        </li>
      )}
    </>
  );
}

interface StoreReviewsProps {
  business: ReviewedBusiness;
  summary: RatingSummary | null | undefined;
  /** A customer account signed in: it may rate the business once it bought there (the API says). */
  customer: boolean;
}

/**
 * The reviews of a store after its menu (Fase 24H): its average from three reviews, the reviews newest first ten at a
 * time with the business's answers, and «Calificar» for a customer account that received an order or attended an
 * appointment there. Without reviews, the section shows only to whoever can write the first one.
 */
export default function StoreReviews({ business, summary, customer }: StoreReviewsProps) {
  const [writing, setWriting] = useState(false);
  const [pages, setPages] = useState(1);
  const count = summary?.count ?? 0;
  const mine = useQuery({
    queryKey: ['me', 'reviews', business.slug],
    queryFn: ({ signal }) => reviewsApi.getMyReview(business.slug, signal),
    enabled: customer,
  });
  const review = mine.data?.review ?? null;
  const canWrite = !!review || !!mine.data?.canReview;
  if (count === 0 && !canWrite) return null;

  return (
    <section id={STORE_REVIEWS_ID} className="rv-section" aria-labelledby={`${STORE_REVIEWS_ID}-title`}>
      <h2 id={`${STORE_REVIEWS_ID}-title`} className="pm-section-title">Reseñas</h2>
      <div className="rv-summary">
        {summary?.average != null ? (
          <div className="rv-average">
            {/* The stars say it in words: the number is for the eyes. */}
            <span className="rv-average-number ta-money" aria-hidden="true">{formatAverage(summary.average)}</span>
            <span>
              <Stars rating={summary.average} />
              <span className="d-block small text-muted">{reviewCountLabel(count)}</span>
            </span>
          </div>
        ) : (
          <p className="text-muted mb-0">
            {count === 0 ? 'Todavía no hay reseñas: la tuya puede ser la primera.' : `${reviewCountLabel(count)}. La calificación aparece desde 3 reseñas.`}
          </p>
        )}
        {canWrite && (
          <button type="button" className="btn btn-light btn-touch" onClick={() => setWriting(true)}>
            <i className={`${review ? 'ri-edit-line' : 'ri-star-line'} me-1`} aria-hidden="true"></i>
            {review ? (review.hidden ? 'Ver tu reseña' : 'Cambiar tu reseña') : 'Calificar'}
          </button>
        )}
      </div>

      {count > 0 && (
        <ul className="rv-list">
          {Array.from({ length: pages }, (_, index) => (
            <ReviewsPage
              key={index}
              slug={business.slug}
              businessName={business.name}
              page={index + 1}
              last={index + 1 === pages}
              onMore={() => setPages(pages + 1)}
            />
          ))}
        </ul>
      )}

      <ReviewDialog business={writing ? business : null} onClose={() => setWriting(false)} />
    </section>
  );
}
