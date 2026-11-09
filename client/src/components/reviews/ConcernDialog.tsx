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

interface ConcernFormProps {
  description: string;
  confirmLabel: string;
  initial?: ReviewConcern | null;
  /** False while it fades out: what it sent was answered, so it sends nothing else. */
  open: boolean;
  saving: boolean;
  error: unknown;
  onSave: (reason: ReviewConcern) => void;
  onCancel: () => void;
}

function ConcernForm({ description, confirmLabel, initial, open, saving, error, onSave, onCancel }: ConcernFormProps) {
  const ids = useId();
  const [reason, setReason] = useState<ReviewConcern | null>(initial ?? null);

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        if (reason !== null && !saving) onSave(reason);
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
        {error != null && (
          <div className="alert alert-danger mt-3 mb-0" role="alert">{getApiErrorMessage(error, 'No se pudo guardar. Intenta de nuevo.')}</div>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onCancel} disabled={saving}>Cancelar</button>
        <button type="submit" className="btn btn-primary" disabled={reason === null || saving || !open}>
          {saving ? 'Guardando…' : confirmLabel}
        </button>
      </Modal.Footer>
    </form>
  );
}

/**
 * The reason from the list (Fase 24H): why a business reports a review, or why the platform hides it. A list and not a
 * text, so nobody writes about the customer.
 */
export default function ConcernDialog({ title, onSubmit, onClose, ...form }: ConcernDialogProps) {
  const titleId = useId();
  // While it fades out it keeps its title and its form.
  const [shown, setShown] = useState(title);
  if (title !== null && title !== shown) setShown(title);
  const current = title ?? shown;
  // Here and not in the form: the dialog does not close while what it sent is on its way, so the answer (and its error)
  // reaches it and not one opened after it.
  const submit = useMutation({
    mutationFn: (reason: ReviewConcern) => onSubmit(reason),
    onSuccess: onClose,
  });
  const close = () => {
    if (submit.isPending) return;
    // The next one starts without the error of this one.
    submit.reset();
    onClose();
  };

  return (
    <Modal show={title !== null} onHide={close} centered aria-labelledby={titleId}>
      <Modal.Header closeButton={!submit.isPending}>
        <Modal.Title id={titleId} as="h2" className="h5">{current}</Modal.Title>
      </Modal.Header>
      {/* A new key for another review starts from its own reason. */}
      {current !== null && (
        <ConcernForm
          key={current}
          {...form}
          open={title !== null}
          saving={submit.isPending}
          error={submit.error}
          onSave={(reason) => submit.mutate(reason)}
          onCancel={close}
        />
      )}
    </Modal>
  );
}
