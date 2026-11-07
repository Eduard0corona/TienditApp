import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../api/queries';
import { quoteFolio } from '../utils/quotes';
import { useToast } from './useToast';

/** A customer answered a quote from its link (Fase 29R): which one and how, never what they wrote. */
export interface QuoteAnsweredPayload {
  quoteId: string;
  number: string;
  revision: number;
  accepted: boolean;
}

/** "Te aceptaron la cotización COT-000007 · versión 2.", and what follows. */
export function describeAnswer(payload: QuoteAnsweredPayload): { title: string; message: string } {
  const folio = quoteFolio(payload.number, payload.revision);
  return payload.accepted
    ? { title: 'Cotización aceptada', message: `Te aceptaron la cotización ${folio}. Ábrela en Cotizaciones para seguir.` }
    : { title: 'Cotización rechazada', message: `Te rechazaron la cotización ${folio}. Puedes corregirla y mandarla otra vez.` };
}

/**
 * The live answers to the quotes of the business, for whoever works them (the top bar mounts it, Fase 29R): the bell reads
 * them at once, the list and the quote open on screen reload, and a notice tells each one. Answers sent while the connection
 * was down are lost: every (re)connection reads again, and the bell still reads every minute.
 */
export function useQuoteRepliesLive(enabled: boolean, tenantId?: string) {
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
      // The list and the detail; their settings and the catalog of a draft do not change with an answer.
      void queryClient.invalidateQueries({
        queryKey: ['quotes'],
        predicate: (query) => query.queryKey[1] !== 'settings' && query.queryKey[1] !== 'catalog',
      });
    };
    // The top bar is on every first screen: SignalR loads apart, once the person is known to work the quotes.
    void import('../api/signalr').then(({ createHubConnection, keepHubConnected }) => {
      if (isDisposed) return;
      const connection = createHubConnection('/hubs/quotes');
      connection.on('QuoteAnswered', (payload: QuoteAnsweredPayload) => {
        changed();
        const { title, message } = describeAnswer(payload);
        if (payload.accepted) toastRef.current.success(message, { title, durationMs: 10_000 });
        else toastRef.current.warning(message, { title, durationMs: 10_000 });
      });
      stop = keepHubConnected(connection, 'QuoteHub', () => isDisposed, changed);
    }).catch(() => {
      // A new version replaced the chunk: the page reloads (vite:preloadError), and the bell still reads every minute.
    });
    return () => {
      isDisposed = true;
      stop?.();
    };
  }, [enabled, tenantId, queryClient]);
}
