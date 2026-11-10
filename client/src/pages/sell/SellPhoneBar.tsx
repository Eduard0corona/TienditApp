import { useId, type ReactNode } from 'react';
import { Offcanvas } from 'react-bootstrap';
import { formatMoney } from '../../utils/money';

interface SellPhoneBarProps {
  articles: number;
  total: number;
  /** «Enviar», «Registrar», «Cobrar» or «Guardar», as the ticket's main action. */
  actionLabel: string;
  onAction: () => void;
  actionDisabled: boolean;
  ticketOpen: boolean;
  onOpenTicket: () => void;
  onCloseTicket: () => void;
  /** The ticket, in its bottom sheet. */
  children: ReactNode;
}

/** Phones: the ticket lives in a bottom sheet behind this bar, with the ticket's main action beside it. */
export default function SellPhoneBar(props: SellPhoneBarProps) {
  const ticketTitleId = useId();
  const { articles, total } = props;
  return (
    <>
      <div className="sell-phone-bar d-lg-none">
        <button type="button" className="btn btn-light btn-touch flex-grow-1" onClick={props.onOpenTicket}>
          <i className="ri-shopping-basket-2-line me-1" aria-hidden="true"></i>
          {articles} {articles === 1 ? 'artículo' : 'artículos'} · <span className="ta-money">{formatMoney(total)}</span>
        </button>
        <button type="button" className="btn btn-primary btn-touch" onClick={props.onAction} disabled={props.actionDisabled}>
          {props.actionLabel}
        </button>
      </div>
      <Offcanvas show={props.ticketOpen} onHide={props.onCloseTicket} placement="bottom" className="sell-sheet" aria-labelledby={ticketTitleId}>
        <Offcanvas.Header closeButton>
          <Offcanvas.Title id={ticketTitleId} as="span" className="visually-hidden">Ticket</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>{props.children}</Offcanvas.Body>
      </Offcanvas>
    </>
  );
}
