import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { at, uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

/**
 * What does not add up at Café La Esquina (Fase 27C): someone loaded 2 L of milk in Roma Norte without a movement after
 * this morning's sale, and between two movements of last week 0.25 kg of coffee went missing from Roma Norte without
 * one. The other 22 stocks are what their movements add up to.
 */
function withGaps(data: DemoData) {
  const supplyOf = (code: string) => data.supplies.find((s: { code: string }) => s.code === code);
  const locationOf = (code: string) => data.locations.find((l: { code: string }) => l.code === code);
  const roma = locationOf('ROMA');
  const item = (code: string, onHand: number, history: number, movementCount: number) => {
    const supply = supplyOf(code);
    return {
      supplyItemId: supply.id,
      supplyItemName: supply.name,
      unit: supply.unit,
      isActive: true,
      inventoryLocationId: roma.id,
      inventoryLocationName: roma.name,
      onHandQuantity: onHand,
      historyQuantity: history,
      difference: Math.round((onHand - history) * 10000) / 10000,
      movementCount,
    };
  };

  const coffee = item('INS-001', 8.5, 8.75, 31);
  const milk = item('INS-002', 14, 12, 58);
  data.reconciliation = {
    checkedCount: 24,
    items: [coffee, milk],
    details: {
      [`${coffee.supplyItemId}:${roma.id}`]: {
        ...coffee,
        reviewedCount: 31,
        eventCount: 1,
        events: [{
          kind: 3, quantity: -0.25, quantityFrom: 10.25, quantityTo: 10,
          movementId: uuid('mov:INV-20261001-0004'), movementNumber: 'INV-20261001-0004', movementDate: at(9, 15, -3),
          previousMovementId: uuid('mov:INV-20260930-0007'), previousMovementNumber: 'INV-20260930-0007', previousMovementDate: at(19, 40, -4),
        }],
      },
      [`${milk.supplyItemId}:${roma.id}`]: {
        ...milk,
        reviewedCount: 58,
        eventCount: 1,
        events: [{
          kind: 5, quantity: 2, quantityFrom: 12, quantityTo: 14,
          movementId: uuid('mov:INV-20261004-0003'), movementNumber: 'INV-20261004-0003', movementDate: at(12, 55),
        }],
      },
    },
  };
}

export const CON_DIFERENCIAS = asOption(withGaps);

/** The review endpoints over `data.reconciliation`, as InventoryReconciliationController answers them. */
const reconciliationRoutes: ExtraRoutes = (data, { ok, paginate }) => {
  const notFound = { status: 404, json: { code: 'not_found', detail: 'No se encontró el registro. Es posible que ya no exista.' } };
  return [
    ['GET', /^\/inventory\/reconciliation$/, (_m, q) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      let rows = data.reconciliation.items as any[];
      const location = q.get('inventoryLocationId');
      if (location) rows = rows.filter((i) => i.inventoryLocationId === location);
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((i) => [i.supplyItemName, i.inventoryLocationName].some((v: string) => v.toLowerCase().includes(search)));
      return ok({ checkedCount: data.reconciliation.checkedCount, mismatches: paginate(rows, q, 25) });
    }],
    ['GET', /^\/inventory\/reconciliation\/([^/]+)\/([^/]+)$/, (m) => {
      const detail = data.reconciliation.details[`${m[1]}:${m[2]}`];
      return detail ? ok(detail) : notFound;
    }],
  ];
};

export const RECONCILIATION_ROUTES = asOption(reconciliationRoutes);
