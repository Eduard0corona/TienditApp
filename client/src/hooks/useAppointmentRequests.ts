import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../api/queries';
import { formatInBusinessZone } from '../utils/dates';
import { useArrivalChime } from './useArrivalChime';
import { useToast } from './useToast';

/** A request online that arrived (Fase 29N): what was asked for and when, never who asked. */
export interface AppointmentRequestedPayload {
  appointmentId: string;
  serviceName: string;
  startAt: string;
  /** A visit to quote at the customer's address, not an appointment at the business. */
  homeVisit: boolean;
  /** It came from the business's own team: the screens refresh without telling it. */
  fromTeam: boolean;
}

/** An appointment booked online that its customer cancelled (Fase 29O): what it was for and when, never who cancelled it. */
export interface AppointmentCancelledPayload {
  appointmentId: string;
  serviceName: string;
  startAt: string;
  /** A visit at the customer's address, not an appointment at the business. */
  homeVisit: boolean;
  /** It came from the business's own team: the screens refresh without telling it. */
  fromTeam: boolean;
  /** It still waited for the business's answer. */
  wasRequest: boolean;
}

/**
 * Fase 29P: the device's choice of a sound with each request or cancellation of a customer. The agenda turns it on or off;
 * the top bar plays it on any screen.
 */
export const APPOINTMENTS_SOUND_KEY = 'tienditapp:appointments-sound';

/** The window event the agenda listens to: a request arrived, or the live connection came back and may have missed one. */
export const APPOINTMENT_REQUESTS_CHANGED = 'tienditapp:appointment-requests-changed';

/** "viernes, 6 de noviembre, 10:00 a.m.", in the business's zone. */
function when(startAt: string): string {
  return formatInBusinessZone(startAt, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    hour: 'numeric',
    minute: '2-digit',
  });
}

/** "Corte de cabello, viernes, 6 de noviembre, 10:00 a.m.". */
export function describeRequest(payload: AppointmentRequestedPayload): { title: string; message: string } {
  return {
    title: payload.homeVisit ? 'Nueva solicitud de visita' : 'Nueva solicitud de cita',
    message: `${payload.serviceName}, ${when(payload.startAt)}. Respóndela desde la Agenda.`,
  };
}

/** Fase 29O: "Cita cancelada: Corte de cabello, viernes, 6 de noviembre, 10:00 a.m. Ese horario quedó libre.". */
export function describeCancellation(payload: AppointmentCancelledPayload): { title: string; message: string } {
  const kind = payload.homeVisit ? 'visita' : 'cita';
  return {
    title: payload.wasRequest ? `Solicitud de ${kind} cancelada` : `${payload.homeVisit ? 'Visita' : 'Cita'} cancelada`,
    message: `El cliente canceló ${payload.serviceName}, ${when(payload.startAt)}. Ese horario quedó libre.`,
  };
}

/**
 * The live requests online of the business, for whoever answers them (the top bar mounts it): the bell reads them at once,
 * the agenda reloads, and a notice tells each one that came from a customer. Since the Fase 29O, so do the appointments its
 * customers cancel, and since the Fase 29P each one rings when the device has the sound on. Events sent while the
 * connection was down are lost: every (re)connection reads again, and the bell still reads every minute.
 */
export function useAppointmentRequestsLive(enabled: boolean, tenantId?: string) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const toastRef = useRef(toast);
  useEffect(() => {
    toastRef.current = toast;
  }, [toast]);
  // Fase 29P: rings on this device when the agenda turned the sound on (never for the team's own).
  const chime = useArrivalChime(APPOINTMENTS_SOUND_KEY);
  const playRef = useRef(chime.play);
  useEffect(() => {
    playRef.current = chime.play;
  }, [chime.play]);

  useEffect(() => {
    // Switching business reloads the page; the business is a key all the same, so a connection never outlives its own.
    if (!enabled || !tenantId) return undefined;
    let isDisposed = false;
    let stop: (() => void) | undefined;
    const changed = () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.attention });
      window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));
    };
    // The top bar is on every first screen: SignalR loads apart, once the person is known to answer the requests.
    void import('../api/signalr').then(({ createHubConnection, keepHubConnected }) => {
      if (isDisposed) return;
      const connection = createHubConnection('/hubs/appointments');
      connection.on('AppointmentRequested', (payload: AppointmentRequestedPayload) => {
        changed();
        if (payload.fromTeam) return;
        const { title, message } = describeRequest(payload);
        toastRef.current.info(message, { title, durationMs: 10_000 });
        playRef.current('arrival');
      });
      connection.on('AppointmentCancelled', (payload: AppointmentCancelledPayload) => {
        changed();
        if (payload.fromTeam) return;
        const { title, message } = describeCancellation(payload);
        toastRef.current.warning(message, { title, durationMs: 10_000 });
        playRef.current('leaving');
      });
      stop = keepHubConnected(connection, 'AppointmentHub', () => isDisposed, changed);
    }).catch(() => {
      // A new version replaced the chunk: the page reloads (vite:preloadError), and the bell still reads every minute.
    });
    return () => {
      isDisposed = true;
      stop?.();
    };
  }, [enabled, tenantId, queryClient]);
}

/** Runs <paramref name="callback"/> when a request online arrives or is cancelled, or the live connection comes back. */
export function useOnAppointmentRequestsChanged(callback: () => void) {
  const callbackRef = useRef(callback);
  useEffect(() => {
    callbackRef.current = callback;
  }, [callback]);

  useEffect(() => {
    const listener = () => callbackRef.current();
    window.addEventListener(APPOINTMENT_REQUESTS_CHANGED, listener);
    return () => window.removeEventListener(APPOINTMENT_REQUESTS_CHANGED, listener);
  }, []);
}
