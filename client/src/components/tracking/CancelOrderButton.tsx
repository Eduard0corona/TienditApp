import { useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';

interface CancelOrderButtonProps {
  orderNumber: string;
  /** Cancels it and shows the order as the server answers it. */
  cancel: () => Promise<void>;
  /** The server refused it (the business accepted it meanwhile): the page reads the order again. */
  onRefused: () => void;
}

/**
 * The customer cancels their online order while the business has not accepted it (Fase 29R), from its link or their
 * account. The page shows it only while the server says it may; once accepted, the customer calls the business.
 */
export default function CancelOrderButton({ orderNumber, cancel, onRefused }: CancelOrderButtonProps) {
  const { confirm } = useConfirm();
  const toast = useToast();
  const [cancelling, setCancelling] = useState(false);

  const onCancel = async () => {
    const accepted = await confirm({
      title: 'Cancelar pedido',
      message: `El negocio todavía no acepta tu pedido ${orderNumber}. Si lo cancelas no lo prepara, y para recibirlo tendrías que pedirlo otra vez.`,
      confirmLabel: 'Cancelar pedido',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    setCancelling(true);
    try {
      await cancel();
      toast.success(`Cancelaste tu pedido ${orderNumber}.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cancelar tu pedido.'));
      onRefused();
    } finally {
      setCancelling(false);
    }
  };

  return (
    <div className="mt-3">
      <button type="button" className="btn btn-outline-danger btn-sm" onClick={() => void onCancel()} disabled={cancelling}>
        {cancelling
          ? <span className="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>
          : <i className="ri-close-circle-line me-1" aria-hidden="true"></i>}
        Cancelar pedido
      </button>
      <p className="small text-muted mt-1 mb-0">Puedes cancelarlo mientras el negocio no lo acepte.</p>
    </div>
  );
}
