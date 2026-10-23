import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useUpdateCustomerFiscalData } from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import type { CustomerFiscalData } from '../../types';
import { EMPTY_RECEIVER, normalizeReceiver } from '../../utils/cfdi';
import CfdiReceiverFields from '../invoicing/CfdiReceiverFields';

interface CustomerFiscalDataModalProps {
  customer: { id: string; name: string; fiscalData?: CustomerFiscalData | null };
  onClose: (changed: boolean) => void;
}

/** The fiscal data a customer's invoices go to (Fase 28A): the invoice of each of their orders proposes it. */
export default function CustomerFiscalDataModal({ customer, onClose }: CustomerFiscalDataModalProps) {
  const titleId = useId();
  const toast = useToast();
  const update = useUpdateCustomerFiscalData(customer.id);
  const [form, setForm] = useState<CustomerFiscalData>(customer.fiscalData ?? EMPTY_RECEIVER);

  const save = async (fiscalData: CustomerFiscalData | null) => {
    try {
      await update.mutateAsync(fiscalData);
      toast.success(fiscalData ? `Datos fiscales de ${customer.name} guardados.` : `Datos fiscales de ${customer.name} quitados.`);
      onClose(true);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudieron guardar los datos fiscales.'));
    }
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    void save(normalizeReceiver(form));
  };

  return (
    <Modal show onHide={() => onClose(false)} size="lg" centered aria-labelledby={titleId}>
      <form onSubmit={submit}>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Datos fiscales de {customer.name}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small text-muted">
            Como aparecen en su constancia de situación fiscal: el SAT los revisa al timbrar. La factura de sus pedidos los propone.
          </p>
          <CfdiReceiverFields idPrefix="customer-fiscal" value={form} onChange={setForm} disabled={update.isPending} />
        </Modal.Body>
        <Modal.Footer>
          {customer.fiscalData && (
            <button type="button" className="btn btn-outline-danger me-auto" disabled={update.isPending} onClick={() => void save(null)}>
              Quitar datos fiscales
            </button>
          )}
          <button type="button" className="btn btn-light" onClick={() => onClose(false)}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={update.isPending}>
            {update.isPending ? 'Guardando…' : 'Guardar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
