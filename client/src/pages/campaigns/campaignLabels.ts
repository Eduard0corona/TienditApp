import {
  CampaignAudienceKind,
  CampaignStatus,
  OutgoingMessageStatus,
} from '../../types';

// Same chips as order states (theme.css .ta-chip-*): always with text.
export const CAMPAIGN_STATUS: Record<CampaignStatus, { label: string; className: string }> = {
  [CampaignStatus.Scheduled]: { label: 'Programada', className: 'ta-chip ta-chip-confirmed' },
  [CampaignStatus.Sending]: { label: 'Enviando', className: 'ta-chip ta-chip-preparing' },
  [CampaignStatus.Completed]: { label: 'Terminada', className: 'ta-chip ta-chip-ready' },
  [CampaignStatus.Cancelled]: { label: 'Cancelada', className: 'ta-chip ta-chip-cancelled' },
};

export const MESSAGE_STATUS: Record<OutgoingMessageStatus, { label: string; className: string }> = {
  [OutgoingMessageStatus.Pending]: { label: 'Pendiente', className: 'ta-chip ta-chip-new' },
  [OutgoingMessageStatus.Sent]: { label: 'Enviado', className: 'ta-chip ta-chip-ready' },
  [OutgoingMessageStatus.Failed]: { label: 'Falló', className: 'ta-chip ta-chip-cancelled' },
  [OutgoingMessageStatus.Skipped]: { label: 'No enviado', className: 'ta-chip ta-chip-done' },
  [OutgoingMessageStatus.Cancelled]: { label: 'Cancelado', className: 'ta-chip ta-chip-done' },
};

export function describeAudience(campaign: { audienceKind: number; audienceMinimumOrders: number; audienceDays?: number | null }): string {
  switch (campaign.audienceKind) {
    case CampaignAudienceKind.AllCustomers:
      return 'Todos los clientes con teléfono';
    case CampaignAudienceKind.Buyers: {
      const orders = campaign.audienceMinimumOrders === 1 ? '1 compra' : `${campaign.audienceMinimumOrders} compras`;
      return campaign.audienceDays
        ? `Con ${orders} o más en los últimos ${campaign.audienceDays} días`
        : `Con ${orders} o más`;
    }
    default:
      return `Sin comprar en los últimos ${campaign.audienceDays ?? 0} días`;
  }
}

/** "{nombre}" chips, inserted at the cursor of the message box. */
export const PLACEHOLDER_HELP: Record<string, string> = {
  nombre: 'Nombre del cliente',
  negocio: 'Nombre de tu negocio',
  servicio: 'Servicio de la cita',
  fecha: 'Día de la cita',
  hora: 'Hora de la cita',
};

/** The same chips in a reminder of what a customer owes (Fase 26C): {fecha} is the last day to pay that passed. */
export const CREDIT_PLACEHOLDER_HELP: Record<string, string> = {
  nombre: 'Nombre del cliente',
  negocio: 'Nombre de tu negocio',
  monto: 'Lo que debe en total',
  vencido: 'Lo que ya venció',
  fecha: 'Último día para pagar que ya pasó',
};

/** The same chips in the notice before something comes due (Fase 26E): {fecha} is the coming last day to pay. */
export const CREDIT_DUE_SOON_PLACEHOLDER_HELP: Record<string, string> = {
  nombre: 'Nombre del cliente',
  negocio: 'Nombre de tu negocio',
  monto: 'Lo que debe en total',
  porvencer: 'Lo que vence ese día',
  fecha: 'Último día para pagar que viene',
};
