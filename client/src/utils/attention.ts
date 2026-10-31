import { SubscriptionStatusValue, type SubscriptionDetail } from '../api/platformApi';
import type { Schemas } from '../api/typedClient';
import { formatDate } from './dates';

export type AttentionItem = Schemas['AttentionItemDto'];

export type AttentionTone = 'danger' | 'warning' | 'info';

export interface AttentionNotice {
  key: string;
  text: string;
  path: string;
  icon: string;
  tone: AttentionTone;
}

const plural = (count: number, singular: string, pluralForm: string) => `${count} ${count === 1 ? singular : pluralForm}`;

/**
 * What a notice from the API says and where it leads (the keys of AttentionKeys). A key the screen does not know yet
 * is left out instead of showing a raw name.
 */
export function describeAttention(item: AttentionItem): AttentionNotice | null {
  const { key, count, urgentCount } = item;
  switch (key) {
    case 'onlineOrdersToConfirm':
      return { key, text: `${plural(count, 'pedido en línea', 'pedidos en línea')} por confirmar`, path: '/orders', icon: 'ri-global-line', tone: 'warning' };
    case 'lowStock':
      return {
        key,
        text: `${plural(count, 'insumo', 'insumos')} por reabastecer${urgentCount > 0 ? ` (${urgentCount} agotado${urgentCount === 1 ? '' : 's'} o crítico${urgentCount === 1 ? '' : 's'})` : ''}`,
        path: '/inventory',
        icon: 'ri-archive-drawer-line',
        tone: urgentCount > 0 ? 'danger' : 'warning',
      };
    case 'drawersOpenSinceYesterday':
      return { key, text: `${plural(count, 'caja abierta', 'cajas abiertas')} desde otro día`, path: '/cash', icon: 'ri-safe-2-line', tone: 'warning' };
    case 'failedMessages':
      return { key, text: `${plural(count, 'mensaje', 'mensajes')} sin enviar en las últimas 24 h`, path: '/campaigns', icon: 'ri-error-warning-line', tone: 'danger' };
    case 'unreadChats':
      return { key, text: `${plural(count, 'conversación', 'conversaciones')} sin leer`, path: '/whatsapp/chat', icon: 'ri-chat-3-line', tone: 'info' };
    case 'lateServices':
      return { key, text: `${plural(count, 'servicio atrasado', 'servicios atrasados')}`, path: '/services/board', icon: 'ri-time-line', tone: 'warning' };
    case 'appointmentRequests':
      return {
        key,
        text: `${plural(count, 'solicitud de cita', 'solicitudes de cita')} por confirmar${urgentCount > 0 ? ` (${urgentCount} de hoy)` : ''}`,
        path: '/appointments?estado=por-confirmar',
        icon: 'ri-calendar-todo-line',
        tone: 'warning',
      };
    case 'appointmentsToConfirm':
      return { key, text: `${plural(count, 'cita', 'citas')} de hoy sin confirmar`, path: '/appointments', icon: 'ri-calendar-check-line', tone: 'info' };
    case 'cfdisToReview':
      // Nothing resolves them on its own (Fase 28K): until someone checks the SAT's portal, what they cover stays unsettled.
      return {
        key,
        text: `${plural(count, 'CFDI', 'CFDI')} por revisar en el portal del SAT`,
        path: '/reports/issued-cfdis',
        icon: 'ri-file-warning-line',
        tone: 'danger',
      };
    case 'homeVisitAddress':
      // A business that goes to homes (Fase 29A) whose store publishes the address of a local: only its owner knows
      // whether customers go there, so the notice waits in «Cómo te ven» until they check it.
      return { key, text: 'Tu tienda muestra la dirección de un local: revisa si atiendes ahí', path: '/settings/business', icon: 'ri-map-pin-user-line', tone: 'info' };
    default:
      return null;
  }
}

/** The notices the screens know, the most urgent first. */
export function describeAttentionItems(items: AttentionItem[]): AttentionNotice[] {
  const order: Record<AttentionTone, number> = { danger: 0, warning: 1, info: 2 };
  return items
    .map(describeAttention)
    .filter((notice): notice is AttentionNotice => notice !== null)
    .sort((a, b) => order[a.tone] - order[b.tone]);
}

/**
 * The plan past due, as one more line in the bell: only owners and admins read the plan, and on the screens where the
 * team works the bell carries it instead of the full notice (Inicio and Configuración keep it).
 */
export function describePlanNotice(subscription: SubscriptionDetail | undefined): AttentionNotice | null {
  if (!subscription?.hasPlan || subscription.status !== SubscriptionStatusValue.PastDue) return null;
  return {
    key: 'planPastDue',
    text: `Pago del plan vencido: el servicio se suspende el ${formatDate(subscription.suspendsAt)}`,
    path: '/settings/business',
    icon: 'ri-error-warning-line',
    tone: 'warning',
  };
}
