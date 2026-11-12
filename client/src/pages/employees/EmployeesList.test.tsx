import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { useTenant } from '../../hooks/useTenant';
import type { EmployeeRecord } from '../../utils/teamRoles';
import EmployeesList from './EmployeesList';

const toast = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn() }));
const dialogs = vi.hoisted(() => ({ confirm: vi.fn() }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
// The form and the share of an invitation have their own tests: here, who opens them and what the list does after.
vi.mock('../../components/employees/EmployeeFormModal', () => ({
  default: ({ employee, presets, onSaved, onClose }: {
    employee: { firstName: string } | null; presets: Array<{ name: string }>;
    onSaved: (saved: Record<string, unknown>) => void; onClose: () => void;
  }) => (
    <div role="dialog" aria-label={employee ? `Editar a ${employee.firstName}` : 'Invitar persona'}>
      <p>Roles: {presets.map((preset) => preset.name).join(', ')}</p>
      <button type="button" onClick={() => onSaved({ firstName: 'Rosa', lastName: 'Díaz', email: 'rosa@cafe.mx', phoneNumber: '5522223333', invitationUrl: '/invitation/abc' })}>
        Guardar con invitación
      </button>
      <button type="button" onClick={() => onSaved({ firstName: 'Luis', lastName: 'Peña', email: 'luis@cafe.mx' })}>Guardar sin invitación</button>
      <button type="button" onClick={onClose}>Cerrar formulario</button>
    </div>
  ),
}));
vi.mock('../../components/employees/InvitationShare', () => ({
  default: ({ invitationPath, firstName, email, phone, businessName }: Record<string, string | undefined>) => (
    <p>Invitar a {firstName} ({email}, {phone ?? 'sin teléfono'}) a {businessName} con {invitationPath}</p>
  ),
}));

const presets = [
  { key: 'manager', name: 'Gerente', description: '', appRole: 1, tenantRole: 1, permissions: [] },
  { key: 'cashier', name: 'Cajero', description: '', appRole: 2, tenantRole: 2, permissions: ['ManageOrders', 'ChargeOrders', 'ManageCash'] },
];
const permissionOptions = [
  { code: 'ManageOrders', name: 'Pedidos', description: '' },
  { code: 'ChargeOrders', name: 'Cobrar', description: '' },
  { code: 'ManageCash', name: 'Caja', description: '' },
];

function employee(patch: Partial<EmployeeRecord>): EmployeeRecord {
  return {
    tenantUserId: 't', userId: 'u', email: 'x@cafe.mx', firstName: 'X', lastName: 'Y', appRole: 'Staff', tenantRole: 'Staff',
    status: 'Active', isActive: true, createdAt: '2026-10-01T12:00:00Z', permissions: [],
    noticeEmails: { requests: false, quoteReplies: false, orderCancellations: false, reviews: false, emailConfirmed: true },
    ...patch,
  };
}

/** The owner, a manager, a cashier, someone invited with a role of their own, and someone without access, turned off. */
const team = [
  employee({ tenantUserId: 'ana', firstName: 'Ana', lastName: 'Gil', email: 'ana@cafe.mx', appRole: 'TenantAdmin', tenantRole: 'Owner' }),
  employee({ tenantUserId: 'sofia', firstName: 'Sofía', lastName: 'Luna', email: 'sofia@cafe.mx', appRole: 'TenantAdmin', tenantRole: 'Admin', permissions: ['ManageCash'] }),
  employee({ tenantUserId: 'luis', firstName: 'Luis', lastName: 'Peña', email: 'luis@cafe.mx', phoneNumber: '5511112222', permissions: ['ChargeOrders', 'ManageOrders', 'ManageCash'] }),
  employee({ tenantUserId: 'marta', firstName: 'Marta', lastName: 'Ruiz', email: 'marta@cafe.mx', status: 'Invited', isActive: false, permissions: ['ManageOrders', 'ViewReports'] }),
  employee({ tenantUserId: 'pedro', firstName: 'Pedro', lastName: 'Sol', email: 'pedro@cafe.mx', status: 'Disabled', isActive: false }),
];

type Options = { query?: Record<string, unknown>; path?: Record<string, string>; body?: unknown };
const page = (items: EmployeeRecord[], totalCount = items.length) =>
  ({ items, pageNumber: 1, totalPages: Math.ceil(totalCount / 10), totalCount, hasPreviousPage: false, hasNextPage: totalCount > 10 });

function serve({
  list = () => Promise.resolve(page(team)), roles = () => Promise.resolve(presets), active = () => Promise.resolve({}),
}: {
  list?: () => Promise<unknown>; roles?: () => Promise<unknown>; active?: () => Promise<unknown>;
} = {}) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string) => {
    if (method === 'get' && path === '/api/v1/employees') return list();
    if (method === 'get' && path === '/api/v1/employees/role-presets') return roles();
    if (method === 'get' && path === '/api/v1/employees/permission-options') return Promise.resolve(permissionOptions);
    if (method === 'patch' && path === '/api/v1/employees/{tenantUserId}/active') return active();
    if (method === 'post' && path === '/api/v1/employees/{tenantUserId}/resend-invitation') {
      return Promise.resolve({ invitationUrl: '/invitation/nueva', email: 'marta.ruiz@cafe.mx' });
    }
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
}

const calls = (method: string, path: string) =>
  vi.mocked(apiRequest).mock.calls.filter(([m, p]) => m === method && p === path).map(([, , options]) => options as Options);

function renderTeam() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}><EmployeesList /></QueryClientProvider>);
}

const row = async (name: string) => (await screen.findByText(name)).closest('tr') as HTMLElement;

describe('EmployeesList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useTenant).mockReturnValue({ tenant: { name: 'Café La Esquina' } } as unknown as ReturnType<typeof useTenant>);
    serve();
  });

  it('lists each person with their contact, the role their permissions make, their status and what they may do', async () => {
    renderTeam();

    const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell').slice(0, 4).map((cell) => cell.textContent);
    // The names of the permissions arrive apart: the row waits for them.
    await waitFor(async () => expect(cells(await row('Luis Peña'))).toEqual(['Luis Peñaluis@cafe.mx5511112222', 'Cajero', 'Activo', 'Cobrar, Pedidos, Caja']));
    expect(cells(await row('Ana Gil'))).toEqual(['Ana Gilana@cafe.mx', 'Dueño', 'Activo', 'Todo']);
    // A manager administers the business whatever their permissions say.
    expect(cells(await row('Sofía Luna'))).toEqual(['Sofía Lunasofia@cafe.mx', 'Gerente', 'Activo', 'Todo']);
    // A role that matches no named one is their own; a permission without a name shows its code.
    expect(cells(await row('Marta Ruiz'))).toEqual(['Marta Ruizmarta@cafe.mx', 'Personalizado', 'Invitación pendiente', 'Pedidos, ViewReports']);
    expect(cells(await row('Pedro Sol'))).toEqual(['Pedro Solpedro@cafe.mx', 'Sin acceso', 'Desactivado', 'Ninguno']);
    expect(calls('get', '/api/v1/employees')[0]).toEqual({ query: { pageNumber: 1, pageSize: 10, search: undefined, status: undefined } });
  });

  it('offers to each one only what applies: the owner is never turned off, an invitation is sent again or cancelled', async () => {
    renderTeam();

    const actions = async (name: string) => within(await row(name)).getAllByRole('button').map((button) => button.textContent);
    expect(await actions('Ana Gil')).toEqual(['Editar']);
    expect(await actions('Luis Peña')).toEqual(['Editar', 'Desactivar']);
    expect(await actions('Marta Ruiz')).toEqual(['Invitación', 'Editar', 'Cancelar invitación']);
    expect(await actions('Pedro Sol')).toEqual(['Editar', 'Activar']);
  });

  it('asks the server for the status and what is searched, from the first page', async () => {
    const user = userEvent.setup();
    serve({ list: () => Promise.resolve(page(team, 25)) });
    renderTeam();
    await row('Ana Gil');

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(calls('get', '/api/v1/employees').at(-1)?.query).toMatchObject({ pageNumber: 2 }));
    await user.selectOptions(screen.getByLabelText('Estado'), 'Invited');
    await waitFor(() => expect(calls('get', '/api/v1/employees').at(-1)).toEqual({
      query: { pageNumber: 1, pageSize: 10, search: undefined, status: 'Invited' },
    }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.type(screen.getByLabelText('Nombre, correo o teléfono'), 'marta');
    await waitFor(() => expect(calls('get', '/api/v1/employees').at(-1)).toEqual({
      query: { pageNumber: 1, pageSize: 10, search: 'marta', status: 'Invited' },
    }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(calls('get', '/api/v1/employees').at(-1)?.query).toMatchObject({ pageNumber: 2 }));
    await user.selectOptions(screen.getByLabelText('Por página'), '25');
    await waitFor(() => expect(calls('get', '/api/v1/employees').at(-1)).toEqual({
      query: { pageNumber: 1, pageSize: 25, search: 'marta', status: 'Invited' },
    }));
  });

  it('turns someone off only once confirmed, and turns them on again without asking', async () => {
    const user = userEvent.setup();
    dialogs.confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    let changed: (value: unknown) => void = () => undefined;
    serve({ active: () => new Promise((resolve) => { changed = resolve; }) });
    renderTeam();

    await user.click(within(await row('Luis Peña')).getByRole('button', { name: 'Desactivar a Luis Peña' }));
    expect(dialogs.confirm).toHaveBeenCalledWith({
      title: 'Desactivar acceso', message: 'Luis Peña deja de entrar a este negocio. Su historial se conserva.',
      confirmLabel: 'Desactivar', danger: true,
    });
    expect(calls('patch', '/api/v1/employees/{tenantUserId}/active')).toEqual([]);

    // While the change is on its way, that row waits: it cannot be sent twice.
    await user.click(within(await row('Luis Peña')).getByRole('button', { name: 'Desactivar a Luis Peña' }));
    await waitFor(async () => expect(within(await row('Luis Peña')).getByRole('button', { name: 'Desactivar a Luis Peña' })).toBeDisabled());
    changed({});
    serve();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Luis Peña ya no tiene acceso.'));
    expect(within(await row('Luis Peña')).getByRole('button', { name: 'Desactivar a Luis Peña' })).toBeEnabled();
    expect(calls('patch', '/api/v1/employees/{tenantUserId}/active')).toEqual([{ path: { tenantUserId: 'luis' }, body: { isActive: false } }]);

    await user.click(within(await row('Pedro Sol')).getByRole('button', { name: 'Activar a Pedro Sol' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Pedro Sol puede entrar de nuevo.'));
    expect(calls('patch', '/api/v1/employees/{tenantUserId}/active').at(-1)).toEqual({ path: { tenantUserId: 'pedro' }, body: { isActive: true } });
    expect(dialogs.confirm).toHaveBeenCalledTimes(2);
    // The list is read again after each change.
    expect(calls('get', '/api/v1/employees').length).toBeGreaterThanOrEqual(3);
  });

  it('cancels an invitation once confirmed, and says why when the server refuses', async () => {
    const user = userEvent.setup();
    dialogs.confirm.mockResolvedValue(true);
    vi.mocked(apiRequest).mockImplementation(((method: string, path: string) => {
      if (method === 'get' && path === '/api/v1/employees') return Promise.resolve(page(team));
      if (method === 'get' && path === '/api/v1/employees/role-presets') return Promise.resolve(presets);
      if (method === 'get' && path === '/api/v1/employees/permission-options') return Promise.resolve(permissionOptions);
      return Promise.reject({ isAxiosError: true, response: { status: 409, data: { error: 'La invitación ya fue aceptada.' } } });
    }) as unknown as typeof apiRequest);
    renderTeam();

    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Cancelar invitación de Marta Ruiz' }));
    expect(dialogs.confirm).toHaveBeenCalledWith({
      title: 'Cancelar invitación', message: 'El enlace de Marta Ruiz deja de servir. Puedes volver a invitarle después.',
      confirmLabel: 'Cancelar invitación', danger: true,
    });
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('La invitación ya fue aceptada.'));
    expect(toast.success).not.toHaveBeenCalled();
    expect(within(await row('Marta Ruiz')).getByRole('button', { name: 'Cancelar invitación de Marta Ruiz' })).toBeEnabled();
  });

  it('sends an invitation again and offers to share its new link', async () => {
    const user = userEvent.setup();
    renderTeam();

    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Invitación' }));
    const share = await screen.findByRole('dialog', { name: 'Invitación lista' });
    expect(calls('post', '/api/v1/employees/{tenantUserId}/resend-invitation')).toEqual([{ path: { tenantUserId: 'marta' } }]);
    // The email the invitation went to is the server's.
    expect(share).toHaveTextContent('Invitar a Marta (marta.ruiz@cafe.mx, sin teléfono) a Café La Esquina con /invitation/nueva');
    await user.click(within(share).getByRole('button', { name: 'Listo' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Invitación lista' })).not.toBeInTheDocument());
  });

  it('holds the row while an invitation is being sent, and says why when it could not be', async () => {
    const user = userEvent.setup();
    let refuse: (reason: unknown) => void = () => undefined;
    vi.mocked(apiRequest).mockImplementation(((method: string, path: string) => {
      if (method === 'get' && path === '/api/v1/employees') return Promise.resolve(page(team));
      if (method === 'get' && path === '/api/v1/employees/role-presets') return Promise.resolve(presets);
      if (method === 'get' && path === '/api/v1/employees/permission-options') return Promise.resolve(permissionOptions);
      return new Promise((_, reject) => { refuse = reject; });
    }) as unknown as typeof apiRequest);
    renderTeam();

    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Invitación' }));
    const marta = await row('Marta Ruiz');
    expect(within(marta).getByRole('button', { name: 'Invitación' })).toBeDisabled();
    expect(within(marta).getByRole('button', { name: 'Cancelar invitación de Marta Ruiz' })).toBeDisabled();
    // Only hers: the others may still be changed.
    expect(within(await row('Luis Peña')).getByRole('button', { name: 'Desactivar a Luis Peña' })).toBeEnabled();

    refuse(new Error('Network Error'));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('No se pudo crear la invitación.'));
    expect(within(await row('Marta Ruiz')).getByRole('button', { name: 'Invitación' })).toBeEnabled();
    expect(screen.queryByRole('dialog', { name: 'Invitación lista' })).not.toBeInTheDocument();
  });

  it('closes the form without saving and without reading the team again', async () => {
    const user = userEvent.setup();
    renderTeam();

    await user.click(within(await row('Luis Peña')).getByRole('button', { name: 'Editar a Luis Peña' }));
    const before = calls('get', '/api/v1/employees').length;
    await user.click(within(screen.getByRole('dialog', { name: 'Editar a Luis' })).getByRole('button', { name: 'Cerrar formulario' }));
    expect(screen.queryByRole('dialog', { name: 'Editar a Luis' })).not.toBeInTheDocument();
    expect(calls('get', '/api/v1/employees')).toHaveLength(before);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('invites someone and shares the link, or says it saved who needs no invitation, and reads the team again', async () => {
    const user = userEvent.setup();
    renderTeam();
    await row('Ana Gil');

    await user.click(await screen.findByRole('button', { name: 'Invitar persona' }));
    const form = screen.getByRole('dialog', { name: 'Invitar persona' });
    expect(form).toHaveTextContent('Roles: Gerente, Cajero');
    const before = calls('get', '/api/v1/employees').length;
    await user.click(within(form).getByRole('button', { name: 'Guardar con invitación' }));
    expect(screen.queryByRole('dialog', { name: 'Invitar persona' })).not.toBeInTheDocument();
    expect(await screen.findByRole('dialog', { name: 'Invitación lista' })).toHaveTextContent(
      'Invitar a Rosa (rosa@cafe.mx, 5522223333) a Café La Esquina con /invitation/abc',
    );
    await waitFor(() => expect(calls('get', '/api/v1/employees').length).toBe(before + 1));
    // The link is what is shared: no other message besides it.
    expect(toast.success).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Listo' }));

    await user.click(within(await row('Luis Peña')).getByRole('button', { name: 'Editar a Luis Peña' }));
    await user.click(within(screen.getByRole('dialog', { name: 'Editar a Luis' })).getByRole('button', { name: 'Guardar sin invitación' }));
    expect(toast.success).toHaveBeenCalledWith('Luis Peña: guardado.');
    expect(screen.queryByRole('dialog', { name: 'Invitación lista' })).not.toBeInTheDocument();
  });

  it('waits for the roles before letting anyone be invited or edited', async () => {
    serve({ roles: () => new Promise(() => undefined) });
    renderTeam();

    expect(within(await row('Luis Peña')).getByRole('button', { name: 'Editar a Luis Peña' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Invitar persona' })).toBeDisabled();
  });

  it('says when the team could not be read', async () => {
    serve({ list: () => Promise.reject(new Error('Network Error')) });
    renderTeam();

    expect(await screen.findByText('No se pudo cargar el equipo.')).toBeInTheDocument();
  });
});
