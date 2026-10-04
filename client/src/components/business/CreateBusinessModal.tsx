import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { authApi } from '../../api/authApi';
import { getApiErrorMessage } from '../../api/errors';
import { useAuth } from '../../hooks/useAuth';
import { BusinessType } from '../../types';

interface CreateBusinessModalProps {
  show: boolean;
  onHide: () => void;
}

/**
 * Opens another business with the same account. The person becomes its owner and switches to it, where the
 * guided setup starts (modules, IVA, sample catalog).
 */
export default function CreateBusinessModal({ show, onHide }: CreateBusinessModalProps) {
  const { switchTenant } = useAuth();
  const [name, setName] = useState('');
  const [businessType, setBusinessType] = useState<BusinessType>(BusinessType.Retail);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const close = () => {
    if (submitting) return;
    setError('');
    onHide();
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim()) {
      setError('Escribe el nombre del negocio.');
      return;
    }
    setSubmitting(true);
    setError('');
    try {
      const { data } = await authApi.createBusiness({ businessName: name.trim(), businessType });
      // Switching reloads the app in the new business, which opens its guided setup.
      await switchTenant(data.tenantId);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo abrir el negocio.'));
      setSubmitting(false);
    }
  };

  return (
    <Modal show={show} onHide={close} centered>
      <form onSubmit={(event) => void submit(event)}>
        <Modal.Header closeButton>
          <Modal.Title as="h2" className="h5">Abrir otro negocio</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="text-muted small">
            Con tu misma cuenta serás dueño del negocio nuevo. Sus ventas, catálogo y equipo quedan separados de los
            de tus otros negocios.
          </p>
          <div className="mb-3">
            <label className="form-label" htmlFor="new-business-name">Nombre del negocio</label>
            <input
              id="new-business-name"
              className="form-control"
              maxLength={200}
              value={name}
              onChange={(event) => setName(event.target.value)}
              disabled={submitting}
              autoFocus
            />
          </div>
          <div>
            <label className="form-label" htmlFor="new-business-type">Tipo de negocio</label>
            <select
              id="new-business-type"
              className="form-select"
              value={businessType}
              onChange={(event) => setBusinessType(Number(event.target.value) as BusinessType)}
              disabled={submitting}
            >
              <option value={BusinessType.Food}>Alimentos</option>
              <option value={BusinessType.Retail}>Comercio</option>
              <option value={BusinessType.Service}>Servicios</option>
            </select>
          </div>
          {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={close} disabled={submitting}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={submitting}>
            {submitting ? 'Abriendo…' : 'Abrir negocio'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
