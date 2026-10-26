import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useUpdateCustomerFiscalData } from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import type { CustomerFiscalData } from '../../types';
import { EMPTY_RECEIVER, isPlainEmail, normalizeReceiver } from '../../utils/cfdi';
import CfdiReceiverFields from '../invoicing/CfdiReceiverFields';

interface CustomerFiscalDataModalProps {
  customer: { id: string; name: string; fiscalData?: CustomerFiscalData | null; invoiceEmail?: string | null };
  onClose: (changed: boolean) => void;
}

/**
 * The fiscal data a customer's invoices go to (Fase 28A), and the email their XML and PDF go to (Fase 28E): the invoice of
 * each of their orders proposes them.
 */
export default function CustomerFiscalDataModal({ customer, onClose }: CustomerFiscalDataModalProps) {
  const titleId = useId();
  const toast = useToast();
  const update = useUpdateCustomerFiscalData(customer.id);
  const [form, setForm] = useState<CustomerFiscalData>(customer.fiscalData ?? EMPTY_RECEIVER);
  const [email, setEmail] = useState(customer.invoiceEmail ?? '');
  const typedEmail = email.trim();
  const emailValid = !typedEmail || isPlainEmail(typedEmail);

  const save = async (fiscalData: CustomerFiscalData | null) => {
    try {
      await update.mutateAsync({ fiscalData, invoiceEmail: typedEmail || null });
      toast.success(fiscalData ? `Datos fiscales de ${customer.name} guardados.` : `Datos fiscales de ${customer.name} quitados.`);
      onClose(true);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudieron guardar los datos fiscales.'));
    }
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!emailValid) return;
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
          <div className="mt-2">
            <label className="form-label small mb-1" htmlFor="customer-fiscal-email">Correo para facturas (opcional)</label>
            <input
              id="customer-fiscal-email"
              type="email"
              className={`form-control${emailValid ? '' : ' is-invalid'}`}
              maxLength={254}
              autoComplete="off"
              disabled={update.isPending}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              aria-describedby="customer-fiscal-email-help"
            />
            <div id="customer-fiscal-email-help" className={emailValid ? 'form-text' : 'invalid-feedback'}>
              {emailValid
                ? 'Ahí le llegan el XML y el PDF de sus facturas. La factura de sus pedidos lo propone.'
                : 'Escribe un correo válido, como nombre@ejemplo.com, o déjalo vacío.'}
            </div>
          </div>
        </Modal.Body>
        <Modal.Footer>
          {customer.fiscalData && (
            <button type="button" className="btn btn-outline-danger me-auto" disabled={update.isPending} onClick={() => void save(null)}>
              Quitar datos fiscales
            </button>
          )}
          <button type="button" className="btn btn-light" onClick={() => onClose(false)}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={update.isPending || !emailValid}>
            {update.isPending ? 'Guardando…' : 'Guardar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
