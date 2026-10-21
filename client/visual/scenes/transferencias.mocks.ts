import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { at, uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

const OWNER = 'mariana.ortiz@cafelaesquina.mx';
const CASHIER = 'carlos.mendez@cafelaesquina.mx';

export const TODAY_TRANSFER = 'TRF-20261004-0001';
export const YESTERDAY_TRANSFER = 'TRF-20261003-0001';

/** Cents, as the server rounds them. */
const r2 = (amount: number) => Math.round(amount * 100) / 100;

/**
 * The transfers of Café La Esquina (Fase 27B): this morning the owner brought cups and chocolate from Bodega Central to
 * Roma Norte, and yesterday Carlos took coffee and milk from Roma Norte to Coyoacán for the weekend. Each item is worth
 * the same leaving and entering: its average cost, else its last one, else its reference cost.
 */
function withTransfers(data: DemoData) {
  const supplyOf = (code: string) => data.supplies.find((s: { code: string }) => s.code === code);
  const locationOf = (code: string) => data.locations.find((l: { code: string }) => l.code === code);

  const line = (code: string, quantity: number, from: number, to: number) => {
    const supply = supplyOf(code);
    const unitCost = supply.averageCost || supply.lastCost || supply.defaultCost || null;
    return {
      supplyItemId: supply.id,
      supplyItemName: supply.name,
      unit: supply.unit,
      quantity,
      unitCost,
      totalCost: unitCost === null ? null : r2(unitCost * quantity),
      fromQuantityBefore: r2(from + quantity),
      fromQuantityAfter: from,
      toQuantityBefore: r2(to - quantity),
      toQuantityAfter: to,
    };
  };

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const transfer = (number: string, fromCode: string, toCode: string, lines: any[], extra: Record<string, unknown>) => {
    const from = locationOf(fromCode);
    const to = locationOf(toCode);
    return {
      id: uuid(`transfer:${number}`),
      transferNumber: number,
      fromLocationId: from.id,
      fromLocationName: from.name,
      toLocationId: to.id,
      toLocationName: to.name,
      lineCount: lines.length,
      totalCost: r2(lines.reduce((sum, l) => sum + (l.totalCost ?? 0), 0)),
      notes: null,
      lines: [...lines].sort((a, b) => a.supplyItemName.localeCompare(b.supplyItemName, 'es')),
      ...extra,
    };
  };

  data.inventoryTransfers = [
    transfer(TODAY_TRANSFER, 'BOD', 'ROMA', [line('INS-009', 2, 7, 3), line('INS-008', 1, 5, 1)], {
      transferredAt: at(10, 30), transferredBy: OWNER, outMovementId: uuid('mov:INV-20261004-0005'), outMovementNumber: 'INV-20261004-0005',
      inMovementId: uuid('mov:INV-20261004-0006'), inMovementNumber: 'INV-20261004-0006',
    }),
    transfer(YESTERDAY_TRANSFER, 'ROMA', 'COY', [line('INS-001', 4, 8.5, 4), line('INS-002', 6, 14, 4)], {
      transferredAt: at(18, 10, -1), transferredBy: CASHIER, notes: 'Para el fin de semana',
      outMovementId: uuid('mov:INV-20261003-0003'), outMovementNumber: 'INV-20261003-0003',
      inMovementId: uuid('mov:INV-20261003-0004'), inMovementNumber: 'INV-20261003-0004',
    }),
  ];
}

export const CON_TRANSFERENCIAS = asOption(withTransfers);

/** The transfers endpoints over `data.inventoryTransfers`, as InventoryTransfersController answers them. */
const transferRoutes: ExtraRoutes = (data, { ok, paginate }) => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const transfers = () => data.inventoryTransfers as any[];
  const notFound = { status: 404, json: { code: 'not_found', detail: 'No se encontró el registro. Es posible que ya no exista.' } };
  return [
    ['GET', /^\/inventory\/transfers$/, (_m, q) => {
      // eslint-disable-next-line @typescript-eslint/no-unused-vars
      let rows = transfers().map(({ lines, notes, outMovementId, outMovementNumber, inMovementId, inMovementNumber, ...item }) => item);
      const location = q.get('inventoryLocationId');
      if (location) rows = rows.filter((t) => t.fromLocationId === location || t.toLocationId === location);
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) {
        rows = rows.filter((t) => [t.transferNumber, t.fromLocationName, t.toLocationName].some((v: string) => v.toLowerCase().includes(search)));
      }
      return ok(paginate(rows, q, 20));
    }],
    ['GET', /^\/inventory\/transfers\/([^/]+)$/, (m) => {
      const transfer = transfers().find((t) => t.id === m[1]);
      return transfer ? ok(transfer) : notFound;
    }],
  ];
};

export const TRANSFER_ROUTES = asOption(transferRoutes);
