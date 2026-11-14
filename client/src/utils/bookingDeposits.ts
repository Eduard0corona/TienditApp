import { AppointmentDepositStatus } from '../api/generated/enums';
import { formatMoney } from './money';

/**
 * The deposit to book an appointment online (Fase 31O, ADR 0095): what the business, the booking page and the customer read
 * of it. The server decides where it stands and whether cancelling gives it back; these only say it.
 */

/** Hours before an agendada appointment its customer may cancel and get the deposit back, when the API does not say. */
export const DEPOSIT_CANCEL_HOURS = 24;

interface WithDeposit {
  depositAmount?: number | null;
  depositStatus?: AppointmentDepositStatus | null;
}

/** Whether it holds a deposit still waiting for its sale. */
export function holdsDeposit(appointment: WithDeposit): boolean {
  return appointment.depositAmount != null && appointment.depositStatus === AppointmentDepositStatus.Held;
}

/** The business can give it back: held, or one it kept. */
export function canReturnDeposit(appointment: WithDeposit): boolean {
  return appointment.depositAmount != null
    && (appointment.depositStatus === AppointmentDepositStatus.Held || appointment.depositStatus === AppointmentDepositStatus.Kept);
}

/** Where the deposit stands, as the agenda says it next to the appointment. */
export function businessDepositLabel(appointment: WithDeposit): string | null {
  if (appointment.depositAmount == null) return null;
  const amount = formatMoney(appointment.depositAmount);
  switch (appointment.depositStatus) {
    case AppointmentDepositStatus.Held:
      return `Anticipo de ${amount} pagado en línea: entra al cobrarla`;
    case AppointmentDepositStatus.Applied:
      return `Anticipo de ${amount} en su venta`;
    case AppointmentDepositStatus.Returned:
      return `Anticipo de ${amount} devuelto al cliente`;
    case AppointmentDepositStatus.Kept:
      return `Anticipo de ${amount}: te lo quedas`;
    default:
      return `Anticipo de ${amount}`;
  }
}

/** Where the deposit stands, as its customer reads it on the appointment. */
export function customerDepositLine(appointment: WithDeposit): string | null {
  if (appointment.depositAmount == null) return null;
  const amount = formatMoney(appointment.depositAmount);
  switch (appointment.depositStatus) {
    case AppointmentDepositStatus.Held:
      return `Pagaste un anticipo de ${amount}: se descuenta al cobrarte la cita.`;
    case AppointmentDepositStatus.Applied:
      return `Tu anticipo de ${amount} se descontó de tu cuenta.`;
    case AppointmentDepositStatus.Returned:
      return `Te devolvemos tu anticipo de ${amount} a tu tarjeta o cuenta de Mercado Pago. Según tu banco puede tardar unos días en verse.`;
    case AppointmentDepositStatus.Kept:
      return `El negocio se quedó con tu anticipo de ${amount}.`;
    default:
      return null;
  }
}

/** What cancelling does with the deposit, said before the customer confirms. Empty without one held. */
export function cancelDepositSentence(appointment: WithDeposit & { cancelReturnsDeposit?: boolean }, cancelHours = DEPOSIT_CANCEL_HOURS): string {
  if (!holdsDeposit(appointment)) return '';
  const amount = formatMoney(appointment.depositAmount!);
  return appointment.cancelReturnsDeposit
    ? ` Te devolvemos tu anticipo de ${amount}.`
    : ` Faltan menos de ${cancelHours} horas: el negocio se queda con tu anticipo de ${amount}.`;
}

/**
 * What the customer was told cancelling does with a deposit held, sent with the cancellation so the server refuses it if
 * that changed (the 24 hours passed while the page was open); nothing without one.
 */
export function expectedDepositBack(appointment: WithDeposit & { cancelReturnsDeposit?: boolean }): boolean | undefined {
  return holdsDeposit(appointment) ? !!appointment.cancelReturnsDeposit : undefined;
}

/** What happens to the deposit, as the booking page says it before paying. */
export function depositPolicy(cancelHours = DEPOSIT_CANCEL_HOURS): string {
  return 'Se te devuelve si el negocio no acepta tu cita o no la responde antes de su hora, '
    + `o si la cancelas con al menos ${cancelHours} horas. Si la cancelas después o no llegas, el negocio se lo queda.`;
}
