import { act, render, renderHook, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ToastProvider from '../components/feedback/ToastProvider';
import { queryKeys } from '../api/queries';
import { setBusinessTimeZone } from '../utils/dates';
import {
  APPOINTMENT_REQUESTS_CHANGED,
  describeCancellation,
  describeRequest,
  useAppointmentRequestsLive,
  useOnAppointmentRequestsChanged,
  type AppointmentCancelledPayload,
  type AppointmentRequestedPayload,
} from './useAppointmentRequests';

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (payload: unknown) => void>(),
  onConnected: undefined as (() => void) | undefined,
  stop: vi.fn(),
  urls: [] as string[],
}));

vi.mock('../api/signalr', () => ({
  createHubConnection: (url: string) => {
    hub.urls.push(url);
    return { on: (name: string, handler: (payload: unknown) => void) => hub.handlers.set(name, handler) };
  },
  keepHubConnected: (_connection: unknown, _name: string, _isDisposed: () => boolean, onConnected?: () => void) => {
    hub.onConnected = onConnected;
    return hub.stop;
  },
}));

const request = (overrides: Partial<AppointmentRequestedPayload> = {}): AppointmentRequestedPayload => ({
  appointmentId: 'r1',
  serviceName: 'Corte de cabello',
  // Friday 2026-11-06 at 10:00 in Mexico City.
  startAt: '2026-11-06T16:00:00Z',
  homeVisit: false,
  fromTeam: false,
  ...overrides,
});

const cancellation = (overrides: Partial<AppointmentCancelledPayload> = {}): AppointmentCancelledPayload => ({
  ...request(),
  wasRequest: false,
  ...overrides,
});

function Live({ enabled = true, tenantId = 't1' }: { enabled?: boolean; tenantId?: string }) {
  useAppointmentRequestsLive(enabled, tenantId);
  return null;
}

describe('the requests online, live (Fase 29N)', () => {
  let queryClient: QueryClient;
  let changes: number;
  const countChange = () => {
    changes += 1;
  };

  beforeEach(() => {
    setBusinessTimeZone('America/Mexico_City');
    hub.handlers.clear();
    hub.onConnected = undefined;
    hub.urls = [];
    hub.stop.mockReset();
    queryClient = new QueryClient();
    changes = 0;
    window.addEventListener(APPOINTMENT_REQUESTS_CHANGED, countChange);
  });

  afterEach(() => window.removeEventListener(APPOINTMENT_REQUESTS_CHANGED, countChange));

  const renderLive = (props?: { enabled?: boolean; tenantId?: string }) =>
    render(
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <Live {...props} />
        </ToastProvider>
      </QueryClientProvider>,
    );

  /** SignalR loads apart: the connection exists once its module arrives. */
  const connected = () => waitFor(() => expect(hub.handlers.has('AppointmentRequested')).toBe(true));

  it('tells a request of a customer, and the bell and the agenda read it at once', async () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    renderLive();
    await connected();

    act(() => hub.handlers.get('AppointmentRequested')?.(request()));

    expect(hub.urls).toEqual(['/hubs/appointments']);
    expect(screen.getByText('Nueva solicitud de cita')).toBeInTheDocument();
    expect(screen.getByText(/^Corte de cabello, viernes, 6 de noviembre, 10:00.*Respóndela desde la Agenda\.$/)).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.attention });
    expect(changes).toBe(1);
  });

  it('refreshes without telling a request of the team, and reads again when the connection comes back', async () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    renderLive();
    await connected();

    act(() => hub.handlers.get('AppointmentRequested')?.(request({ fromTeam: true })));
    act(() => hub.onConnected?.());

    expect(screen.queryByText(/Nueva solicitud/)).not.toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledTimes(2);
    expect(changes).toBe(2);
  });

  it('does not connect for whoever does not answer them, nor without a business, and stops when it goes', async () => {
    renderLive({ enabled: false });
    renderLive({ tenantId: '' });

    const { unmount } = renderLive();
    await connected();
    unmount();

    expect(hub.urls).toEqual(['/hubs/appointments']);
    expect(hub.stop).toHaveBeenCalledTimes(1);
  });

  it('never connects when it goes before SignalR arrives', async () => {
    const { unmount } = renderLive();
    unmount();
    await import('../api/signalr');
    await Promise.resolve();

    expect(hub.urls).toEqual([]);
    expect(hub.stop).not.toHaveBeenCalled();
  });

  it('tells an appointment its customer cancelled, and the bell and the agenda read it again (Fase 29O)', async () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    renderLive();
    await connected();

    act(() => hub.handlers.get('AppointmentCancelled')?.(cancellation()));

    expect(screen.getByText('Cita cancelada')).toBeInTheDocument();
    expect(
      screen.getByText(/^El cliente canceló Corte de cabello, viernes, 6 de noviembre, 10:00.*Ese horario quedó libre\.$/),
    ).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.attention });
    expect(changes).toBe(1);
  });

  it('refreshes without telling a cancellation of the team (Fase 29O)', async () => {
    renderLive();
    await connected();

    act(() => hub.handlers.get('AppointmentCancelled')?.(cancellation({ fromTeam: true })));

    expect(screen.queryByText(/cancelada/)).not.toBeInTheDocument();
    expect(changes).toBe(1);
  });

  it('names a cancelled request and a cancelled visit as such (Fase 29O)', () => {
    expect(describeCancellation(cancellation({ wasRequest: true })).title).toBe('Solicitud de cita cancelada');
    expect(describeCancellation(cancellation({ wasRequest: true, homeVisit: true })).title).toBe('Solicitud de visita cancelada');
    expect(describeCancellation(cancellation({ homeVisit: true, serviceName: 'Visita a domicilio' })).title).toBe('Visita cancelada');
  });

  it('names a visit as such', () => {
    expect(describeRequest(request({ homeVisit: true, serviceName: 'Visita a domicilio' })).title).toBe('Nueva solicitud de visita');
  });

  it('lets a screen hear each change with its latest callback', () => {
    const wrapper = ({ children }: { children: ReactNode }) => <>{children}</>;
    const first = vi.fn();
    const second = vi.fn();
    const { rerender, unmount } = renderHook(({ callback }) => useOnAppointmentRequestsChanged(callback), {
      wrapper,
      initialProps: { callback: first },
    });
    rerender({ callback: second });

    act(() => {
      window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));
    });
    unmount();
    window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));

    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });
});
