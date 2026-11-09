import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { inventoryApi } from '../../api/inventoryApi';
import { InventoryMovementReason, InventoryMovementType, type InventoryLocationDto, type SupplyItemDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import MovementModal from './MovementModal';

vi.mock('../../api/inventoryApi', () => ({ inventoryApi: { createMovement: vi.fn() } }));

const supplies = [
  // The code goes before the SKU when it has both.
  { id: 'milk', name: 'Leche entera', code: 'LEC-01', sku: 'SKU-LECHE' },
  { id: 'beans', name: 'Café en grano', sku: 'CAF-KG' },
  { id: 'cups', name: 'Vaso 12 oz' },
] as unknown as SupplyItemDto[];
const locations = [
  { id: 'bar', name: 'Barra' },
  { id: 'store', name: 'Almacén' },
] as unknown as InventoryLocationDto[];

function renderModal() {
  const onSaved = vi.fn();
  const onHide = vi.fn();
  render(<MovementModal show supplyOptions={supplies} activeLocations={locations} onHide={onHide} onSaved={onSaved} />);
  return { dialog: screen.getByRole('dialog', { name: 'Registrar movimiento de inventario' }), onSaved, onHide };
}

/** Fills line `index` (0-based) of the movement. */
async function fillLine(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, index: number, line: { supply: string; location: string; quantity: string; cost?: string }) {
  await user.selectOptions(within(dialog).getAllByLabelText('Insumo *')[index], line.supply);
  await user.selectOptions(within(dialog).getAllByLabelText('Ubicación *')[index], line.location);
  const quantity = within(dialog).getAllByLabelText('Cantidad *')[index];
  await user.clear(quantity);
  await user.type(quantity, line.quantity);
  if (line.cost) await user.type(within(dialog).getAllByLabelText('Costo unitario')[index], line.cost);
}

describe('MovementModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => setBusinessTimeZone(null));

  it('offers only the movements made by hand: no transfer legs and no ledger reconciliation', () => {
    const { dialog } = renderModal();

    const types = within(within(dialog).getByLabelText('Tipo *')).getAllByRole('option').map((option) => option.textContent);
    expect(types).toEqual(['Entrada', 'Salida', 'Ajuste (+)', 'Ajuste (-)', 'Devolución producción', 'Devolución proveedor']);
    const reasons = within(within(dialog).getByLabelText('Motivo *')).getAllByRole('option').map((option) => option.textContent);
    expect(reasons).toEqual([
      'Compra', 'Venta', 'Producción', 'Consumo', 'Merma', 'Dañado', 'Conteo físico', 'Carga inicial', 'Devolución', 'Ajuste manual', 'Otro',
    ]);
    // Each supply reads with its code, else its SKU.
    expect(within(within(dialog).getByLabelText('Insumo *')).getByRole('option', { name: 'Leche entera (LEC-01)' })).toBeInTheDocument();
    expect(within(within(dialog).getByLabelText('Insumo *')).getByRole('option', { name: 'Café en grano (CAF-KG)' })).toBeInTheDocument();
    expect(within(within(dialog).getByLabelText('Insumo *')).getByRole('option', { name: 'Vaso 12 oz (-)' })).toBeInTheDocument();
  });

  it('registers an exit for waste with one line per supply, the numbers the API takes', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.createMovement).mockResolvedValue({ data: {} } as never);
    const { dialog, onSaved } = renderModal();

    await user.selectOptions(within(dialog).getByLabelText('Tipo *'), 'Salida');
    await user.selectOptions(within(dialog).getByLabelText('Motivo *'), 'Merma');
    await user.type(within(dialog).getByLabelText('Referencia'), ' MER-0042 ');
    await fillLine(user, dialog, 0, { supply: 'milk', location: 'bar', quantity: '2.5', cost: '24.50' });
    await user.click(within(dialog).getByRole('button', { name: 'Agregar línea' }));
    await fillLine(user, dialog, 1, { supply: 'beans', location: 'store', quantity: '1' });
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));

    expect(inventoryApi.createMovement).toHaveBeenCalledWith(expect.objectContaining({
      movementType: InventoryMovementType.Exit,
      movementReason: InventoryMovementReason.Waste,
      referenceNumber: 'MER-0042',
      movementDate: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/),
      lines: [
        { supplyItemId: 'milk', inventoryLocationId: 'bar', quantity: 2.5, unitCost: 24.5, lotNumber: undefined, expirationDate: undefined, notes: undefined },
        { supplyItemId: 'beans', inventoryLocationId: 'store', quantity: 1, unitCost: undefined, lotNumber: undefined, expirationDate: undefined, notes: undefined },
      ],
    }));
    expect(onSaved).toHaveBeenCalledWith('Movimiento registrado correctamente.');
  });

  it('sends what is written in each field of the movement and of its line', async () => {
    const user = userEvent.setup();
    // Not the zone of the team's machines: an expiration read in the device's zone fails here too.
    setBusinessTimeZone('America/Cancun');
    vi.mocked(inventoryApi.createMovement).mockResolvedValue({ data: {} } as never);
    const { dialog } = renderModal();

    await user.type(within(dialog).getByLabelText('Referencia externa'), 'FAC-A-1182');
    await user.type(within(dialog).getByLabelText('Contraparte'), 'Lácteos del Valle');
    await user.type(within(dialog).getByLabelText('Documento'), 'Remisión 77');
    await user.type(within(dialog).getAllByLabelText('Notas')[0], 'Entrega de la mañana');
    await fillLine(user, dialog, 0, { supply: 'milk', location: 'store', quantity: '24', cost: '22.80' });
    await user.type(within(dialog).getByLabelText('Lote'), 'L-2610');
    await user.type(within(dialog).getByLabelText('Caducidad'), '2026-10-20');
    await user.type(within(dialog).getAllByLabelText('Notas')[1], 'Refrigerar');
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));

    expect(inventoryApi.createMovement).toHaveBeenCalledWith(expect.objectContaining({
      movementType: InventoryMovementType.Entry, externalReference: 'FAC-A-1182', counterpartyName: 'Lácteos del Valle',
      documentNumber: 'Remisión 77', notes: 'Entrega de la mañana',
      lines: [{
        // Cancún is UTC-5 all year: its day starts at 05:00 UTC.
        supplyItemId: 'milk', inventoryLocationId: 'store', quantity: 24, unitCost: 22.8, lotNumber: 'L-2610',
        expirationDate: '2026-10-20T05:00:00.000Z', notes: 'Refrigerar',
      }],
    }));
  });

  it('cannot be sent twice while it is being saved', async () => {
    const user = userEvent.setup();
    let answer: (value: unknown) => void = () => undefined;
    vi.mocked(inventoryApi.createMovement).mockReturnValue(new Promise((resolve) => { answer = resolve; }) as never);
    const { dialog, onSaved } = renderModal();

    await fillLine(user, dialog, 0, { supply: 'milk', location: 'bar', quantity: '1' });
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));
    expect(within(dialog).getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    await user.click(within(dialog).getByRole('button', { name: 'Guardando...' }));
    expect(inventoryApi.createMovement).toHaveBeenCalledTimes(1);

    answer({ data: {} });
    await vi.waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
  });

  it('removes the line asked for and keeps what the others say', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.createMovement).mockResolvedValue({ data: {} } as never);
    const { dialog } = renderModal();

    // A single line cannot be removed.
    expect(within(dialog).queryByRole('button', { name: 'Quitar la línea 1' })).not.toBeInTheDocument();
    await fillLine(user, dialog, 0, { supply: 'milk', location: 'bar', quantity: '3' });
    await user.click(within(dialog).getByRole('button', { name: 'Agregar línea' }));
    await fillLine(user, dialog, 1, { supply: 'beans', location: 'store', quantity: '4' });
    await user.click(within(dialog).getByRole('button', { name: 'Agregar línea' }));
    await fillLine(user, dialog, 2, { supply: 'cups', location: 'bar', quantity: '50' });
    await user.click(within(dialog).getByRole('button', { name: 'Quitar la línea 2' }));

    expect(within(dialog).getAllByLabelText('Insumo *').map((select) => (select as HTMLSelectElement).value)).toEqual(['milk', 'cups']);
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));
    expect(vi.mocked(inventoryApi.createMovement).mock.calls[0][0].lines.map((line) => [line.supplyItemId, line.quantity]))
      .toEqual([['milk', 3], ['cups', 50]]);
  });

  it('says what is missing before sending: the supply and location of a line, or a quantity above 0', async () => {
    const user = userEvent.setup();
    const { dialog, onSaved } = renderModal();

    await user.selectOptions(within(dialog).getByLabelText('Insumo *'), 'milk');
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Línea 1: insumo y ubicación son obligatorios.');

    await user.selectOptions(within(dialog).getByLabelText('Ubicación *'), 'bar');
    await user.clear(within(dialog).getByLabelText('Cantidad *'));
    await user.type(within(dialog).getByLabelText('Cantidad *'), '0');
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Línea 1: cantidad inválida.');
    expect(inventoryApi.createMovement).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('shows what the server answers when it refuses the movement, and lets it be sent again', async () => {
    const user = userEvent.setup();
    vi.mocked(inventoryApi.createMovement).mockRejectedValue({
      isAxiosError: true, response: { status: 400, data: { error: 'No hay existencia suficiente de Leche entera en Barra.' } },
    });
    const { dialog, onSaved } = renderModal();

    await user.selectOptions(within(dialog).getByLabelText('Tipo *'), 'Salida');
    await fillLine(user, dialog, 0, { supply: 'milk', location: 'bar', quantity: '40' });
    await user.click(within(dialog).getByRole('button', { name: 'Registrar movimiento' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('No hay existencia suficiente de Leche entera en Barra.');
    expect(onSaved).not.toHaveBeenCalled();
    expect(within(dialog).getByRole('button', { name: 'Registrar movimiento' })).toBeEnabled();
  });
});
