import { OrderConfirmTime, QuoteTime, ResponseTime } from '../api/generated/enums';

// What an owner reads of their own times in «Cómo te ven en TienditApp». Apart from utils/responseTime, which the cards of
// Explorar (the first visit) load: these notes come with Configuración only.

/**
 * What its owner reads under the preview of its card while Explorar counts its requests online (Fase 29K, ADR 0065):
 * where its customers read how soon it answers, or why nobody reads it yet. Since Fase 29M (ADR 0067) a customer counts once,
 * by their last request, and those of its own team do not count; since Fase 29V (ADR 0076), nor those it marked as fake.
 */
export function ownResponseTimeNote(responseTime: ResponseTime | null | undefined): string {
  if (responseTime == null)
    return 'Todavía no contamos solicitudes en línea de 3 clientes para saber en cuánto sueles responder: una cuenta cuando la respondes, o cuando llega su hora o pasa un día sin respuesta.';
  if (responseTime === ResponseTime.Slower)
    return 'Sueles tardar un día o más en responder tus solicitudes en línea, así que tus clientes no lo leen.';
  return 'Lo calculamos con tus solicitudes en línea de los últimos 90 días, la última de cada cliente, sin las de tu equipo ni las que marcaste como falsas. Tus clientes lo leen en tu tarjeta, en tu tienda y donde te piden citas o visitas.';
}

/**
 * What its owner reads of how soon it quotes, while Explorar counts its requests and it receives visits (Fase 29S). Since
 * Fase 29W (ADR 0077) a visit accepted that goes a week past its end without its quote sent counts among the slowest, unless
 * its quote was discarded, or its work was charged at the visit and no quote was started.
 */
export function ownQuoteTimeNote(quoteTime: QuoteTime | null | undefined): string {
  if (quoteTime == null)
    return 'Todavía no contamos visitas en línea de 3 clientes para saber en cuánto sueles cotizar: una cuenta cuando mandas su cotización, o cuando pasa una semana desde que termina la visita sin que la mandes.';
  if (quoteTime === QuoteTime.Slower)
    return 'Sueles tardar una semana o más en mandar la cotización después de la visita, así que tus clientes no lo leen. Cuentan así también las visitas que llevan una semana de terminadas sin su cotización mandada.';
  return 'Lo contamos desde que termina cada visita en línea hasta que mandas su cotización, en los últimos 90 días, la última de cada cliente. Una visita que lleva una semana de terminada sin su cotización mandada cuenta entre las más lentas, salvo que descartes su cotización o que cobres el trabajo en la visita sin empezar una. Tus clientes lo leen en tu tarjeta y donde te piden la visita.';
}

/** What its owner reads of how soon it confirms its online orders, while Explorar counts them (Fase 29T, ADR 0074). */
export function ownOrderConfirmTimeNote(confirmTime: OrderConfirmTime | null | undefined): string {
  if (confirmTime == null)
    return 'Todavía no contamos pedidos en línea de 3 clientes para saber en cuánto sueles confirmarlos: uno cuenta cuando lo aceptas o lo rechazas, o cuando pasa media hora sin respuesta.';
  if (confirmTime === OrderConfirmTime.Slower)
    return 'Sueles tardar media hora o más en confirmar tus pedidos en línea, así que tus clientes no lo leen.';
  return 'Lo contamos desde que llega cada pedido de tu menú en línea hasta que lo aceptas o lo rechazas, en los últimos 90 días, el último de cada cliente y sin los de tus mesas. Tus clientes lo leen en tu tarjeta y en tu menú.';
}
