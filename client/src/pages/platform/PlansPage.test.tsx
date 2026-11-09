import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { platformApi, type Plan } from '../../api/platformApi';
import PlansPage from './PlansPage';

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
vi.mock('../../api/platformApi', () => ({ platformApi: { getPlans: vi.fn(), createPlan: vi.fn(), updatePlan: vi.fn() } }));

const basic: Plan = {
  id: 'basic', code: 'BASICO', name: 'Básico', description: null, monthlyPrice: 199, currency: 'MXN', modules: 1, maxBranches: 1, maxUsers: 3,
  trialDays: 14, isDefaultForNewBusinesses: true, isActive: true, sortOrder: 0, tenantsCount: 4,
};
const pro: Plan = { ...basic, id: 'pro', code: 'PRO', name: 'Pro', monthlyPrice: 499, isDefaultForNewBusinesses: false, sortOrder: 1, tenantsCount: 1 };

function renderPlans() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}><PlansPage /></QueryClientProvider>);
}

describe('PlansPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(platformApi.getPlans).mockResolvedValue([basic, pro]);
  });

  it('keeps the form open while it is saved, and says there why the server did not save', async () => {
    const user = userEvent.setup();
    let refuse: (reason: unknown) => void = () => undefined;
    vi.mocked(platformApi.updatePlan).mockReturnValueOnce(new Promise((_, reject) => { refuse = reject; }) as never);
    renderPlans();

    await user.click(await screen.findByRole('button', { name: 'Editar Pro' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar Pro' });
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plan' }));

    // Neither its buttons, the X nor Escape close it while it is on its way.
    expect(within(dialog).getByRole('button', { name: 'Guardando…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(dialog).toHaveClass('show');

    await act(async () => refuse({ isAxiosError: true, response: { status: 409, data: { code: 'conflict', detail: 'Ya existe un plan con la clave PRO.', error: 'Ya existe un plan con la clave PRO.' } } }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Ya existe un plan con la clave PRO.');
    expect(within(dialog).getByRole('button', { name: 'Guardar plan' })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeEnabled();
  });

  it('holds the next form while it saves, even when the list read after the one before arrives meanwhile', async () => {
    const user = userEvent.setup();
    let listed: (value: Plan[]) => void = () => undefined;
    let created: (value: unknown) => void = () => undefined;
    renderPlans();
    await screen.findByRole('button', { name: 'Editar Pro' });
    // The list read after Pro is saved, and the new plan's save, both arrive late.
    vi.mocked(platformApi.getPlans).mockReturnValue(new Promise((resolve) => { listed = resolve; }));
    vi.mocked(platformApi.updatePlan).mockResolvedValueOnce({} as never);
    vi.mocked(platformApi.createPlan).mockReturnValueOnce(new Promise((resolve) => { created = resolve; }) as never);

    await user.click(screen.getByRole('button', { name: 'Editar Pro' }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Editar Pro' })).getByRole('button', { name: 'Guardar plan' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar Pro' })).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: /Nuevo plan/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo plan' });
    await user.type(within(dialog).getByLabelText('Clave'), 'NEGOCIO');
    await user.type(within(dialog).getByLabelText('Nombre'), 'Negocio');
    await user.type(within(dialog).getByLabelText('Precio mensual (MXN)'), '899');
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plan' }));
    await act(async () => listed([basic, pro, { ...pro, id: 'max', code: 'MAX', name: 'Máximo' }]));

    // Its «Guardando…» stays until its own answer arrives: a second click cannot create the plan twice.
    await screen.findByText('Máximo');
    expect(within(dialog).getByRole('button', { name: 'Guardando…' })).toBeDisabled();
    expect(platformApi.createPlan).toHaveBeenCalledTimes(1);
    await act(async () => created({}));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo plan' })).not.toBeInTheDocument());
  });
});
