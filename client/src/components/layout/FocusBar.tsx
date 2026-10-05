import { useEffect, useState } from 'react';
import type { FocusArea } from './focusMode';

const TITLES: Record<FocusArea, string> = { sell: 'Vender', kitchen: 'Cocina' };

interface FocusBarProps {
  area: FocusArea;
  businessName?: string;
  onExit: () => void;
}

const clock = (date: Date) => date.toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });

/** All that stays of the shell in focus mode: where you are, the time, and the way out. */
export default function FocusBar({ area, businessName, onExit }: FocusBarProps) {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30_000);
    return () => window.clearInterval(timer);
  }, []);

  return (
    <header className="focus-bar">
      <div className="focus-bar-title">
        <strong>{TITLES[area]}</strong>
        {businessName && <span className="text-muted ms-2">{businessName}</span>}
      </div>
      <div className="d-flex align-items-center gap-3">
        <time className="focus-bar-clock" dateTime={now.toISOString()}>{clock(now)}</time>
        <button type="button" className="btn btn-light" onClick={onExit}>
          <i className="ri-fullscreen-exit-line me-1" aria-hidden="true"></i>Salir del modo enfocado
        </button>
      </div>
    </header>
  );
}
