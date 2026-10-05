import type { MouseEventHandler } from 'react';
import { buildWhatsAppTicketUrl } from '../../utils/ticket';

interface WhatsAppLinkProps {
  /** Message WhatsApp opens with; the person still reviews it and taps send. */
  text: string;
  /** Chat to open; without one WhatsApp asks who to send it to. */
  phone?: string | null;
  label?: string;
  className?: string;
  disabled?: boolean;
  onClick?: MouseEventHandler<HTMLAnchorElement>;
}

/**
 * Opens WhatsApp (app or web) with a message ready to send. It is a link and not window.open, so the
 * browser never blocks it as a popup.
 */
export default function WhatsAppLink({
  text,
  phone,
  label = 'Enviar por WhatsApp',
  className = 'btn btn-light',
  disabled = false,
  onClick,
}: WhatsAppLinkProps) {
  return (
    <a
      className={`${className}${disabled ? ' disabled' : ''}`}
      href={buildWhatsAppTicketUrl(text, phone ?? undefined)}
      target="_blank"
      rel="noopener noreferrer"
      aria-disabled={disabled || undefined}
      tabIndex={disabled ? -1 : undefined}
      onClick={onClick}
    >
      <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>{label}
    </a>
  );
}
