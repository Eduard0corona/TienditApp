import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { reviewsApi, type MyReview } from '../../api/reviewsApi';
import { useToast } from '../../hooks/useToast';
import { REVIEW_CONCERN_LABELS, REVIEW_TEXT_MAX_LENGTH, starsLabel } from '../../utils/reviews';
import Stars from './Stars';
import './Reviews.css';

export interface ReviewedBusiness {
  slug: string;
  name: string;
}

/** What the customer sees of their review once it was written: in their reviews, in the store and in the order. */
function reviewQueryKeys(slug: string) {
  return [['me', 'reviews'], ['public-reviews', slug], ['public-menu', slug]] as const;
}

function ReviewForm({ business, review, onClose }: { business: ReviewedBusiness; review: MyReview | null; onClose: () => void }) {
  const ids = useId();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [rating, setRating] = useState(review?.rating ?? 0);
  const [comment, setComment] = useState(review?.comment ?? '');
  const [deleting, setDeleting] = useState(false);
  const hidden = !!review?.hidden;
  const refresh = () => Promise.all(reviewQueryKeys(business.slug).map((queryKey) => queryClient.invalidateQueries({ queryKey })));

  // The dialog closes at once, and what shows the review asks for it again behind it.
  const save = useMutation({
    mutationFn: () => reviewsApi.saveMyReview(business.slug, { rating, comment: comment.trim() || null }),
    onSuccess: () => {
      toast.success(review ? 'Guardamos los cambios de tu reseña.' : `Publicamos tu reseña de ${business.name}.`);
      onClose();
      void refresh();
    },
  });
  const remove = useMutation({
    mutationFn: () => reviewsApi.deleteMyReview(business.slug),
    onSuccess: () => {
      toast.success('Borraste tu reseña.');
      onClose();
      void refresh();
    },
  });
  const busy = save.isPending || remove.isPending;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (hidden || rating === 0 || busy) return;
        save.mutate();
      }}
    >
      <Modal.Body>
        {hidden && review ? (
          <>
            <div className="alert alert-warning" role="status">
              La plataforma ocultó tu reseña
              {review.hiddenReason != null ? ` (${REVIEW_CONCERN_LABELS[review.hiddenReason].toLowerCase()})` : ''}: ya no se
              puede cambiar. Puedes borrarla.
            </div>
            <Stars rating={review.rating} />
            {review.comment && <p className="rv-comment mt-2 mb-0">{review.comment}</p>}
          </>
        ) : (
          <>
            <fieldset className="mb-3">
              <legend className="form-label small mb-1">Tu calificación</legend>
              <div className="rv-star-input">
                {[1, 2, 3, 4, 5].map((star) => (
                  <label key={star} className={star <= rating ? 'is-on' : ''}>
                    <input
                      type="radio"
                      className="visually-hidden"
                      name={`${ids}-rating`}
                      value={star}
                      checked={rating === star}
                      onChange={() => setRating(star)}
                      aria-label={star === 1 ? '1 estrella' : `${star} estrellas`}
                    />
                    <i className={star <= rating ? 'ri-star-fill' : 'ri-star-line'} aria-hidden="true"></i>
                  </label>
                ))}
              </div>
              {/* Each radio says its stars itself: this line is for the eyes. */}
              <div className="small text-muted">{rating ? starsLabel(rating) : 'Elige de 1 a 5 estrellas.'}</div>
            </fieldset>
            <label className="form-label small mb-1" htmlFor={`${ids}-comment`}>Cuéntanos cómo te fue (opcional)</label>
            <textarea
              id={`${ids}-comment`}
              className="form-control"
              rows={4}
              maxLength={REVIEW_TEXT_MAX_LENGTH}
              value={comment}
              onChange={(event) => setComment(event.target.value)}
              aria-describedby={`${ids}-comment-help`}
            />
            <div id={`${ids}-comment-help`} className="d-flex justify-content-between gap-2 small text-muted mt-1">
              <span>Se publica con tu nombre y la inicial de tu apellido. Sin enlaces, correos ni teléfonos.</span>
              <span className="ta-money text-nowrap">{comment.length}/{REVIEW_TEXT_MAX_LENGTH}</span>
            </div>
          </>
        )}
        {review?.reply && (
          <div className="rv-reply mt-3">
            <div className="small fw-semibold">Respuesta de {business.name}</div>
            <p className="mb-0">{review.reply}</p>
          </div>
        )}
        {save.error && <div className="alert alert-danger mt-3 mb-0" role="alert">{getApiErrorMessage(save.error, 'No se pudo guardar tu reseña.')}</div>}
        {remove.error && <div className="alert alert-danger mt-3 mb-0" role="alert">{getApiErrorMessage(remove.error, 'No se pudo borrar tu reseña.')}</div>}
      </Modal.Body>
      <Modal.Footer className="rv-dialog-footer">
        {deleting ? (
          <>
            <span className="me-auto small">¿Borrar tu reseña de {business.name}?</span>
            <button type="button" className="btn btn-light" onClick={() => setDeleting(false)} disabled={busy}>Volver</button>
            <button type="button" className="btn btn-danger" onClick={() => remove.mutate()} disabled={busy}>
              {remove.isPending ? 'Borrando…' : 'Borrar'}
            </button>
          </>
        ) : (
          <>
            {review && (
              <button type="button" className="btn btn-link text-danger px-0 me-auto" onClick={() => setDeleting(true)} disabled={busy}>
                Borrar reseña
              </button>
            )}
            <button type="button" className="btn btn-light" onClick={onClose}>{hidden ? 'Cerrar' : 'Cancelar'}</button>
            {!hidden && (
              <button type="submit" className="btn btn-primary" disabled={rating === 0 || busy}>
                {save.isPending ? 'Guardando…' : review ? 'Guardar cambios' : 'Publicar'}
              </button>
            )}
          </>
        )}
      </Modal.Footer>
    </form>
  );
}

/**
 * Where a customer account rates a business (Fase 24H): from one to five stars and a comment, once it received an order
 * or attended an appointment there; the same dialog changes it or deletes it. One the platform hid only shows why and can
 * be deleted. The API decides who may review and what a comment may say.
 */
export default function ReviewDialog({ business, onClose }: { business: ReviewedBusiness | null; onClose: () => void }) {
  const titleId = useId();
  // While it fades out it keeps showing the business it was opened for.
  const [shown, setShown] = useState(business);
  if (business && business !== shown) setShown(business);
  const current = business ?? shown;
  const mine = useQuery({
    queryKey: ['me', 'reviews', current?.slug],
    queryFn: ({ signal }) => reviewsApi.getMyReview(current?.slug ?? '', signal),
    enabled: business !== null,
  });
  const review = mine.data?.review ?? null;

  return (
    <Modal show={business !== null} onHide={onClose} centered fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">
          {review ? `Tu reseña de ${current?.name}` : `Califica a ${current?.name}`}
        </Modal.Title>
      </Modal.Header>
      {mine.isPending ? (
        <Modal.Body><p className="text-muted mb-0" aria-busy="true">Cargando…</p></Modal.Body>
      ) : mine.isError ? (
        <Modal.Body>
          <div className="alert alert-danger mb-0" role="alert">{getApiErrorMessage(mine.error, 'No se pudo abrir tu reseña.')}</div>
        </Modal.Body>
      ) : current && (review || mine.data?.canReview) ? (
        // Another business starts the form from what it keeps; the same one keeps what is being written, also while the
        // review is asked for again after a save (the dialog is gone by then).
        <ReviewForm key={current.slug} business={current} review={review} onClose={onClose} />
      ) : (
        <Modal.Body>
          <p className="mb-0">Podrás calificar a {current?.name} cuando recibas un pedido o vayas a una cita con tu cuenta.</p>
        </Modal.Body>
      )}
    </Modal>
  );
}
