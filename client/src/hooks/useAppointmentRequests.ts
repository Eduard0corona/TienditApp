import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../api/queries';
import { formatInBusinessZone } from '../utils/dates';
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

/** The window event the agenda listens to: a request arrived, or the live connection came back and may have missed one. */
export const APPOINTMENT_REQUESTS_CHANGED = 'tienditapp:appointment-requests-changed';

/** "Corte de cabello, viernes, 6 de noviembre, 10:00 a.m.". */
export function describeRequest(payload: AppointmentRequestedPayload): { title: string; message: string } {
  const when = formatInBusinessZone(payload.startAt, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    hour: 'numeric',
    minute: '2-digit',
  });
  return {
    title: payload.homeVisit ? 'Nueva solicitud de visita' : 'Nueva solicitud de cita',
    message: `${payload.serviceName}, ${when}. Respóndela desde la Agenda.`,
  };
}

/**
 * The live requests online of the business, for whoever answers them (the top bar mounts it): the bell reads them at once,
 * the agenda reloads, and a notice tells each one that came from a customer. Events sent while the connection was down are
 * lost: every (re)connection reads again, and the bell still reads every minute.
 */
export function useAppointmentRequestsLive(enabled: boolean, tenantId?: string) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const toastRef = useRef(toast);
  useEffect(() => {
    toastRef.current = toast;
  }, [toast]);

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

/** Runs <paramref name="callback"/> when a request online arrives, or the live connection comes back. */
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
