import { useEffect, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { printTicket } from '../../utils/ticket';

interface PrintButtonProps {
  label: string;
  className?: string;
  /** What is printed, as a 72 mm ticket (built when printing, so it carries the current data). */
  render: () => ReactNode;
}

/** Prints a ticket-sized document (cash cut, daily cut) from any screen, without opening another page. */
export default function PrintButton({ label, className = 'btn btn-light', render }: PrintButtonProps) {
  const [printing, setPrinting] = useState(false);

  useEffect(() => {
    if (!printing) return undefined;
    const done = () => setPrinting(false);
    window.addEventListener('afterprint', done, { once: true });
    printTicket();
    return () => window.removeEventListener('afterprint', done);
  }, [printing]);

  return (
    <>
      <button type="button" className={className} onClick={() => setPrinting(true)} disabled={printing}>
        <i className="ri-printer-line me-1" aria-hidden="true"></i>{label}
      </button>
      {printing && createPortal(<div className="ta-print-area ta-print-only ta-print-document">{render()}</div>, document.body)}
    </>
  );
}
