import { QuoteTime, ResponseTime } from '../api/generated/enums';

// What an owner reads of their own times in «Cómo te ven en TienditApp». Apart from utils/responseTime, which the cards of
// Explorar (the first visit) load: these notes come with Configuración only.

/**
 * What its owner reads under the preview of its card while Explorar counts its requests online (Fase 29K, ADR 0065):
 * where its customers read how soon it answers, or why nobody reads it yet. Since Fase 29M (ADR 0067) a customer counts once,
 * by their last request, and those of its own team do not count.
 */
export function ownResponseTimeNote(responseTime: ResponseTime | null | undefined): string {
  if (responseTime == null)
    return 'Todavía no contamos solicitudes en línea de 3 clientes para saber en cuánto sueles responder: una cuenta cuando la respondes, o cuando llega su hora o pasa un día sin respuesta.';
  if (responseTime === ResponseTime.Slower)
    return 'Sueles tardar un día o más en responder tus solicitudes en línea, así que tus clientes no lo leen.';
  return 'Lo calculamos con tus solicitudes en línea de los últimos 90 días, la última de cada cliente y sin las de tu equipo. Tus clientes lo leen en tu tarjeta, en tu tienda y donde te piden citas o visitas.';
}

/** What its owner reads of how soon it quotes, while Explorar counts its requests and it receives visits (Fase 29S). */
export function ownQuoteTimeNote(quoteTime: QuoteTime | null | undefined): string {
  if (quoteTime == null)
    return 'Todavía no contamos visitas en línea de 3 clientes con su cotización mandada para saber en cuánto sueles cotizar.';
  if (quoteTime === QuoteTime.Slower)
    return 'Sueles tardar una semana o más en mandar la cotización después de la visita, así que tus clientes no lo leen.';
  return 'Lo contamos desde que termina cada visita en línea hasta que mandas su cotización, en los últimos 90 días, la última de cada cliente. Tus clientes lo leen en tu tarjeta y donde te piden la visita.';
}
