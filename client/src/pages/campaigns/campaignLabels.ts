import {
  CampaignAudienceKind,
  CampaignStatus,
  OutgoingMessageStatus,
} from '../../types';

export const CAMPAIGN_STATUS: Record<CampaignStatus, { label: string; className: string }> = {
  [CampaignStatus.Scheduled]: { label: 'Programada', className: 'bg-info-subtle text-info-emphasis' },
  [CampaignStatus.Sending]: { label: 'Enviando', className: 'bg-warning-subtle text-warning-emphasis' },
  [CampaignStatus.Completed]: { label: 'Terminada', className: 'bg-success-subtle text-success-emphasis' },
  [CampaignStatus.Cancelled]: { label: 'Cancelada', className: 'bg-secondary-subtle text-secondary-emphasis' },
};

export const MESSAGE_STATUS: Record<OutgoingMessageStatus, { label: string; className: string }> = {
  [OutgoingMessageStatus.Pending]: { label: 'Pendiente', className: 'bg-info-subtle text-info-emphasis' },
  [OutgoingMessageStatus.Sent]: { label: 'Enviado', className: 'bg-success-subtle text-success-emphasis' },
  [OutgoingMessageStatus.Failed]: { label: 'Falló', className: 'bg-danger-subtle text-danger-emphasis' },
  [OutgoingMessageStatus.Skipped]: { label: 'No enviado', className: 'bg-secondary-subtle text-secondary-emphasis' },
  [OutgoingMessageStatus.Cancelled]: { label: 'Cancelado', className: 'bg-secondary-subtle text-secondary-emphasis' },
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
