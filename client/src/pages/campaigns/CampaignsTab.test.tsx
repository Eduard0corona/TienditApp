import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { messagingApi } from '../../api/messagingApi';
import { useCampaigns, useCancelCampaign, useCreateCampaign } from '../../api/queries';
import { setBusinessTimeZone } from '../../utils/dates';
import CampaignsTab from './CampaignsTab';

vi.mock('../../api/messagingApi', () => ({ messagingApi: { getTemplates: vi.fn(), previewCampaign: vi.fn() } }));
vi.mock('../../api/queries', () => ({
  useCampaigns: vi.fn(), useCancelCampaign: vi.fn(), useCreateCampaign: vi.fn(), useCampaignMessages: vi.fn(),
}));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn().mockResolvedValue(true) }) }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

const create = vi.fn();

describe('CampaignsTab scheduled send', () => {
  // The business is in Baja California (UTC-7 in October); the device is on Mexico City time (UTC-6).
  beforeEach(() => {
    vi.stubEnv('TZ', 'America/Mexico_City');
    setBusinessTimeZone('America/Tijuana');
    create.mockReset().mockResolvedValue({});
    vi.mocked(useCampaigns).mockReturnValue({ data: { items: [] }, isPending: false, error: null } as never);
    vi.mocked(useCancelCampaign).mockReturnValue({ mutateAsync: vi.fn() } as never);
    vi.mocked(useCreateCampaign).mockReturnValue({ mutateAsync: create } as never);
    vi.mocked(messagingApi.getTemplates).mockResolvedValue({ data: [{ id: 't1', name: 'Promoción' }] } as never);
  });

  afterEach(() => {
    setBusinessTimeZone(null);
    vi.unstubAllEnvs();
    vi.useRealTimers();
  });

  it('proposes an hour from now on the clock of the business and schedules the hour typed as its clock', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    // 17:30 UTC is 10:30 in Tijuana (11:30 on the device).
    vi.setSystemTime(new Date('2026-10-08T17:30:00.000Z'));
    const user = userEvent.setup();
    render(<CampaignsTab onCreateTemplate={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
    const form = within(await screen.findByRole('dialog', { name: 'Nueva campaña' }));
    await user.type(form.getByLabelText('Nombre'), 'Fin de semana');
    await user.click(form.getByLabelText('Programar'));
    expect(form.getByLabelText('Fecha y hora de envío')).toHaveValue('2026-10-08T11:30');

    fireEvent.change(form.getByLabelText('Fecha y hora de envío'), { target: { value: '2026-10-09T18:00' } });
    await user.click(form.getByRole('button', { name: 'Programar' }));

    await waitFor(() => expect(create).toHaveBeenCalledTimes(1));
    expect(create.mock.calls[0][0].scheduledAt).toBe('2026-10-10T01:00:00.000Z');
  });

  it('schedules the hour it proposed when it falls in the hour the clock repeats', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    // 08:30 UTC on 1 November is 01:30 PDT in Tijuana; an hour later, 01:30 PST, the second time 01:30 comes.
    vi.setSystemTime(new Date('2026-11-01T08:30:00.000Z'));
    const user = userEvent.setup();
    render(<CampaignsTab onCreateTemplate={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Nueva campaña' }));
    const form = within(await screen.findByRole('dialog', { name: 'Nueva campaña' }));
    await user.type(form.getByLabelText('Nombre'), 'Fin de semana');
    await user.click(form.getByLabelText('Programar'));
    expect(form.getByLabelText('Fecha y hora de envío')).toHaveValue('2026-11-01T01:30');
    await user.click(form.getByRole('button', { name: 'Programar' }));

    await waitFor(() => expect(create).toHaveBeenCalledTimes(1));
    expect(create.mock.calls[0][0].scheduledAt).toBe('2026-11-01T09:30:00.000Z');
  });
});
