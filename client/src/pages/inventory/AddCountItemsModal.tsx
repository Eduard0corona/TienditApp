import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useAddInventoryCountItems } from '../../api/inventoryCounts';
import type { SupplyItemDto } from '../../types';
import SupplyPicker from './SupplyPicker';

interface AddCountItemsModalProps {
  countId: string;
  supplyOptions: SupplyItemDto[];
  onHide: () => void;
  onAdded: (count: number) => void;
}

/** What turned up on the shelf and the count does not have; what it has already stays as it is. */
export default function AddCountItemsModal({ countId, supplyOptions, onHide, onAdded }: AddCountItemsModalProps) {
  const titleId = useId();
  const add = useAddInventoryCountItems(countId);
  const [chosen, setChosen] = useState<string[]>([]);
  const [error, setError] = useState('');

  const submit = async () => {
    setError('');
    if (chosen.length === 0) {
      setError('Elige al menos un artículo.');
      return;
    }
    try {
      await add.mutateAsync(chosen);
      onAdded(chosen.length);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudieron agregar los artículos.'));
    }
  };

  return (
    <Modal show onHide={onHide} centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Agregar artículos al conteo</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        <SupplyPicker supplies={supplyOptions} selected={chosen} onChange={setChosen} idPrefix="agregar-conteo-articulo" />
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>Cancelar</button>
        <button type="button" className="btn btn-primary" onClick={submit} disabled={add.isPending}>
          {add.isPending ? 'Agregando…' : 'Agregar'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
