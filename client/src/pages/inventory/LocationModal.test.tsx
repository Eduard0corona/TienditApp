import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { inventoryApi } from '../../api/inventoryApi';
import LocationModal from './LocationModal';

vi.mock('../../api/inventoryApi', () => ({ inventoryApi: { createLocation: vi.fn(), updateLocation: vi.fn() } }));
// The branches it can belong to are read elsewhere; here, none.
vi.mock('../../hooks/useWorkingBranch', () => ({ useActiveBranches: () => ({ data: [] }) }));

async function fill(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement) {
  await user.type(within(dialog).getByLabelText('Nombre *'), 'Bodega norte');
  await user.type(within(dialog).getByLabelText('Código *'), 'BOD-N');
}

describe('LocationModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('cannot be sent twice nor closed while it is being saved', async () => {
    const user = userEvent.setup();
    let answer: (value: unknown) => void = () => undefined;
    vi.mocked(inventoryApi.createLocation).mockReturnValue(new Promise((resolve) => { answer = resolve; }) as never);
    const onHide = vi.fn();
    const onSaved = vi.fn();
    render(<LocationModal show location={null} onHide={onHide} onSaved={onSaved} />);
    const dialog = screen.getByRole('dialog', { name: 'Nueva ubicación' });

    await fill(user, dialog);
    await user.click(within(dialog).getByRole('button', { name: 'Crear ubicación' }));
    expect(within(dialog).getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(onHide).not.toHaveBeenCalled();
    expect(inventoryApi.createLocation).toHaveBeenCalledTimes(1);

    answer({ data: {} });
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith('Ubicación creada correctamente.'));
  });

  it('creates a location once, even with a double click when the server answers at once', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.createLocation).mockResolvedValue({ data: {} } as never);
    // As Inventario does: saving closes the dialog.
    function Harness() {
      const [show, setShow] = useState(true);
      return <LocationModal show={show} location={null} onHide={() => setShow(false)} onSaved={() => setShow(false)} />;
    }
    render(<Harness />);
    const dialog = screen.getByRole('dialog', { name: 'Nueva ubicación' });

    await fill(user, dialog);
    // The second click lands while the dialog fades out.
    await user.dblClick(within(dialog).getByRole('button', { name: 'Crear ubicación' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(inventoryApi.createLocation).toHaveBeenCalledTimes(1);
  });
});
