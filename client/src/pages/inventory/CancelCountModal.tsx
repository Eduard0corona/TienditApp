import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useCancelInventoryCount } from '../../api/inventoryCounts';

interface CancelCountModalProps {
  countId: string;
  countNumber: string;
  onHide: () => void;
  onCancelled: () => void;
}

/** Closes an open count without moving anything; what was counted stays in it. */
export default function CancelCountModal({ countId, countNumber, onHide, onCancelled }: CancelCountModalProps) {
  const titleId = useId();
  const cancel = useCancelInventoryCount(countId);
  const [reason, setReason] = useState('');
  const [error, setError] = useState('');

  const submit = async () => {
    setError('');
    try {
      await cancel.mutateAsync(reason.trim());
      onCancelled();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo cancelar el conteo.'));
    }
  };

  return (
    <Modal show onHide={onHide} centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Cancelar el conteo {countNumber}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        <p>Las existencias no cambian. Lo que se contó se queda en el conteo.</p>
        <label htmlFor="cancelar-conteo-motivo" className="form-label">Motivo (opcional)</label>
        <textarea
          id="cancelar-conteo-motivo"
          className="form-control"
          rows={2}
          maxLength={300}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>Volver</button>
        <button type="button" className="btn btn-danger" onClick={submit} disabled={cancel.isPending}>
          {cancel.isPending ? 'Cancelando…' : 'Cancelar conteo'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
