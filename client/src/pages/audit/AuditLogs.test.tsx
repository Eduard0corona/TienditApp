import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { auditApi } from '../../api/auditApi';
import type { AuditLogDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import AuditLogs from './AuditLogs';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/auditApi', () => ({ auditApi: { getLogs: vi.fn() } }));

const getLogs = vi.mocked(auditApi.getLogs);

const page = (items: AuditLogDto[], totalCount = items.length) => ({
  data: {
    items, pageNumber: 1, totalPages: Math.max(1, Math.ceil(totalCount / 20)), totalCount, hasPreviousPage: false,
    hasNextPage: totalCount > 20,
  },
}) as never;

function deferred() {
  let resolve: (value: unknown) => void = () => undefined;
  let reject: (reason: unknown) => void = () => undefined;
  const promise = new Promise((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** An invoice asked for by Mariana, and the permissions of Carlos that she changed. */
const INVOICE: AuditLogDto = {
  id: 'a1', entityName: 'OrderInvoice', entityId: '7c1e0d52-2b0e-4a55-9a43-1d1f0c8e6f10', action: 'OrderInvoiceRequested',
  performedBy: 'mariana@cafe.mx', timestamp: '2026-10-09T15:04:05Z',
};
const PERMISSIONS: AuditLogDto = {
  id: 'a2', entityName: 'TenantUser', entityId: '3f2a9b1c-6d4e-4f8a-b2c1-9e8d7f6a5b4c', action: 'PermissionsChanged',
  performedBy: 'mariana@cafe.mx', timestamp: '2026-10-08T23:30:00Z',
};

/** The parameters of the last reading. */
const lastQuery = () => getLogs.mock.calls.at(-1)?.[0];
/** Waits past the pause after typing in Entidad or Acción. */
const pause = () => act(() => new Promise((resolve) => setTimeout(resolve, 350)));

describe('AuditLogs', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    getLogs.mockResolvedValue(page([INVOICE, PERMISSIONS]));
  });

  afterEach(() => setBusinessTimeZone(null));

  it('lists who changed what, with the date and time of the business', async () => {
    setBusinessTimeZone('America/Cancun');
    render(<AuditLogs />);

    const row = (await screen.findByText('OrderInvoiceRequested')).closest('tr') as HTMLElement;
    // Cancún is UTC-5: 15:04:05 UTC is 10:04:05 there.
    expect(within(row).getAllByRole('cell').map((cell) => cell.textContent)).toEqual([
      expect.stringMatching(/^09\/10\/2026, 10:04:05/), 'OrderInvoice', 'OrderInvoiceRequested', INVOICE.entityId, 'mariana@cafe.mx',
    ]);
    // 23:30 UTC of the 8th is still the 8th in Cancún.
    expect(within(screen.getByText('PermissionsChanged').closest('tr') as HTMLElement).getAllByRole('cell')[0])
      .toHaveTextContent(/^08\/10\/2026, 06:30:00/);
    expect(getLogs).toHaveBeenCalledWith({
      entityName: undefined, action: undefined, from: undefined, to: undefined, search: undefined, pageNumber: 1, pageSize: 20,
    });
  });

  it('asks for the entity and the action once typing stops, whole, and from the first page', async () => {
    const user = userEvent.setup();
    getLogs.mockResolvedValue(page([INVOICE, PERMISSIONS], 45));
    render(<AuditLogs />);
    await screen.findByText('OrderInvoiceRequested');
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ pageNumber: 2 })));
    const reads = getLogs.mock.calls.length;

    // The server matches them whole: the field says so.
    expect(screen.getByLabelText('Entidad')).toHaveAttribute('placeholder', 'Nombre exacto');
    expect(screen.getByLabelText('Acción')).toHaveAttribute('placeholder', 'Nombre exacto');
    await user.type(screen.getByLabelText('Entidad'), ' OrderInvoice ');
    expect(getLogs).toHaveBeenCalledTimes(reads);
    await pause();
    expect(getLogs).toHaveBeenCalledTimes(reads + 1);
    expect(lastQuery()).toEqual(expect.objectContaining({ entityName: 'OrderInvoice', action: undefined, pageNumber: 1 }));

    await user.type(screen.getByLabelText('Acción'), 'OrderInvoiceRequested');
    await pause();
    expect(getLogs).toHaveBeenCalledTimes(reads + 2);
    expect(lastQuery()).toEqual(expect.objectContaining({ entityName: 'OrderInvoice', action: 'OrderInvoiceRequested', pageNumber: 1 }));

    // Typed and erased before the pause: nothing is asked for, and the page stays.
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ pageNumber: 2 })));
    await user.type(screen.getByLabelText('Acción'), 'X');
    await user.type(screen.getByLabelText('Acción'), '{Backspace}');
    await pause();
    expect(getLogs).toHaveBeenCalledTimes(reads + 3);
    expect(lastQuery()).toEqual(expect.objectContaining({ pageNumber: 2 }));
  });

  it('asks for the days of the business and the search, and clears every filter at once, from the first page', async () => {
    const user = userEvent.setup();
    setBusinessTimeZone('America/Cancun');
    getLogs.mockResolvedValue(page([INVOICE, PERMISSIONS], 45));
    render(<AuditLogs />);
    await screen.findByText('OrderInvoiceRequested');

    await user.type(screen.getByLabelText('Desde'), '2026-10-01');
    await user.type(screen.getByLabelText('Hasta'), '2026-10-05');
    // Cancún is UTC-5 all year: its day starts at 05:00 UTC and ends at 04:59:59 UTC of the next.
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({
      from: '2026-10-01T05:00:00.000Z', to: '2026-10-06T04:59:59.000Z', pageNumber: 1,
    })));
    await user.type(screen.getByLabelText('Qué cambió, acción o quién'), 'mariana');
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ search: 'mariana', pageNumber: 1 })));

    await user.type(screen.getByLabelText('Entidad'), 'TenantUser');
    await user.type(screen.getByLabelText('Acción'), ' PermissionsChanged ');
    await pause();
    expect(lastQuery()).toEqual(expect.objectContaining({ entityName: 'TenantUser', action: 'PermissionsChanged' }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ pageNumber: 2 })));
    const reads = getLogs.mock.calls.length;

    await user.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    // One reading, with every filter cleared and from the first page; nothing typed comes back after the pause.
    await waitFor(() => expect(getLogs).toHaveBeenCalledTimes(reads + 1));
    expect(lastQuery()).toEqual({
      entityName: undefined, action: undefined, from: undefined, to: undefined, search: 'mariana', pageNumber: 1, pageSize: 20,
    });
    await pause();
    expect(getLogs).toHaveBeenCalledTimes(reads + 1);
    for (const field of ['Entidad', 'Acción', 'Desde', 'Hasta']) expect(screen.getByLabelText(field)).toHaveValue('');
  });

  it('counts as filters what is typed, not spaces, and shows 20 per page or more', async () => {
    const user = userEvent.setup();
    getLogs.mockResolvedValue(page([INVOICE, PERMISSIONS], 45));
    render(<AuditLogs />);
    await screen.findByText('OrderInvoiceRequested');

    await user.type(screen.getByLabelText('Entidad'), '   ');
    expect(screen.getByRole('button', { name: 'Filtros' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Acción'), 'X');
    expect(screen.getByRole('button', { name: 'Filtros (1)' })).toBeInTheDocument();

    const perPage = screen.getByLabelText('Por página');
    expect(perPage).toHaveDisplayValue('20');
    expect(within(perPage).getAllByRole('option').map((option) => option.textContent)).toEqual(['20', '50', '100']);
    await user.selectOptions(perPage, '50');
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ pageSize: 50, pageNumber: 1 })));
  });

  it('shows only the last reading asked for, and says nothing of one left behind', async () => {
    const user = userEvent.setup();
    const forEntity = deferred();
    const forAction = deferred();
    const lateAgain = deferred();
    const lateFailure = deferred();
    getLogs
      .mockResolvedValueOnce(page([INVOICE, PERMISSIONS]))
      .mockReturnValueOnce(forEntity.promise as never)
      .mockReturnValueOnce(forAction.promise as never)
      .mockReturnValueOnce(lateAgain.promise as never)
      .mockResolvedValueOnce(page([PERMISSIONS]))
      .mockReturnValueOnce(lateFailure.promise as never)
      .mockResolvedValueOnce(page([PERMISSIONS]));
    render(<AuditLogs />);
    await screen.findByText('OrderInvoiceRequested');

    await user.type(screen.getByLabelText('Entidad'), 'OrderInvoice');
    await pause();
    await user.type(screen.getByLabelText('Acción'), 'OrderInvoiceRequested');
    await pause();
    await act(async () => forEntity.resolve(page([PERMISSIONS])));
    // The reading of the action is still on its way: the earlier one neither shows nor ends the loading.
    expect(screen.queryByText('PermissionsChanged')).not.toBeInTheDocument();
    await act(async () => forAction.resolve(page([INVOICE])));
    expect(await screen.findByText('OrderInvoiceRequested')).toBeInTheDocument();

    // One left behind that answers after the current one arrived does not replace it.
    await user.clear(screen.getByLabelText('Acción'));
    await pause();
    await user.clear(screen.getByLabelText('Entidad'));
    await pause();
    expect(await screen.findByText('PermissionsChanged')).toBeInTheDocument();
    await act(async () => lateAgain.resolve(page([INVOICE])));
    expect(screen.queryByText('OrderInvoiceRequested')).not.toBeInTheDocument();

    // Nor does its error.
    await user.type(screen.getByLabelText('Entidad'), 'TenantUser');
    await pause();
    await user.clear(screen.getByLabelText('Entidad'));
    await pause();
    expect(await screen.findByText('PermissionsChanged')).toBeInTheDocument();
    await act(async () => lateFailure.reject(new Error('Network Error')));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says why the activity could not be read instead of saying there is none, and reads it again', async () => {
    const user = userEvent.setup();
    let answer: (value: unknown) => void = () => undefined;
    getLogs
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 403, data: { error: 'Solo el dueño o un administrador pueden ver la actividad.' } } })
      .mockReturnValueOnce(new Promise((resolve) => { answer = resolve; }) as never)
      .mockResolvedValue(page([]));
    render(<AuditLogs />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Solo el dueño o un administrador pueden ver la actividad.');
    expect(screen.getByText('No se pudo leer la actividad.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    // While it reads again, the notice goes and the list says it is loading.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByText('No se pudo leer la actividad.')).not.toBeInTheDocument();
    expect(getLogs).toHaveBeenCalledTimes(2);
    await act(async () => answer(page([])));

    // Read, the notice goes, and with nothing filtered there is simply nothing yet.
    expect(await screen.findByText('Todavía no hay actividad registrada.')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Acción'), 'PermissionsChanged');
    await pause();
    expect(await screen.findByText('No hay actividad registrada con estos filtros.')).toBeInTheDocument();
  });

  it('does not leave the rows of another page or filter under a reading that failed', async () => {
    const user = userEvent.setup();
    getLogs
      .mockResolvedValueOnce(page([INVOICE, PERMISSIONS], 45))
      .mockRejectedValueOnce(new Error('Network Error'))
      .mockRejectedValueOnce(new Error('Network Error'))
      .mockResolvedValue(page([PERMISSIONS], 45));
    render(<AuditLogs />);
    await screen.findByText('OrderInvoiceRequested');

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo cargar la actividad del negocio.');
    expect(screen.queryByText('OrderInvoiceRequested')).not.toBeInTheDocument();
    expect(screen.getByText('No se pudo leer la actividad.')).toBeInTheDocument();

    // With something searched it still says it could not be read, not that the search found nothing.
    await user.type(screen.getByLabelText('Qué cambió, acción o quién'), 'mariana');
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ search: 'mariana' })));
    expect(await screen.findByText('No se pudo leer la actividad.')).toBeInTheDocument();
    expect(screen.queryByText('No se encontraron resultados.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cerrar' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    await user.clear(screen.getByLabelText('Qué cambió, acción o quién'));
    expect(await screen.findByText('PermissionsChanged')).toBeInTheDocument();
  });
});
