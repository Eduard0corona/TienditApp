import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { DayOfWeek, OnlineMenuMode, TenantCapability, type OpeningIntervalDto, type TenantProfileDto } from '../../types';
import OpeningHoursCard from './OpeningHoursCard';

vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const confirm = vi.fn();

function withTenant(openingHours: OpeningIntervalDto[] | null, refreshTenant = vi.fn()) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: {
      id: 't1',
      name: 'Taquería Lupita',
      slug: 'lupita',
      onlineMenuMode: OnlineMenuMode.Orders,
      timeZone: 'America/Mexico_City',
      openingHours,
    } as TenantProfileDto,
    capabilities: TenantCapability.Food,
    isLoading: false,
    refreshTenant,
  });
  return refreshTenant;
}

function domainError(detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status: 422, statusText: '', headers, config: { headers }, data: { code: 'domain_rule', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

const split: OpeningIntervalDto[] = [
  { day: DayOfWeek.Monday, opens: '09:00', closes: '14:00' },
  { day: DayOfWeek.Monday, opens: '16:00', closes: '20:00' },
  { day: DayOfWeek.Friday, opens: '18:00', closes: '02:00' },
];

describe('OpeningHoursCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm, prompt: vi.fn() });
  });

  it('starts with every day closed and says the menu never shows as closed', () => {
    withTenant(null);
    render(<OpeningHoursCard />);

    expect(screen.getAllByRole('switch')).toHaveLength(7);
    expect(screen.getByRole('switch', { name: 'Lunes' })).not.toBeChecked();
    expect(screen.getByText(/Aún no tienes horario/)).toBeInTheDocument();
    expect(screen.getByText(/los pedidos desde el QR de una mesa sí llegan/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar horario' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Quitar horario' })).not.toBeInTheDocument();
  });

  it('opens a day at 9:00 to 20:00, repeats it on the next day and saves the week', async () => {
    const refreshTenant = withTenant(null);
    vi.mocked(apiRequest).mockResolvedValue({ intervals: [] } as never);
    render(<OpeningHoursCard />);

    await userEvent.click(screen.getByRole('switch', { name: 'Lunes' }));
    expect(screen.getByLabelText('Abre el lunes')).toHaveValue('09:00');
    expect(screen.getByLabelText('Cierra el lunes')).toHaveValue('20:00');
    fireEvent.change(screen.getByLabelText('Abre el lunes'), { target: { value: '10:00' } });
    await userEvent.click(screen.getByRole('switch', { name: 'Martes' }));
    expect(screen.getByLabelText('Abre el martes')).toHaveValue('10:00');

    await userEvent.click(screen.getByRole('button', { name: 'Guardar horario' }));

    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/auth/my-tenant/opening-hours', {
      body: {
        intervals: [
          { day: DayOfWeek.Monday, opens: '10:00', closes: '20:00' },
          { day: DayOfWeek.Tuesday, opens: '10:00', closes: '20:00' },
        ],
      },
    });
    expect(refreshTenant).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('Horario guardado'));
  });

  it('shows the saved hours with a second opening and the ones that end the next day', async () => {
    withTenant(split);
    vi.mocked(apiRequest).mockResolvedValue({ intervals: [] } as never);
    render(<OpeningHoursCard />);

    expect(screen.getByRole('switch', { name: 'Lunes' })).toBeChecked();
    expect(screen.getByLabelText('Abre el lunes, segundo horario')).toHaveValue('16:00');
    expect(screen.getByRole('switch', { name: 'Martes' })).not.toBeChecked();
    expect(screen.getByText('Cierra al día siguiente')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar horario' })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: 'Quitar el segundo horario del lunes' }));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar horario' }));

    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/auth/my-tenant/opening-hours', {
      body: {
        intervals: [
          { day: DayOfWeek.Monday, opens: '09:00', closes: '14:00' },
          { day: DayOfWeek.Friday, opens: '18:00', closes: '02:00' },
        ],
      },
    });
  });

  it('asks for the times left blank and shows why the API refuses the hours', async () => {
    withTenant([{ day: DayOfWeek.Monday, opens: '09:00', closes: '20:00' }]);
    vi.mocked(apiRequest).mockRejectedValue(domainError('Los horarios del lunes se enciman.'));
    render(<OpeningHoursCard />);

    await userEvent.click(screen.getByRole('button', { name: 'Agregar horario al lunes' }));
    expect(screen.queryByRole('button', { name: 'Agregar horario al lunes' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Guardar horario' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Escribe a qué hora abres y cierras el lunes.');
    expect(apiRequest).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Abre el lunes, segundo horario'), { target: { value: '18:00' } });
    fireEvent.change(screen.getByLabelText('Cierra el lunes, segundo horario'), { target: { value: '22:00' } });
    await userEvent.click(screen.getByRole('button', { name: 'Guardar horario' }));

    expect(apiRequest).toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('Los horarios del lunes se enciman.');
    expect(screen.getByRole('button', { name: 'Guardar horario' })).toBeEnabled();
  });

  it('asks for an open day instead of saving a week without any', async () => {
    withTenant([{ day: DayOfWeek.Monday, opens: '09:00', closes: '20:00' }]);
    render(<OpeningHoursCard />);

    await userEvent.click(screen.getByRole('switch', { name: 'Lunes' }));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar horario' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Marca al menos un día abierto');
    expect(apiRequest).not.toHaveBeenCalled();
  });

  it('removes the hours after confirming it', async () => {
    const refreshTenant = withTenant(split);
    confirm.mockResolvedValue(true);
    vi.mocked(apiRequest).mockResolvedValue({ intervals: [] } as never);
    render(<OpeningHoursCard />);

    await userEvent.click(screen.getByRole('button', { name: 'Quitar horario' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }));
    expect(apiRequest).toHaveBeenCalledWith('put', '/api/v1/auth/my-tenant/opening-hours', { body: { intervals: [] } });
    expect(refreshTenant).toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('Horario quitado'));
  });

  it('says that table orders still arrive outside the hours but not on the special days it closes', () => {
    withTenant(split);
    render(<OpeningHoursCard />);

    expect(screen.getByText(
      'Fuera de tu horario el menú se puede ver, pero no recibe pedidos para recoger o a domicilio; los pedidos desde el '
      + 'QR de una mesa sí llegan fuera de horario, porque el cliente ya está en tu negocio, pero no en los días especiales '
      + 'que cierras.',
    )).toBeInTheDocument();
  });

  it('tells the special days the hours changed after saving them', async () => {
    const refreshTenant = withTenant(split);
    const onSaved = vi.fn();
    vi.mocked(apiRequest).mockResolvedValue({ intervals: [] } as never);
    render(<OpeningHoursCard onSaved={onSaved} />);

    await userEvent.click(screen.getByRole('button', { name: 'Quitar el segundo horario del lunes' }));
    await userEvent.click(screen.getByRole('button', { name: 'Guardar horario' }));

    expect(onSaved).toHaveBeenCalledTimes(1);
    // After the business is read again: the special days ask the API for suggestions of the new hours.
    expect(refreshTenant.mock.invocationCallOrder[0]).toBeLessThan(onSaved.mock.invocationCallOrder[0]);
  });

  it('says what is left when removing the hours of a business with special days ahead', async () => {
    withTenant(split);
    confirm.mockResolvedValue(false);
    const { rerender } = render(<OpeningHoursCard />);

    await userEvent.click(screen.getByRole('button', { name: 'Quitar horario' }));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({
      message: 'Tu menú en línea dejará de mostrar tu horario y ya no aparecerá como cerrado.',
    }));

    rerender(<OpeningHoursCard upcomingSpecialDays={2} />);
    await userEvent.click(screen.getByRole('button', { name: 'Quitar horario' }));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({
      message: 'Tu menú en línea dejará de mostrar tu horario de la semana y solo se verá cerrado en tus días especiales.',
    }));
    expect(apiRequest).not.toHaveBeenCalled();
  });
});
