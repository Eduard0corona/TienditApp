import type { Schemas } from '../../api/typedClient';
import { PlatformCommissionBase, type OrderChannel } from '../../types';
import { formatDate } from '../../utils/dates';
import { isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { ORDER_CHANNEL_LABELS } from '../../utils/labels';
import { formatMoney } from '../../utils/money';

// Plataformas de reparto in Mi negocio: how the business works with Rappi, Uber Eats and DiDi Food. The server keeps the
// terms, checks them and works out what they give; these helpers only word what it answered and keep the form as typed.

/** One delivery platform as the API answers it: never saved, it comes without a commission and with the fiscal defaults. */
export type PlatformSetting = Schemas['DeliveryPlatformDto'];

/** The form of one platform. The percentages stay as typed until they are saved. */
export interface DeliveryPlatformDraft {
  isActive: boolean;
  commissionPercent: string;
  commissionBase: PlatformCommissionBase;
  commissionVatPercent: string;
  isrWithholdingPercent: string;
  vatWithholdingPercent: string;
}

/** The field of the form a problem is about, so it is marked. */
export type DraftField = 'commission' | 'isr' | 'vatWithholding';

export interface DraftProblem {
  message: string;
  field?: DraftField;
}

/** The IVA rates a platform can invoice its commission with (VatRateRules): the general one, the border one and 0 %. */
export const COMMISSION_VAT_RATES = [16, 8, 0] as const;

export const COMMISSION_REQUIRED_MESSAGE = 'Escribe la comisión de tu contrato.';
// The server's own messages (PlatformTerms): the browser only spots a percentage it cannot read, the API checks the rest.
const COMMISSION_MESSAGE = 'La comisión va de 0 % a 60 %, con hasta dos decimales.';
const ISR_MESSAGE = 'La retención de ISR va de 0 % a 20 %, con hasta dos decimales.';
const VAT_WITHHOLDING_MESSAGE = 'La retención de IVA va de 0 % a 100 % del IVA del pedido, con hasta dos decimales.';

/** Where each platform states the commission of its contract. A percentage never wraps away from its sign. */
const COMMISSION_HELP: Record<DeliveryPlatform, string> = {
  3: 'Rappi la fija en tu contrato; búscala en tu relación de venta.',
  4: 'Uber publica 25\u00a0% (Lite), 27\u00a0% (Plus) y 29\u00a0% (Premium) más IVA; súmale marketing o Uber One si los pagas.',
  5: 'DiDi la fija en tu contrato (en su ejemplo es 30\u00a0%); búscala en Finanzas › Detalles de pagos.',
};

/** "Rappi", "Uber Eats", "DiDi Food". */
export function platformName(channel: OrderChannel): string {
  return ORDER_CHANNEL_LABELS[channel];
}

export function commissionHelp(channel: OrderChannel): string {
  return isDeliveryPlatform(channel) ? COMMISSION_HELP[channel] : '';
}

const PERCENT = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 2 });

/** "2.5 %": a percentage of the terms as the API keeps it (25.00 is 25 %), never split from its sign. */
export function formatPercent(value: number): string {
  return `${PERCENT.format(value)}\u00a0%`;
}

/**
 * A percentage typed by a person ("30", "2.5", "2,5", "16 %"), with at most two decimals and never negative; null when it
 * is not one. The API decides whether it is within the limits.
 */
export function parsePercent(text: string): number | null {
  const cleaned = text.replace(/[%\s]/g, '').replace(',', '.');
  return /^\d+(\.\d{0,2})?$/.test(cleaned) ? Number(cleaned) : null;
}

/** The form opened on a platform. One never saved starts on: configuring it is to take its orders. */
export function draftOf(platform: PlatformSetting): DeliveryPlatformDraft {
  return {
    isActive: platform.isConfigured ? platform.isActive : true,
    commissionPercent: platform.commissionPercent === null || platform.commissionPercent === undefined ? '' : String(platform.commissionPercent),
    commissionBase: platform.commissionBase,
    commissionVatPercent: String(platform.commissionVatPercent),
    isrWithholdingPercent: String(platform.isrWithholdingPercent),
    vatWithholdingPercent: String(platform.vatWithholdingPercent),
  };
}

/** What the browser can tell before asking the API: the commission is missing or a percentage cannot be read. */
export function draftProblem(draft: DeliveryPlatformDraft): DraftProblem | null {
  if (!draft.commissionPercent.trim()) return { field: 'commission', message: COMMISSION_REQUIRED_MESSAGE };
  if (parsePercent(draft.commissionPercent) === null) return { field: 'commission', message: COMMISSION_MESSAGE };
  if (parsePercent(draft.isrWithholdingPercent) === null) return { field: 'isr', message: ISR_MESSAGE };
  if (parsePercent(draft.vatWithholdingPercent) === null) return { field: 'vatWithholding', message: VAT_WITHHOLDING_MESSAGE };
  return null;
}

/** What is saved, with the revision read: the API answers 409 when someone else saved the platform meanwhile. */
export function bodyOf(platform: PlatformSetting, draft: DeliveryPlatformDraft): Schemas['SaveDeliveryPlatformRequest'] {
  return {
    revision: platform.revision,
    isActive: draft.isActive,
    commissionPercent: parsePercent(draft.commissionPercent) ?? 0,
    commissionBase: draft.commissionBase,
    commissionVatPercent: Number(draft.commissionVatPercent),
    isrWithholdingPercent: parsePercent(draft.isrWithholdingPercent) ?? 0,
    vatWithholdingPercent: parsePercent(draft.vatWithholdingPercent) ?? 0,
  };
}

/** "Comisión 30 % sobre el total con IVA + IVA 16 % · Retiene ISR 2.5 % e IVA 50 %". */
export function describeTerms(platform: PlatformSetting): string {
  const base = platform.commissionBase === PlatformCommissionBase.SubTotalWithoutTax ? 'el subtotal sin IVA' : 'el total con IVA';
  return `Comisión ${formatPercent(platform.commissionPercent ?? 0)} sobre ${base} + IVA ${formatPercent(platform.commissionVatPercent)}`
    + ` · Retiene ISR ${formatPercent(platform.isrWithholdingPercent)} e IVA ${formatPercent(platform.vatWithholdingPercent)}`;
}

/**
 * The server's example, at the business's rate of IVA: "Con un pedido de $1,000.00 con IVA al 16 % incluido cobrado por
 * Rappi: comisión e IVA $348.00, retenciones $90.52, te deposita $561.48."
 */
export function describeExample(platform: PlatformSetting): string | null {
  const example = platform.example;
  if (!example) return null;
  return `Con un pedido de ${formatMoney(example.orderTotal)} con IVA al ${formatPercent(example.orderVatPercent)} incluido `
    + `cobrado por ${platformName(platform.channel)}: comisión e IVA ${formatMoney(example.commissionAndTax)}, `
    + `retenciones ${formatMoney(example.withheld)}, te deposita ${formatMoney(example.expectedSettlement)}.`;
}

/** "Cambió mariana.ortiz@cafelaesquina.mx el 11 oct 2026", in the business's time zone. */
export function describeChange(platform: PlatformSetting): string | null {
  if (!platform.changedAt) return null;
  const when = formatDate(platform.changedAt);
  return platform.changedBy ? `Cambió ${platform.changedBy} el ${when}` : `Cambió el ${when}`;
}

/** The notice after saving: it says what changed for Vender. */
export function savedMessage(platform: PlatformSetting, draft: DeliveryPlatformDraft): string {
  const name = platformName(platform.channel);
  if (draft.isActive && !platform.isActive) return `${name} quedó activa.`;
  if (!draft.isActive && platform.isActive) return `${name} ya no aparece en Vender.`;
  return `${name} guardada.`;
}
