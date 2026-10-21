import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { at, uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// InventoryCountStatus as the contract numbers it.
const IN_PROGRESS = 1;
const POSTED = 2;
const CANCELLED = 3;

const OWNER = 'mariana.ortiz@cafelaesquina.mx';
const CASHIER = 'carlos.mendez@cafelaesquina.mx';

export const OPEN_COUNT = 'CNT-20261004-0001';
export const POSTED_COUNT = 'CNT-20261002-0001';
export const CANCELLED_COUNT = 'CNT-20261001-0001';

/** Cents, as the server rounds them. */
const r2 = (amount: number) => Math.round(amount * 100) / 100;

interface CountLine {
  id: string;
  supplyItemId: string;
  countedQuantity: number | null;
  systemQuantity: number | null;
  /** What the location has now (open counts) or had when the count moved it (posted). */
  currentQuantity: number | null;
  resultingQuantity: number | null;
  countedAt: string | null;
  countedBy: string | null;
  notes: string | null;
}

/**
 * The physical counts of Café La Esquina (Fase 27A): this morning Carlos started counting Roma Norte and has 7 of its 12
 * items (the coffee was sold from after he counted it); on Friday the owner counted the dry goods of Bodega Central and
 * posted it (the half kilo of sugar that was missing is the adjustment INV-20261002-0001); and the count of Coyoacán of
 * Thursday was cancelled because the milk had not arrived yet.
 */
function withCounts(data: DemoData) {
  const supplyOf = (code: string) => data.supplies.find((s: { code: string }) => s.code === code);
  const locationOf = (code: string) => data.locations.find((l: { code: string }) => l.code === code);
  const stockOf = (code: string, location: string) => data.stockRows
    .find((s: { supplyCode: string; inventoryLocationCode: string }) => s.supplyCode === code && s.inventoryLocationCode === location)
    ?.onHandQuantity ?? 0;

  const line = (countKey: string, code: string, location: string, counted?: [number, number, string?], current?: number): CountLine => {
    const supply = supplyOf(code);
    const now = current ?? stockOf(code, location);
    return {
      id: uuid(`count:${countKey}:${code}`),
      supplyItemId: supply.id,
      countedQuantity: counted ? counted[0] : null,
      systemQuantity: counted ? counted[1] : null,
      currentQuantity: now,
      resultingQuantity: counted ? r2(now + counted[0] - counted[1]) : null,
      countedAt: counted ? at(9, 40) : null,
      countedBy: counted ? CASHIER : null,
      notes: counted?.[2] ?? null,
    };
  };

  const roma = locationOf('ROMA');
  const bodega = locationOf('BOD');
  const coyoacan = locationOf('COY');
  // 0.144 kg of coffee went into the americanos sold after Carlos counted it.
  const open = [
    line('roma', 'INS-001', 'ROMA', [8.2, 8.5], r2(8.5 - 0.144)),
    line('roma', 'PAN-003', 'ROMA'),
    line('roma', 'INS-002', 'ROMA', [14, 14]),
    line('roma', 'INS-003', 'ROMA'),
    line('roma', 'INS-004', 'ROMA', [21, 22]),
    line('roma', 'INS-005', 'ROMA', [3.2, 3.2]),
    line('roma', 'INS-006', 'ROMA'),
    line('roma', 'INS-007', 'ROMA', [1.5, 0, 'Una bolsa detrás de los desechables']),
    line('roma', 'INS-009', 'ROMA', [3, 3]),
    line('roma', 'INS-010', 'ROMA'),
    line('roma', 'INS-012', 'ROMA'),
    line('roma', 'INS-013', 'ROMA', [6, 8, 'Dos bolsas rotas']),
  ];
  const posted = [
    line('bodega', 'INS-008', 'BOD', [5, 5]),
    line('bodega', 'INS-009', 'BOD', [7, 7]),
    { ...line('bodega', 'INS-011', 'BOD', [6.5, 7]), currentQuantity: 7, resultingQuantity: 6.5 },
  ].map((item) => ({ ...item, countedBy: OWNER, countedAt: at(18, 40, -2) }));
  const cancelled = [
    line('coyoacan', 'INS-001', 'COY', [4, 4]),
    line('coyoacan', 'INS-002', 'COY'),
  ];
  const decrease = data.movements.find((m: { movementNumber: string }) => m.movementNumber === 'INV-20261002-0001');

  const count = (key: string, number: string, location: { id: string; name: string }, lines: CountLine[], extra: Record<string, unknown>) => ({
    id: uuid(`count:${key}`),
    countNumber: number,
    inventoryLocationId: location.id,
    inventoryLocationName: location.name,
    categoryName: null,
    notes: null,
    postedAt: null,
    postedBy: null,
    cancelledAt: null,
    cancelledBy: null,
    cancelReason: null,
    increaseMovementId: null,
    increaseMovementNumber: null,
    decreaseMovementId: null,
    decreaseMovementNumber: null,
    lineCount: lines.length,
    countedLineCount: lines.filter((l) => l.countedQuantity !== null).length,
    revision: lines.length * 2,
    lines,
    ...extra,
  });

  data.inventoryCounts = [
    count('roma', OPEN_COUNT, roma, open, {
      status: IN_PROGRESS, startedAt: at(9, 10), startedBy: CASHIER, notes: 'Conteo de cierre de semana',
    }),
    count('bodega', POSTED_COUNT, bodega, posted, {
      status: POSTED, categoryName: 'Abarrotes', startedAt: at(18, 30, -2), startedBy: OWNER, postedAt: at(19, 0, -2), postedBy: OWNER,
      decreaseMovementId: decrease?.id ?? null, decreaseMovementNumber: decrease?.movementNumber ?? null,
    }),
    count('coyoacan', CANCELLED_COUNT, coyoacan, cancelled, {
      status: CANCELLED, startedAt: at(8, 0, -3), startedBy: CASHIER, cancelledAt: at(8, 20, -3), cancelledBy: OWNER,
      cancelReason: 'Se contó antes de que llegara la leche',
    }),
  ];
}

export const CON_CONTEOS = asOption(withCounts);

/** What a difference is worth at the item's average cost, as the server values the adjustments of a count. */
function valueOf(data: DemoData, line: CountLine) {
  if (line.countedQuantity === null || line.systemQuantity === null) return null;
  const difference = line.countedQuantity - line.systemQuantity;
  if (difference === 0) return null;
  const supply = data.supplies.find((s: { id: string }) => s.id === line.supplyItemId);
  return r2(difference * (supply.averageCost || supply.lastCost || supply.defaultCost));
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function toCount(data: DemoData, count: any) {
  const valued = count.lines.map((line: CountLine) => valueOf(data, line)).filter((value: number | null) => value !== null);
  const { lines: _lines, ...dto } = count;
  void _lines;
  return {
    ...dto,
    countedLineCount: count.lines.filter((l: CountLine) => l.countedQuantity !== null).length,
    differenceLineCount: valued.length,
    surplusValue: count.status === CANCELLED ? null : r2(valued.filter((v: number) => v > 0).reduce((s: number, v: number) => s + v, 0)),
    shortageValue: count.status === CANCELLED ? null : r2(-valued.filter((v: number) => v < 0).reduce((s: number, v: number) => s + v, 0)),
  };
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function toLine(data: DemoData, count: any, line: CountLine) {
  const supply = data.supplies.find((s: { id: string }) => s.id === line.supplyItemId);
  const difference = line.countedQuantity === null || line.systemQuantity === null ? null : r2(line.countedQuantity - line.systemQuantity);
  const moved = count.status === POSTED && difference !== 0;
  return {
    id: line.id,
    supplyItemId: supply.id,
    supplyItemName: supply.name,
    supplyCode: supply.code ?? null,
    supplySku: supply.sku ?? null,
    supplyBarcode: supply.barcode ?? null,
    categoryName: supply.categoryName ?? null,
    unit: supply.unit,
    isSupplyActive: supply.isActive,
    countedQuantity: line.countedQuantity,
    systemQuantity: line.systemQuantity,
    difference,
    currentQuantity: count.status === IN_PROGRESS || moved ? line.currentQuantity : null,
    resultingQuantity: count.status === IN_PROGRESS || moved ? line.resultingQuantity : null,
    differenceValue: count.status === CANCELLED ? null : valueOf(data, line),
    countedAt: line.countedAt,
    countedBy: line.countedBy,
    notes: line.notes,
  };
}

/** The counts endpoints over `data.inventoryCounts`, as InventoryCountsController answers them. */
const countRoutes: ExtraRoutes = (data, { ok, paginate }) => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const find = (id: string) => (data.inventoryCounts as any[]).find((c) => c.id === id);
  const notFound = { status: 404, json: { code: 'not_found', detail: 'No se encontró el registro. Es posible que ya no exista.' } };
  return [
    ['GET', /^\/inventory\/counts$/, (_m, q) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      let rows = (data.inventoryCounts as any[]).map((c) => toCount(data, c));
      if (q.get('status')) rows = rows.filter((c) => c.status === Number(q.get('status')));
      if (q.get('inventoryLocationId')) rows = rows.filter((c) => c.inventoryLocationId === q.get('inventoryLocationId'));
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((c) => c.countNumber.toLowerCase().includes(search) || c.inventoryLocationName.toLowerCase().includes(search));
      return ok(paginate(rows, q, 20));
    }],
    ['GET', /^\/inventory\/counts\/([^/]+)$/, (m) => {
      const count = find(m[1]);
      return count ? ok(toCount(data, count)) : notFound;
    }],
    ['GET', /^\/inventory\/counts\/([^/]+)\/lines$/, (m, q) => {
      const count = find(m[1]);
      if (!count) return notFound;
      let rows = count.lines.map((line: CountLine) => toLine(data, count, line))
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        .sort((a: any, b: any) => a.supplyItemName.localeCompare(b.supplyItemName, 'es'));
      const search = (q.get('search') ?? '').toLowerCase();
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      if (search) rows = rows.filter((l: any) => [l.supplyItemName, l.supplyCode, l.supplySku, l.supplyBarcode].some((v) => v?.toLowerCase().includes(search)));
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      if (q.get('pending') === 'true') rows = rows.filter((l: any) => l.countedQuantity === null);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      if (q.get('withDifference') === 'true') rows = rows.filter((l: any) => l.difference !== null && l.difference !== 0);
      return ok(paginate(rows, q, 50));
    }],
    ['PUT', /^\/inventory\/counts\/([^/]+)\/lines$/, (m, _q, body) => {
      const count = find(m[1]);
      if (!count) return notFound;
      const saved = (body?.lines ?? []).map((entry: { lineId: string; countedQuantity: number | null }) => {
        const line = count.lines.find((l: CountLine) => l.id === entry.lineId);
        const counted = entry.countedQuantity ?? null;
        Object.assign(line, {
          countedQuantity: counted,
          systemQuantity: counted === null ? null : line.currentQuantity,
          resultingQuantity: counted,
          countedAt: counted === null ? null : at(13, 25),
          countedBy: counted === null ? null : OWNER,
        });
        count.revision += 1;
        return toLine(data, count, line);
      });
      return ok({ count: toCount(data, count), lines: saved });
    }],
  ];
};

export const COUNT_ROUTES = asOption(countRoutes);
