import { useState } from 'react';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { PlatformReviewsFilter, ReviewStatus } from '../../api/generated/enums';
import { reviewsApi, type PlatformReview } from '../../api/reviewsApi';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import ConcernDialog from '../../components/reviews/ConcernDialog';
import { ReviewContent } from '../../components/reviews/ReviewContent';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { formatDate } from '../../utils/dates';
import { REVIEW_CONCERN_LABELS } from '../../utils/reviews';

const COLUMNS = [
  { header: 'Negocio' },
  { header: 'Reseña' },
  { header: 'Motivo' },
  { header: 'Acciones', className: 'text-center' },
];

const PAGE_SIZES = [20, 50];

/**
 * The platform's reviews (Fase 24H, ADR 0030): the ones their businesses reported, the oldest report first, and the hidden
 * ones. The platform hides one with a reason from the list, keeps it (the report goes, the review stays) or restores one
 * it hid. Each decision stays in the activity of the business; the customer sees why theirs was hidden.
 */
export default function PlatformReviewsPage() {
  const { confirm } = useConfirm();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<PlatformReviewsFilter>(PlatformReviewsFilter.Reported);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0]);
  const [hiding, setHiding] = useState<PlatformReview | null>(null);
  const reviews = useQuery({
    queryKey: ['platform', 'reviews', filter, page, pageSize],
    queryFn: ({ signal }) => reviewsApi.getPlatformReviews(filter, page, pageSize, signal),
    placeholderData: keepPreviousData,
  });
  const data = reviews.data;
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['platform', 'reviews'] });

  const decide = async (review: PlatformReview, keep: boolean) => {
    const accepted = await confirm(keep
      ? {
        title: 'Mantener reseña',
        message: `La reseña de ${review.authorName} en ${review.businessName} sigue publicada y su reporte se cierra.`,
        confirmLabel: 'Mantener',
        cancelLabel: 'Volver',
      }
      : {
        title: 'Restaurar reseña',
        message: `La reseña de ${review.authorName} vuelve a verse en la tienda de ${review.businessName}.`,
        confirmLabel: 'Restaurar',
        cancelLabel: 'Volver',
      });
    if (!accepted) return;
    try {
      await (keep ? reviewsApi.keep(review.id) : reviewsApi.restore(review.id));
      toast.success(keep ? 'La reseña sigue publicada.' : 'La reseña volvió a publicarse.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar la decisión.'));
    } finally {
      void refresh();
    }
  };

  const reported = filter === PlatformReviewsFilter.Reported;

  return (
    <>
      <PageTitle title="Reseñas" />

      <div className="card">
        <div className="card-body">
          {reviews.isError && <div className="alert alert-danger">{getApiErrorMessage(reviews.error, 'No se pudieron cargar las reseñas.')}</div>}
          <DataTable
            data={data?.items ?? []}
            columns={COLUMNS}
            keyExtractor={(review) => review.id}
            loading={reviews.isPending}
            searchPlaceholder="Buscar en esta página"
            searchFilter={(review, search) => [review.businessName, review.authorName, review.comment ?? '']
              .some((text) => text.toLowerCase().includes(search))}
            emptyMessage={reported ? 'No hay reseñas reportadas por revisar.' : 'No hay reseñas ocultas.'}
            pageSizeOptions={PAGE_SIZES}
            serverSide={{
              totalCount: data?.totalCount ?? 0,
              page,
              pageSize,
              onPageChange: setPage,
              onPageSizeChange: (size) => { setPageSize(size); setPage(1); },
            }}
            filters={(
              <DataTableFilter label="Mostrar" htmlFor="platform-reviews-filter">
                <select
                  id="platform-reviews-filter"
                  className="form-select form-select-sm"
                  value={filter}
                  onChange={(event) => { setFilter(Number(event.target.value) as PlatformReviewsFilter); setPage(1); }}
                >
                  <option value={PlatformReviewsFilter.Reported}>Reportadas</option>
                  <option value={PlatformReviewsFilter.Hidden}>Ocultas</option>
                </select>
              </DataTableFilter>
            )}
            renderRow={(review) => {
              // Each row by its own state: while another filter loads, the rows of the one before stay.
              const waiting = review.status === ReviewStatus.Published;
              return (
                <>
                  <td>
                    <a href={`/m/${review.businessSlug}`} target="_blank" rel="noopener noreferrer">{review.businessName}</a>
                  </td>
                  <td><ReviewContent review={review} replyLabel={`Respuesta de ${review.businessName}`} /></td>
                  <td>
                    {waiting ? (
                      <>
                        {review.reportReason != null && <div>{REVIEW_CONCERN_LABELS[review.reportReason]}</div>}
                        <div className="small text-muted">Reportada el {formatDate(review.reportedAt)}</div>
                      </>
                    ) : (
                      <>
                        {review.hiddenReason != null && <div>{REVIEW_CONCERN_LABELS[review.hiddenReason]}</div>}
                        <div className="small text-muted">Oculta el {formatDate(review.moderatedAt)}</div>
                      </>
                    )}
                  </td>
                  <td className="text-center">
                    {waiting ? (
                      <>
                        <button
                          type="button"
                          className="btn btn-sm btn-light text-danger me-1"
                          onClick={() => setHiding(review)}
                          aria-label={`Ocultar la reseña de ${review.authorName} en ${review.businessName}`}
                        >
                          Ocultar
                        </button>
                        <button
                          type="button"
                          className="btn btn-sm btn-light"
                          onClick={() => void decide(review, true)}
                          aria-label={`Mantener la reseña de ${review.authorName} en ${review.businessName}`}
                        >
                          Mantener
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        className="btn btn-sm btn-light"
                        onClick={() => void decide(review, false)}
                        aria-label={`Restaurar la reseña de ${review.authorName} en ${review.businessName}`}
                      >
                        Restaurar
                      </button>
                    )}
                  </td>
                </>
              );
            }}
          />
        </div>
      </div>

      <ConcernDialog
        title={hiding ? `Ocultar la reseña de ${hiding.authorName}` : null}
        description={hiding
          ? `Deja de verse en la tienda de ${hiding.businessName} y en su calificación. Su autor ve el motivo y puede borrarla.`
          : ''}
        confirmLabel="Ocultar"
        initial={hiding?.reportReason ?? null}
        onSubmit={async (reason) => {
          if (!hiding) return;
          await reviewsApi.hide(hiding.id, reason);
          await refresh();
          toast.success('Ocultaste la reseña.');
        }}
        onClose={() => setHiding(null)}
      />
    </>
  );
}
