import { act, render, renderHook, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ToastProvider from '../components/feedback/ToastProvider';
import { queryKeys } from '../api/queries';
import { setBusinessTimeZone } from '../utils/dates';
import {
  APPOINTMENT_REQUESTS_CHANGED,
  describeRequest,
  useAppointmentRequestsLive,
  useOnAppointmentRequestsChanged,
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

  it('tells a request of a customer, and the bell and the agenda read it at once', () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    renderLive();

    act(() => hub.handlers.get('AppointmentRequested')?.(request()));

    expect(hub.urls).toEqual(['/hubs/appointments']);
    expect(screen.getByText('Nueva solicitud de cita')).toBeInTheDocument();
    expect(screen.getByText(/^Corte de cabello, viernes, 6 de noviembre, 10:00.*Respóndela desde la Agenda\.$/)).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: queryKeys.attention });
    expect(changes).toBe(1);
  });

  it('refreshes without telling a request of the team, and reads again when the connection comes back', () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    renderLive();

    act(() => hub.handlers.get('AppointmentRequested')?.(request({ fromTeam: true })));
    act(() => hub.onConnected?.());

    expect(screen.queryByText(/Nueva solicitud/)).not.toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledTimes(2);
    expect(changes).toBe(2);
  });

  it('does not connect for whoever does not answer them, nor without a business, and stops when it goes', () => {
    renderLive({ enabled: false });
    renderLive({ tenantId: '' });
    expect(hub.urls).toEqual([]);

    const { unmount } = renderLive();
    unmount();
    expect(hub.stop).toHaveBeenCalledTimes(1);
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
