// Appointments: the agenda, its visits and the appointments of a job.

import type { AppointmentChannel, AppointmentDepositStatus, AppointmentStatus, HomeVisitChargePolicy } from '../api/generated/enums';

export { AppointmentStatus, AppointmentChannel, MyAppointmentsScope } from '../api/generated/enums';

export interface AppointmentDto {
  id: string;
  customerName: string;
  customerPhone: string;
  /** Where the customer of a visit requested online gets its quote (Fase 29B); null when they gave none. */
  customerEmail?: string | null;
  serviceName: string;
  staffName?: string;
  /** The service of the catalog it books (older appointments only have the name). */
  serviceProductId?: string | null;
  /** The team member who attends it. */
  staffUserId?: string | null;
  startAt: string;
  endAt: string;
  status: AppointmentStatus;
  notes?: string;
  /** Where it happens when it is at the customer's address (Fase 22E); null when they come to the business. */
  visit?: AppointmentVisitDto | null;
  /** The sale it was charged with; null until it is charged. */
  saleOrderId?: string | null;
  /** Its sale was cancelled: it is charged again with another one. */
  saleCancelled?: boolean;
  /** Who booked it: the business, or its customer from the online menu (Fase 24D). */
  channel?: AppointmentChannel;
  /** Its customer cancelled it from its link or account (Fase 24D). */
  cancelledByCustomer?: boolean;
  /** The service order whose job it does (Fase 29F): charged in that order, never on its own nor quoted. */
  workOrderId?: string | null;
  /** The folio of that order. */
  workOrderNumber?: string | null;
  /**
   * The photos of what happens the customer sent with a visit requested online (Fase 29H), as private paths, in the order
   * sent; the agenda's lists carry them.
   */
  problemPhotos?: string[];
  /**
   * By when a request online still waiting is answered in time (Fase 29L): the time its customer asked for, or a day after
   * it arrived, whichever comes first. Null for every other appointment, one from the business's own team too.
   */
  answerBy?: string | null;
  /** A request online from the business's own team (Fase 29M): Explorar does not count how soon it is answered. */
  fromTeam?: boolean;
  /**
   * A request online its business declined as fake (Fase 29V), and the mark was not taken back: it does not count in Explorar,
   * and its phone may not request again in the business for 90 days.
   */
  markedFake?: boolean;
  /**
   * What its customer paid online to book it (Fase 31O), with where it stands: held for its sale, in its sale, given back
   * or kept by the business. Null when it was booked without one.
   */
  depositAmount?: number | null;
  depositStatus?: AppointmentDepositStatus | null;
}

/** One appointment that does (part of) the job of a service order that came from a quote (Fase 29F). */
export interface WorkAppointmentDto {
  id: string;
  startAt: string;
  endAt: string;
  staffName?: string | null;
  status: AppointmentStatus;
  /** It happens at the customer's address (the place of the visit the quote came from). */
  atCustomersAddress: boolean;
}

/** The customer's address of an appointment at their home, its zone and the charge for the visit agreed then. */
export interface AppointmentVisitDto {
  postalCode: string;
  addressLine: string;
  references?: string | null;
  zoneName: string;
  /** With the business's tax policy, like its prices; 0 when the zone charges nothing. */
  fee: number;
  /**
   * A visit to quote the customer requested online (Fase 29B): how the business charges it, as agreed; «Sin costo» keeps
   * the fee at 0 wherever it moves. Null for a visit the team arranged.
   */
  chargePolicy?: HomeVisitChargePolicy | null;
}

/** Where an appointment at the customer's address happens, as typed: the server finds its zone. */
export interface AppointmentVisitInput {
  postalCode: string;
  addressLine: string;
  references?: string;
}

export interface CreateAppointmentRequest {
  customerName: string;
  customerPhone: string;
  /** Free name; with serviceProductId the catalog name is used. */
  serviceName?: string;
  serviceProductId?: string;
  staffName?: string;
  staffUserId?: string;
  startAt: string;
  endAt: string;
  notes?: string;
  /** At the customer's address; null (or missing) when the customer comes to the business. */
  visit?: AppointmentVisitInput | null;
}
