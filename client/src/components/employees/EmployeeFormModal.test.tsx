import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability } from '../../types';
import type { EmployeeRecord, EmployeeRolePreset } from '../../utils/teamRoles';
import EmployeeFormModal from './EmployeeFormModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

const PRESETS: EmployeeRolePreset[] = [
  { key: 'manager', name: 'Gerente', description: 'Todo', appRole: 1, tenantRole: 1, permissions: [] },
  { key: 'reception', name: 'Recepción', description: 'La agenda', appRole: 2, tenantRole: 2, permissions: ['ManageAppointments'] },
  { key: 'cashier', name: 'Cajero', description: 'Vender', appRole: 2, tenantRole: 2, permissions: ['ManageOrders'] },
];

const OPTIONS = [
  { code: 'ManageAppointments', name: 'Agenda', description: 'Citas' },
  { code: 'ManageOrders', name: 'Pedidos', description: 'Vender' },
];

function member(overrides: Partial<EmployeeRecord> = {}): EmployeeRecord {
  return {
    tenantUserId: 'm-1',
    userId: 'u-1',
    email: 'recepcion@demo.com',
    firstName: 'Lupita',
    lastName: 'Ruiz',
    appRole: 'Staff',
    tenantRole: 'Staff',
    status: 'Active',
    isActive: true,
    createdAt: '2026-11-01T00:00:00Z',
    permissions: ['ManageAppointments'],
    noticeEmails: { requests: false, quoteReplies: false, emailConfirmed: true },
    ...overrides,
  };
}

function inBusiness(capabilities: number) {
  vi.mocked(useTenant).mockReturnValue({ tenant: null, capabilities, isLoading: false, refreshTenant: vi.fn() } as never);
}

const renderForm = (employee: EmployeeRecord | null, permissionOptions = OPTIONS) =>
  render(
    <EmployeeFormModal employee={employee} presets={PRESETS} permissionOptions={permissionOptions} onClose={vi.fn()} onSaved={vi.fn()} />,
  );

/** The body of the save the form sent. */
function sentBody() {
  const call = vi.mocked(apiRequest).mock.calls.at(-1)!;
  return (call[2] as unknown as { body: Record<string, unknown> }).body;
}

describe('EmployeeFormModal: the notices by email of each person (Fase 29Q)', () => {
  beforeEach(() => {
    vi.mocked(apiRequest).mockReset();
    vi.mocked(apiRequest).mockResolvedValue(member() as never);
    inBusiness(TenantCapability.Service | TenantCapability.Appointments);
  });

  it('lets whoever runs the team turn on the agenda notices of a person, and sends what they chose', async () => {
    const user = userEvent.setup();
    renderForm(member());

    const agenda = screen.getByRole('switch', { name: 'Citas en línea' });
    const quotes = screen.getByRole('switch', { name: 'Cotizaciones' });
    expect(agenda).not.toBeChecked();
    expect(agenda).toHaveAccessibleDescription('Cada solicitud de cita o de visita y cada cita que un cliente cancela.');
    expect(screen.getByText(/Le llegan a recepcion@demo\.com\. También los puede cambiar en Mi perfil\./)).toBeInTheDocument();

    await user.click(agenda);
    // Without the orders' permission, the quotes' notice would not reach her.
    await user.click(quotes);
    expect(screen.getByText('Sin el permiso «Pedidos» no le llegan.')).toBeInTheDocument();
    expect(screen.queryByText('Sin el permiso «Agenda» no le llegan.')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(apiRequest).toHaveBeenCalled());
    expect(sentBody()).toMatchObject({ tenantUserId: 'm-1', requestEmails: true, quoteReplyEmails: true });
  });

  it('shows only the notices the business has', async () => {
    inBusiness(TenantCapability.Service);
    renderForm(member({ noticeEmails: { requests: false, quoteReplies: true, emailConfirmed: false } }));

    expect(screen.queryByRole('switch', { name: 'Citas en línea' })).not.toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Cotizaciones' })).toBeChecked();
    expect(screen.getByText(/cuando confirme su correo/)).toBeInTheDocument();
  });

  it('sends only the notices changed here, never undoing what the person chose after the list was read', async () => {
    const user = userEvent.setup();
    renderForm(member({ noticeEmails: { requests: true, quoteReplies: false, emailConfirmed: true } }));

    // Another permission, and a notice turned off and on again: neither notice changed.
    await user.click(screen.getByRole('radio', { name: /Cajero/ }));
    await user.click(screen.getByRole('switch', { name: 'Cotizaciones' }));
    await user.click(screen.getByRole('switch', { name: 'Cotizaciones' }));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(apiRequest).toHaveBeenCalled());
    expect(sentBody()).toMatchObject({ permissions: ['ManageOrders'], requestEmails: null, quoteReplyEmails: null });
  });

  it('names the permission a notice needs only once the names have loaded', async () => {
    const user = userEvent.setup();
    renderForm(member({ permissions: [] }), []);

    await user.click(screen.getByRole('switch', { name: 'Citas en línea' }));

    expect(screen.queryByText(/Sin el permiso/)).not.toBeInTheDocument();
  });

  it('never chooses the owner\'s: they choose them in Mi perfil', async () => {
    const user = userEvent.setup();
    renderForm(member({ tenantRole: 'Owner', appRole: 'TenantAdmin', noticeEmails: { requests: true, quoteReplies: true, emailConfirmed: true } }));

    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Sus avisos por correo los elige en Mi perfil.');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(apiRequest).toHaveBeenCalled());
    expect(sentBody()).not.toHaveProperty('requestEmails');
    expect(sentBody()).not.toHaveProperty('quoteReplyEmails');
  });

  it('invites someone with the notices chosen, off until someone turns them on', async () => {
    const user = userEvent.setup();
    renderForm(null);

    await user.type(screen.getByLabelText('Nombre'), 'Lupita');
    await user.type(screen.getByLabelText('Apellido'), 'Ruiz');
    await user.type(screen.getByLabelText('Correo'), 'recepcion@demo.com');
    expect(screen.getByText(/Le llegan a recepcion@demo\.com en cuanto su correo esté confirmado\./)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Citas en línea' })).not.toBeChecked();
    await user.click(screen.getByRole('switch', { name: 'Citas en línea' }));
    await user.click(screen.getByRole('button', { name: 'Invitar' }));

    await waitFor(() => expect(apiRequest).toHaveBeenCalled());
    expect(vi.mocked(apiRequest).mock.calls.at(-1)![0]).toBe('post');
    expect(sentBody()).toMatchObject({ email: 'recepcion@demo.com', requestEmails: true, quoteReplyEmails: null });
  });
});
