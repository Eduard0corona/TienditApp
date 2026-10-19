import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useMutation } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import type { ReviewConcern } from '../../api/reviewsApi';
import { REVIEW_CONCERN_LABELS, REVIEW_CONCERNS } from '../../utils/reviews';

interface ConcernDialogProps {
  /** What the dialog is for, as its title says it: «Reportar la reseña de Mariana O.». Null keeps it closed. */
  title: string | null;
  description: string;
  confirmLabel: string;
  /** The reason already chosen (the one of its report, when the platform hides it). */
  initial?: ReviewConcern | null;
  onSubmit: (reason: ReviewConcern) => Promise<unknown>;
  onClose: () => void;
}

function ConcernForm({ description, confirmLabel, initial, onSubmit, onClose }: Omit<ConcernDialogProps, 'title'>) {
  const ids = useId();
  const [reason, setReason] = useState<ReviewConcern | null>(initial ?? null);
  const submit = useMutation({
    mutationFn: (chosen: ReviewConcern) => onSubmit(chosen),
    onSuccess: onClose,
  });

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (reason !== null && !submit.isPending) submit.mutate(reason);
      }}
    >
      <Modal.Body>
        <p>{description}</p>
        <fieldset>
          <legend className="form-label small mb-1">Motivo</legend>
          {REVIEW_CONCERNS.map((concern) => (
            <div key={concern} className="form-check">
              <input
                id={`${ids}-${concern}`}
                className="form-check-input"
                type="radio"
                name={`${ids}-reason`}
                checked={reason === concern}
                onChange={() => setReason(concern)}
              />
              <label className="form-check-label" htmlFor={`${ids}-${concern}`}>{REVIEW_CONCERN_LABELS[concern]}</label>
            </div>
          ))}
        </fieldset>
        {submit.isError && (
          <div className="alert alert-danger mt-3 mb-0" role="alert">{getApiErrorMessage(submit.error, 'No se pudo guardar. Intenta de nuevo.')}</div>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cancelar</button>
        <button type="submit" className="btn btn-primary" disabled={reason === null || submit.isPending}>
          {submit.isPending ? 'Guardando…' : confirmLabel}
        </button>
      </Modal.Footer>
    </form>
  );
}

/**
 * The reason from the list (Fase 24H): why a business reports a review, or why the platform hides it. A list and not a
 * text, so nobody writes about the customer.
 */
export default function ConcernDialog({ title, ...form }: ConcernDialogProps) {
  const titleId = useId();
  // While it fades out it keeps its title and its form.
  const [shown, setShown] = useState(title);
  if (title !== null && title !== shown) setShown(title);
  const current = title ?? shown;
  return (
    <Modal show={title !== null} onHide={form.onClose} centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId} as="h2" className="h5">{current}</Modal.Title>
      </Modal.Header>
      {/* A new key for another review starts from its own reason. */}
      {current !== null && <ConcernForm key={current} {...form} />}
    </Modal>
  );
}
