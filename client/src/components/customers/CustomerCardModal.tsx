import { Modal } from 'react-bootstrap';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { buildSellLink } from '../../utils/sellLink';
import CustomerCardPanel from './CustomerCardPanel';

interface CustomerCardModalProps {
  phone?: string;
  customerId?: string;
  name?: string;
  onClose: () => void;
}

export default function CustomerCardModal({ phone, customerId, name, onClose }: CustomerCardModalProps) {
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const navigate = useNavigate();
  const canSell = getAccess(user, capabilities).sell;

  return (
    <Modal show onHide={onClose} centered fullscreen="sm-down">
      <Modal.Header closeButton>
        <Modal.Title as="h2" className="h5">Ficha del cliente</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <CustomerCardPanel
          phone={phone}
          customerId={customerId}
          fallbackName={name}
          onNewOrder={canSell
            ? (card) => navigate(buildSellLink({ phone: card.phone, name: card.name ?? name }))
            : undefined}
        />
      </Modal.Body>
    </Modal>
  );
}
