import { describe, expect, it } from 'vitest';
import { UnitType } from '../../types';
import { MAX_TRANSFER_LINES, toTransferRequest, type TransferForm, type TransferLineForm } from './transferForm';

const SODA: TransferLineForm = { supplyItemId: 's1', name: 'Refresco de cola 600 ml', unit: UnitType.Unit, onHand: 24, quantity: '10' };
const SUGAR: TransferLineForm = { supplyItemId: 's2', name: 'Azúcar estándar', unit: UnitType.Kg, onHand: 10, quantity: '2,5' };
const FORM: TransferForm = { fromLocationId: 'l1', toLocationId: 'l2', notes: '  Para el fin de semana ', lines: [SODA, SUGAR] };

describe('toTransferRequest', () => {
  it('sends the key of the dialog, both locations, the trimmed notes and each quantity', () => {
    expect(toTransferRequest(FORM, 'k1', 'Bodega')).toEqual({
      request: {
        idempotencyKey: 'k1',
        fromLocationId: 'l1',
        toLocationId: 'l2',
        notes: 'Para el fin de semana',
        lines: [{ supplyItemId: 's1', quantity: 10 }, { supplyItemId: 's2', quantity: 2.5 }],
      },
    });
    expect(toTransferRequest({ ...FORM, notes: '  ' }, 'k1', 'Bodega').request?.notes).toBeNull();
  });

  it.each([
    [{ fromLocationId: '' }, 'Elige de qué ubicación salen los artículos.'],
    [{ toLocationId: '' }, 'Elige a qué ubicación van los artículos.'],
    [{ toLocationId: 'l1' }, 'Elige una ubicación de destino distinta a la de origen.'],
    [{ lines: [] }, 'Agrega al menos un artículo.'],
    [{ lines: Array.from({ length: MAX_TRANSFER_LINES + 1 }, (_, i) => ({ ...SODA, supplyItemId: `s${i}` })) },
      'Una transferencia lleva como máximo 200 artículos.'],
    [{ lines: [{ ...SODA, quantity: '' }] }, 'Escribe cuánto de Refresco de cola 600 ml: más de 0, con máximo 3 decimales y entero si va en piezas.'],
    [{ lines: [{ ...SODA, quantity: '1.5' }] }, 'Escribe cuánto de Refresco de cola 600 ml: más de 0, con máximo 3 decimales y entero si va en piezas.'],
    [{ lines: [{ ...SUGAR, quantity: '0.0005' }] }, 'Escribe cuánto de Azúcar estándar: más de 0, con máximo 3 decimales y entero si va en piezas.'],
    [{ lines: [{ ...SODA, quantity: '25' }] }, 'En Bodega solo hay 24 de Refresco de cola 600 ml.'],
  ] as Array<[Partial<TransferForm>, string]>)('refuses %o: %s', (patch, error) => {
    expect(toTransferRequest({ ...FORM, ...patch }, 'k1', 'Bodega')).toEqual({ error });
  });
});
