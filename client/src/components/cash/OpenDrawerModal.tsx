import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { cashApi } from '../../api/cashApi';
import { getApiErrorMessage } from '../../api/errors';
import { useRefreshCashState } from '../../api/queries';
import type { Branch } from '../../api/branchesApi';
import { parseCashAmount } from '../../pages/cash/cashFormat';
import type { CashSessionDto } from '../../types';

interface OpenDrawerModalProps {
  show: boolean;
  onHide: () => void;
  branches: Branch[];
  /** Branch this device works at: the drawer opens there unless another one is chosen. */
  defaultBranchId?: string;
  onOpened: (session: CashSessionDto) => void;
}

/** Opens the cashier's drawer without leaving the screen (Vender): the opening float and, with several branches, where. */
export default function OpenDrawerModal({ show, onHide, branches, defaultBranchId, onOpened }: OpenDrawerModalProps) {
  const refreshCashState = useRefreshCashState();
  const [openingAmount, setOpeningAmount] = useState('');
  const [notes, setNotes] = useState('');
  const [branchId, setBranchId] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const chosenBranchId = branchId || defaultBranchId || '';

  const close = () => {
    if (submitting) return;
    setError('');
    onHide();
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    const amount = openingAmount.trim() === '' ? 0 : parseCashAmount(openingAmount);
    if (amount === null) {
      setError('Escribe el fondo inicial, por ejemplo 500 o 350.50.');
      return;
    }
    setSubmitting(true);
    setError('');
    try {
      const { data } = await cashApi.openSession({
        openingAmount: amount,
        notes: notes.trim() || undefined,
        branchId: chosenBranchId || undefined,
      });
      setOpeningAmount('');
      setNotes('');
      refreshCashState();
      onOpened(data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo abrir la caja.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal show={show} onHide={close} centered>
      <form onSubmit={(event) => void submit(event)}>
        <Modal.Header closeButton>
          <Modal.Title as="h2" className="h5">Abrir mi caja</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="text-muted small">El efectivo que cobres desde ahora entra a tu corte.</p>
          <div className="mb-3">
            <label className="form-label" htmlFor="sell-drawer-opening">Fondo inicial</label>
            <input
              id="sell-drawer-opening"
              className="form-control form-control-lg ta-money"
              inputMode="decimal"
              placeholder="$0.00"
              value={openingAmount}
              onChange={(event) => setOpeningAmount(event.target.value)}
              disabled={submitting}
              autoFocus
            />
          </div>
          {branches.length > 1 && (
            <div className="mb-3">
              <label className="form-label" htmlFor="sell-drawer-branch">Sucursal</label>
              <select
                id="sell-drawer-branch"
                className="form-select"
                value={chosenBranchId}
                onChange={(event) => setBranchId(event.target.value)}
                disabled={submitting}
              >
                {branches.map((branch) => (
                  <option key={branch.id} value={branch.id}>{branch.name}{branch.isDefault ? ' (principal)' : ''}</option>
                ))}
              </select>
            </div>
          )}
          <div>
            <label className="form-label" htmlFor="sell-drawer-notes">Notas (opcional)</label>
            <input
              id="sell-drawer-notes"
              className="form-control"
              maxLength={2000}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              disabled={submitting}
            />
          </div>
          {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={close} disabled={submitting}>Cancelar</button>
          <button type="submit" className="btn btn-primary btn-touch" disabled={submitting}>
            {submitting ? 'Abriendo…' : 'Abrir caja'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
