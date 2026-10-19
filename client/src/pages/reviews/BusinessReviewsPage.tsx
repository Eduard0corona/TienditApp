import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { BusinessReviewsFilter, ReviewStatus } from '../../api/generated/enums';
import { reviewsApi, type BusinessReview } from '../../api/reviewsApi';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import ConcernDialog from '../../components/reviews/ConcernDialog';
import { ReviewContent, ReviewStatusChip } from '../../components/reviews/ReviewContent';
import Stars from '../../components/reviews/Stars';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { canReportReview, formatAverage, REVIEW_TEXT_MAX_LENGTH, reviewCountLabel } from '../../utils/reviews';

const COLUMNS = [
  { header: 'Reseña' },
  { header: 'Estado' },
  { header: 'Acciones', className: 'text-center' },
];

const FILTERS: Array<{ value: BusinessReviewsFilter; label: string }> = [
  { value: BusinessReviewsFilter.All, label: 'Todas' },
  { value: BusinessReviewsFilter.Unanswered, label: 'Sin responder' },
  { value: BusinessReviewsFilter.Reported, label: 'Reportadas' },
  { value: BusinessReviewsFilter.Hidden, label: 'Ocultas' },
];

const PAGE_SIZES = [20, 50];

/** The business answers a review once; answering again changes its answer. */
function ReplyDialog({ review, onClose }: { review: BusinessReview | null; onClose: () => void }) {
  const titleId = useId();
  // While it fades out it keeps showing the review it was opened for.
  const [shown, setShown] = useState(review);
  if (review && review !== shown) setShown(review);
  const current = review ?? shown;
  return (
    <Modal show={review !== null} onHide={onClose} centered fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">
          {current?.reply ? 'Cambiar respuesta' : 'Responder'} a {current?.authorName}
        </Modal.Title>
      </Modal.Header>
      {current && <ReplyForm key={current.id} review={current} onClose={onClose} />}
    </Modal>
  );
}

function ReplyForm({ review, onClose }: { review: BusinessReview; onClose: () => void }) {
  const ids = useId();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [text, setText] = useState(review.reply ?? '');
  const save = useMutation({
    mutationFn: () => reviewsApi.reply(review.id, text.trim()),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['business-reviews'] });
      // Not «a Mariana O.»: the name already ends in a period.
      toast.success('Publicamos tu respuesta.');
      onClose();
    },
  });

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (text.trim() !== '' && !save.isPending) save.mutate();
      }}
    >
      <Modal.Body>
        <ReviewContent review={{ ...review, reply: null }} replyLabel="" />
        <label className="form-label small mb-1 mt-3" htmlFor={`${ids}-reply`}>Tu respuesta</label>
        <textarea
          id={`${ids}-reply`}
          className="form-control"
          rows={4}
          required
          maxLength={REVIEW_TEXT_MAX_LENGTH}
          value={text}
          onChange={(event) => setText(event.target.value)}
          aria-describedby={`${ids}-reply-help`}
        />
        <div id={`${ids}-reply-help`} className="d-flex justify-content-between gap-2 small text-muted mt-1">
          <span>Se publica en tu tienda bajo la reseña. Sin enlaces, correos ni teléfonos.</span>
          <span className="ta-money text-nowrap">{text.length}/{REVIEW_TEXT_MAX_LENGTH}</span>
        </div>
        {save.isError && <div className="alert alert-danger mt-3 mb-0" role="alert">{getApiErrorMessage(save.error, 'No se pudo publicar tu respuesta.')}</div>}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cancelar</button>
        <button type="submit" className="btn btn-primary" disabled={text.trim() === '' || save.isPending}>
          {save.isPending ? 'Publicando…' : 'Publicar respuesta'}
        </button>
      </Modal.Footer>
    </form>
  );
}

/**
 * The reviews of the business (Fase 24H, ADR 0030): its rating, the ones waiting for an answer and the ones it reported.
 * The business answers each review once (the answer can change or go), and reports one that breaks the rules with a
 * reason from the list: the review stays published until the platform decides. It never deletes what a customer said.
 */
export default function BusinessReviewsPage() {
  const { confirm } = useConfirm();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<BusinessReviewsFilter>(BusinessReviewsFilter.All);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0]);
  const [replying, setReplying] = useState<BusinessReview | null>(null);
  const [reporting, setReporting] = useState<BusinessReview | null>(null);
  const reviews = useQuery({
    queryKey: ['business-reviews', filter, page, pageSize],
    queryFn: ({ signal }) => reviewsApi.getBusinessReviews(filter, page, pageSize, signal),
    placeholderData: keepPreviousData,
  });
  const data = reviews.data;
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['business-reviews'] });
  // A filter whose last row of a page went (answered, reported) goes back to the page before.
  if (data && !reviews.isPlaceholderData && data.reviews.items.length === 0 && page > 1) setPage(page - 1);

  const removeReply = async (review: BusinessReview) => {
    const accepted = await confirm({
      title: 'Quitar respuesta',
      message: `Tu respuesta a ${review.authorName} deja de verse en tu tienda. La reseña sigue publicada.`,
      confirmLabel: 'Quitar respuesta',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    try {
      await reviewsApi.removeReply(review.id);
      toast.success('Quitaste tu respuesta.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo quitar tu respuesta.'));
    } finally {
      void refresh();
    }
  };

  const summary = data?.summary;
  const cards = data ? [
    {
      label: 'Calificación',
      value: summary?.average != null ? formatAverage(summary.average) : '—',
      hint: summary?.average != null
        ? reviewCountLabel(summary.count)
        : `${reviewCountLabel(summary?.count ?? 0)}: aparece desde 3 reseñas`,
      stars: summary?.average ?? null,
    },
    { label: 'Sin responder', value: String(data.unanswered), hint: 'Responder ayuda a que te elijan' },
    { label: 'Reportadas', value: String(data.reported), hint: 'La plataforma las revisa' },
  ] : [];

  return (
    <>
      <PageTitle title="Reseñas" />

      {cards.length > 0 && (
        <div className="row g-2 mb-3">
          {cards.map((card) => (
            <div key={card.label} className="col-12 col-sm-4">
              <div className="card h-100 mb-0">
                <div className="card-body py-2">
                  <div className="text-muted small">{card.label}</div>
                  <div className="h4 mb-0 d-flex align-items-center gap-2">
                    <span className="ta-money">{card.value}</span>
                    {'stars' in card && card.stars != null && <Stars rating={card.stars} className="fs-6" decorative />}
                  </div>
                  <div className="text-muted small">{card.hint}</div>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="card">
        <div className="card-body">
          {reviews.isError && <div className="alert alert-danger">{getApiErrorMessage(reviews.error, 'No se pudieron cargar las reseñas.')}</div>}
          <DataTable
            data={data?.reviews.items ?? []}
            columns={COLUMNS}
            keyExtractor={(review) => review.id}
            loading={reviews.isPending}
            searchPlaceholder="Buscar en esta página"
            searchFilter={(review, search) =>
              review.authorName.toLowerCase().includes(search) || (review.comment ?? '').toLowerCase().includes(search)}
            emptyMessage={filter === BusinessReviewsFilter.All
              ? 'Todavía no tienes reseñas. Tus clientes califican desde sus pedidos y citas.'
              : 'No hay reseñas con este filtro.'}
            pageSizeOptions={PAGE_SIZES}
            serverSide={{
              totalCount: data?.reviews.totalCount ?? 0,
              page,
              pageSize,
              onPageChange: setPage,
              onPageSizeChange: (size) => { setPageSize(size); setPage(1); },
            }}
            filters={(
              <DataTableFilter label="Mostrar" htmlFor="reviews-filter">
                <select
                  id="reviews-filter"
                  className="form-select form-select-sm"
                  value={filter}
                  onChange={(event) => { setFilter(Number(event.target.value) as BusinessReviewsFilter); setPage(1); }}
                >
                  {FILTERS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
              </DataTableFilter>
            )}
            renderRow={(review) => {
              const published = review.status === ReviewStatus.Published;
              return (
                <>
                  <td><ReviewContent review={review} replyLabel="Tu respuesta" /></td>
                  <td><ReviewStatusChip review={review} /></td>
                  <td className="text-center">
                    {published && (
                      <button
                        type="button"
                        className="btn btn-sm btn-light me-1"
                        onClick={() => setReplying(review)}
                        aria-label={`${review.reply ? 'Cambiar respuesta' : 'Responder'} a ${review.authorName}`}
                      >
                        {review.reply ? 'Cambiar respuesta' : 'Responder'}
                      </button>
                    )}
                    {review.reply && (
                      <button
                        type="button"
                        className="btn btn-sm btn-light me-1"
                        onClick={() => void removeReply(review)}
                        aria-label={`Quitar respuesta a ${review.authorName}`}
                      >
                        Quitar respuesta
                      </button>
                    )}
                    {canReportReview(review) && (
                      <button
                        type="button"
                        className="btn btn-sm btn-light text-danger"
                        onClick={() => setReporting(review)}
                        aria-label={`Reportar la reseña de ${review.authorName}`}
                      >
                        Reportar
                      </button>
                    )}
                  </td>
                </>
              );
            }}
          />
        </div>
      </div>

      <ReplyDialog review={replying} onClose={() => setReplying(null)} />
      <ConcernDialog
        title={reporting ? `Reportar la reseña de ${reporting.authorName}` : null}
        description="La plataforma la revisa y decide si la oculta. Mientras, sigue publicada."
        confirmLabel="Reportar"
        onSubmit={async (reason) => {
          if (!reporting) return;
          await reviewsApi.report(reporting.id, reason);
          await refresh();
          toast.success('Reportaste la reseña: la plataforma la va a revisar.');
        }}
        onClose={() => setReporting(null)}
      />
    </>
  );
}
