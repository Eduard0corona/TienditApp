import { formatInBusinessZone } from './dates';

/**
 * By when the business answers an order of its online menu still «Por confirmar» not to count among the slowest in Explorar
 * (Fase 29X, ADR 0078), in its own time zone. The server works out the limit (`confirmBy`: half an hour after it arrived),
 * so it is minutes away and only its time is said. Pedidos loads it, never the first visit.
 */
export function confirmInTimeLabel(confirmBy: string, now: Date = new Date()): { text: string; late: boolean } {
  const limit = new Date(confirmBy);
  if (limit.getTime() <= now.getTime())
    return { text: 'Ya pasó la media hora: cuenta entre los más lentos en Explorar', late: true };
  const time = formatInBusinessZone(limit, { hour: 'numeric', minute: '2-digit' });
  // "de las 3:30", but "de la 1:30" in the one o'clock hour.
  return { text: `Confírmalo antes de ${time.startsWith('1:') ? 'la' : 'las'} ${time} para que no cuente entre los más lentos`, late: false };
}
