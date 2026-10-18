import { ReviewStatus } from '../../api/generated/enums';
import type { BusinessReview } from '../../api/reviewsApi';
import { formatDate } from '../../utils/dates';
import { REVIEW_CONCERN_LABELS } from '../../utils/reviews';
import Stars from './Stars';
import './Reviews.css';

/** A review in a list of the business or the platform (Fase 24H): who, how many stars, when, what it says and the answer. */
export function ReviewContent({ review, replyLabel }: { review: BusinessReview; replyLabel: string }) {
  return (
    <div className="rv-cell">
      <div className="rv-item-head">
        <span className="rv-author">{review.authorName}</span>
        <Stars rating={review.rating} />
        <span className="small text-muted">{formatDate(review.writtenAt)}{review.editedAt ? ' · Editada' : ''}</span>
      </div>
      {review.comment ? <p className="rv-comment mb-0">{review.comment}</p> : <p className="small text-muted mb-0">Sin comentario.</p>}
      {review.reply && (
        <div className="rv-reply mt-2">
          <div className="small fw-semibold">{replyLabel}</div>
          <p className="mb-0">{review.reply}</p>
        </div>
      )}
    </div>
  );
}

/** Where a review stands: published, waiting for the platform after a report, or hidden, with its reason. */
export function ReviewStatusChip({ review }: { review: BusinessReview }) {
  if (review.status === ReviewStatus.Hidden) {
    return (
      <>
        <span className="ta-chip ta-chip-cancelled">Oculta</span>
        {review.hiddenReason != null && <div className="small text-muted mt-1">{REVIEW_CONCERN_LABELS[review.hiddenReason]}</div>}
      </>
    );
  }
  if (review.reportedAt) {
    return (
      <>
        <span className="ta-chip ta-chip-new">En revisión</span>
        {review.reportReason != null && <div className="small text-muted mt-1">{REVIEW_CONCERN_LABELS[review.reportReason]}</div>}
      </>
    );
  }
  return <span className="ta-chip ta-chip-ready">Publicada</span>;
}
