import type { PlatformSettlementOrder, PlatformStatementLine, PlatformStatementMatch } from '../../api/platformSettlements';
import { PlatformSettlementAdjustmentKind, PlatformStatementMatchStatus } from '../../types';
import { toCents } from '../../utils/money';
import {
  MAX_ADJUSTMENT_AMOUNT,
  MAX_ADJUSTMENTS,
  MAX_DEPOSITED_AMOUNT,
  MAX_DESCRIPTION_LENGTH,
  MAX_NOTES_LENGTH,
  centsToText,
  newAdjustmentKey,
  type AdjustmentRow,
  type SettlementForm,
} from './settlementEdits';

// Importar el estado de cuenta (Fase 22H, ADR 0040): the file of a platform read in the browser, line by line, and what the
// comparison of the API proposes for the settlement. The person reviews it before saving, and the server checks it again.

/** The limits of the API (MatchPlatformStatementQuery, and PlatformOrderId for a folio or the long id of an order). */
export const MAX_STATEMENT_LINES = 5_000;
export const MAX_FOLIO_INPUT_LENGTH = 100;
export const MAX_CONCEPT_LENGTH = 200;

export const TOO_MANY_LINES_MESSAGE = 'Se importan hasta 5,000 renglones a la vez.';
export const CHOOSE_COLUMNS_MESSAGE = 'Elige la columna del folio o ID del pedido y la del importe.';
export const NO_LINES_MESSAGE = 'El archivo no tiene renglones con importe.';

/** How the adjustments of a statement begin: importing another one replaces them. */
export const STATEMENT_PREFIX = 'Estado de cuenta: ';

/** The columns of the file, by position (-1 when it has none). */
export interface StatementColumns {
  folio: number;
  amount: number;
  concept: number;
}

/** A header in lower case, without accents or repeated spaces, to compare. */
export function normalizeHeader(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\s+/g, ' ').trim();
}

// The words the platforms' files use, the most telling first.
const FOLIO_HEADERS = ['folio', 'id del pedido', 'id de pedido', 'numero de pedido', 'no. de pedido', 'id de la orden', 'numero de orden',
  'order id', 'pedido', 'orden', 'order'];
const AMOUNT_HEADERS = ['neto', 'importe neto', 'monto neto', 'total neto', 'total a pagar', 'a pagar', 'importe', 'monto', 'net payout',
  'payout', 'total', 'amount'];
const CONCEPT_HEADERS = ['concepto', 'descripcion', 'detalle', 'tipo', 'description', 'type'];

function findColumn(headers: string[], candidates: string[], taken: number[]): number {
  const normalized = headers.map(normalizeHeader);
  for (const candidate of candidates) {
    const exact = normalized.findIndex((header, index) => header === candidate && !taken.includes(index));
    if (exact >= 0) return exact;
  }
  for (const candidate of candidates) {
    const partial = normalized.findIndex((header, index) => header.includes(candidate) && !taken.includes(index));
    if (partial >= 0) return partial;
  }
  return -1;
}

/** The columns a header row suggests: the person may choose others. */
export function guessColumns(headers: string[]): StatementColumns {
  const folio = findColumn(headers, FOLIO_HEADERS, []);
  const amount = findColumn(headers, AMOUNT_HEADERS, [folio]);
  const concept = findColumn(headers, CONCEPT_HEADERS, [folio, amount]);
  return { folio, amount, concept };
}

/** The columns remembered of the platform's last file, by their headers, if this file has them. */
export function rememberedColumns(headers: string[], remembered: { folio: string; amount: string; concept: string } | null): StatementColumns | null {
  if (!remembered) return null;
  const normalized = headers.map(normalizeHeader);
  const at = (name: string) => (name === '' ? -1 : normalized.indexOf(normalizeHeader(name)));
  const columns = { folio: at(remembered.folio), amount: at(remembered.amount), concept: at(remembered.concept) };
  return columns.folio >= 0 && columns.amount >= 0 ? columns : null;
}

/** The decimal separator of a column of amounts: the one its values use with one or two decimals, if only one does. */
export type DecimalSeparator = ',' | '.';

/** What a column of amounts uses as decimal separator: «1,234» is 1234 in one file and unreadable in another. */
export function decimalSeparatorOf(texts: string[]): DecimalSeparator | undefined {
  let comma = false;
  let dot = false;
  for (const text of texts) {
    const digits = text.replace(/[^\d.,]/g, '');
    if (/\d,\d{1,2}$/.test(digits)) comma = true;
    if (/\d\.\d{1,2}$/.test(digits)) dot = true;
  }
  return comma === dot ? undefined : comma ? ',' : '.';
}

/**
 * An amount as a statement writes it: «$1,234.56», «-1,234.56», «(1,234.56)», «+12», «$-12», «1.234,56», «12,5»,
 * «MX$12.00» or «1234.5 MXN». With the decimal separator of its column, a lone «1,234» or «1.234» is read by it; without
 * it, three digits after the separator group thousands. Undefined when it is not one, when it has more than two decimals
 * or when it goes past what a deposit holds.
 */
export function parseStatementAmount(text: string, decimal?: DecimalSeparator): number | undefined {
  let value = text.replace(/\s/g, '').replace(/MXN/gi, '').replace(/MX\$/gi, '$');
  if (value === '') return undefined;
  let negative = false;
  let parenthesized = false;
  if (/^\(.*\)$/.test(value)) {
    negative = true;
    parenthesized = true;
    value = value.slice(1, -1);
  }
  // A sign and a $ sign, in either order, before the digits; or a sign after them («80-»).
  let signs = 0;
  let dollars = 0;
  for (;;) {
    if (/^[-−–+]/.test(value)) {
      if (value[0] !== '+') negative = !negative;
      value = value.slice(1);
      signs += 1;
    } else if (value.startsWith('$')) {
      value = value.slice(1);
      dollars += 1;
    } else break;
  }
  if (/[-−–]$/.test(value)) {
    negative = !negative;
    value = value.slice(0, -1);
    signs += 1;
  }
  if (signs > 1 || dollars > 1 || (parenthesized && signs > 0)) return undefined;
  if (!/^[\d.,]+$/.test(value) || !/\d/.test(value)) return undefined;

  const lastDot = value.lastIndexOf('.');
  const lastComma = value.lastIndexOf(',');
  let plain: string;
  if (lastDot >= 0 && lastComma >= 0) {
    // Both: the last one is the decimal point, and it has to be the column's.
    const [thousands, point] = lastDot > lastComma ? [',', '.'] : ['.', ','];
    if (decimal && point !== decimal) return undefined;
    const whole = value.slice(0, value.lastIndexOf(point));
    if (whole.includes(point) || !groupsOfThree(whole, thousands)) return undefined;
    plain = `${whole.split(thousands).join('')}.${value.slice(value.lastIndexOf(point) + 1)}`;
  } else if (lastDot >= 0 || lastComma >= 0) {
    const separator = lastDot >= 0 ? '.' : ',';
    const several = value.indexOf(separator) !== value.lastIndexOf(separator);
    const decimals = value.length - value.lastIndexOf(separator) - 1;
    // The column's decimal separator or, without one, a comma with one or two digits after it («12,5») or a lone dot
    // («12.5»; «12.345» is unreadable): any other groups thousands («1,234», «1.234.567»).
    const isPoint = decimal ? separator === decimal : !several && (separator === '.' || (decimals >= 1 && decimals <= 2));
    if (isPoint && !several) plain = value.replace(separator, '.');
    else if (!isPoint && groupsOfThree(value, separator)) plain = value.split(separator).join('');
    else return undefined;
  } else {
    plain = value;
  }

  if (!/^\d+(\.\d{0,2})?$/.test(plain)) return undefined;
  const amount = Number(plain);
  if (amount > MAX_DEPOSITED_AMOUNT) return undefined;
  return negative && amount !== 0 ? -amount : amount;
}

/** Thousands grouped by three, the first group of one to three digits and not starting with 0 («0,500» is no amount). */
function groupsOfThree(value: string, separator: string): boolean {
  const groups = value.split(separator);
  return groups.length === 1
    || (/^[1-9]\d{0,2}$/.test(groups[0]) && groups.slice(1).every((group) => /^\d{3}$/.test(group)));
}

/** A line of the file the screen did not send, and why. */
export interface SkippedLine {
  line: number;
  reason: string;
}

/** The file read with the columns chosen: the lines to compare, those left out, and what the lines add up to. */
export interface StatementReading {
  lines: PlatformStatementLine[];
  skipped: SkippedLine[];
  totalCents: number;
  /** The last row of totals of the file (not a subtotal) with an amount that can be read, to check the lines against. */
  fileTotal?: { line: number; cents: number };
}

export const TOTAL_ROW_REASON = 'Es un renglón de totales del archivo.';

// «Total», «Total:», «TOTAL A PAGAR», «Totales», «Total general», «Gran total», «Subtotal» or «Sub-total».
const TOTAL_ROW = /^(gran |sub[ -]?)?total(es)?\b/;
const SUBTOTAL_ROW = /^sub[ -]?total/;

function totalLabel(text: string): string {
  return normalizeHeader(text).replace(/[\s:.]+$/, '');
}

/** A text cut to what fits, without splitting a character in two (an emoji is two units of UTF-16, as the API counts). */
export function cutText(text: string, max: number): string {
  if (text.length <= max) return text;
  const end = /[\uD800-\uDBFF]/.test(text[max - 1]) ? max - 1 : max;
  return text.slice(0, end);
}

/**
 * The lines of a statement (the rows after its header) with the columns chosen. A row without an amount that can be read is
 * left out and said; so is a row of totals of the file (a cell with «Total», «Subtotal» or «Gran total», and no folio but
 * that word), which would count everything twice.
 */
export function readStatement(rows: { line: number; cells: string[] }[], columns: StatementColumns): StatementReading {
  const lines: PlatformStatementLine[] = [];
  const skipped: SkippedLine[] = [];
  let totalCents = 0;
  let fileTotal: StatementReading['fileTotal'];
  const cellOf = (row: { cells: string[] }, index: number) => (index >= 0 ? (row.cells[index] ?? '').trim() : '');
  const decimal = decimalSeparatorOf(rows.map((row) => cellOf(row, columns.amount)));
  for (const row of rows) {
    const folio = cellOf(row, columns.folio);
    const amountText = cellOf(row, columns.amount);
    const concept = cellOf(row, columns.concept).replace(/\s+/g, ' ');
    if (folio === '' && amountText === '' && concept === '') continue;
    const label = row.cells.map(totalLabel).find((value) => TOTAL_ROW.test(value));
    if ((folio === '' || TOTAL_ROW.test(totalLabel(folio))) && label !== undefined) {
      skipped.push({ line: row.line, reason: TOTAL_ROW_REASON });
      const amount = parseStatementAmount(amountText, decimal);
      if (amount !== undefined && !SUBTOTAL_ROW.test(label)) fileTotal = { line: row.line, cents: toCents(amount) };
      continue;
    }
    const amount = parseStatementAmount(amountText, decimal);
    if (amount === undefined) {
      skipped.push({ line: row.line, reason: amountText === '' ? 'No tiene importe.' : `No se pudo leer el importe «${amountText}».` });
      continue;
    }
    if (folio.length > MAX_FOLIO_INPUT_LENGTH) {
      skipped.push({ line: row.line, reason: 'Su folio o ID es demasiado largo para ser de la plataforma.' });
      continue;
    }
    lines.push({ row: row.line, folio: folio || null, amount, concept: cutText(concept, MAX_CONCEPT_LENGTH) || null });
    totalCents += toCents(amount);
  }
  return { lines, skipped, totalCents, fileTotal };
}

/**
 * Whether what the lines add up to is what the file says it deposits: no line was left out but the rows of totals, and the
 * file's own total, if it has one, is the same. Only then the deposit is filled with it.
 */
export function readingAddsUp(reading: StatementReading): boolean {
  return reading.skipped.every((line) => line.reason === TOTAL_ROW_REASON)
    && (reading.fileTotal === undefined || reading.fileTotal.cents === reading.totalCents);
}

/** The reason an adjustment of a concept of the statement most likely has: the person may change it. */
export function kindForConcept(concept: string): PlatformSettlementAdjustmentKind {
  const text = normalizeHeader(concept);
  if (/promo|descuento|cupon|bonific/.test(text)) return PlatformSettlementAdjustmentKind.Promotion;
  if (/reclamo|faltante|queja|claim|reembolso|devolucion/.test(text)) return PlatformSettlementAdjustmentKind.Claim;
  if (/cancel/.test(text)) return PlatformSettlementAdjustmentKind.Cancellation;
  if (/saldo anterior|saldo pendiente|semana anterior|adeudo|balance/.test(text)) return PlatformSettlementAdjustmentKind.PreviousBalance;
  if (/publicidad|anuncio|\bads?\b|marketing|cargo|comision|tableta|tablet|penaliz|\bfee|suscripcion/.test(text)) {
    return PlatformSettlementAdjustmentKind.Fee;
  }
  return PlatformSettlementAdjustmentKind.Other;
}

/** The folios the settlement takes: its own and the pending ones of the period. */
export function takesFolio(status: PlatformStatementMatchStatus): boolean {
  return status === PlatformStatementMatchStatus.InThisSettlement || status === PlatformStatementMatchStatus.Pending;
}

/** What applying a statement does to the settlement being written. */
export interface StatementProposal {
  /** The orders to include, and the orders themselves (one may not be on the screen yet). */
  orderIds: string[];
  orders: PlatformSettlementOrder[];
  /** The orders of the folios with several to pick from: they stay as they are, for the person to pick. */
  ambiguousOrders: PlatformSettlementOrder[];
  /** The adjustments of the settlement: those written by hand, and the statement's in place of any imported before. */
  adjustments: AdjustmentRow[];
  /** How many of the statement's adjustments there are, and how many imported before they replace. */
  importedAdjustments: number;
  replacedAdjustments: number;
  /** How many concepts went together in one adjustment, and how many did not fit at all (100 written by hand). */
  mergedConcepts: number;
  droppedAdjustments: number;
  /** Whether one of the statement's adjustments goes past what one holds ($1,000,000): the person splits it. */
  overLimit: boolean;
  /** Lo depositado: the statement's total when its box was empty and the file adds up; otherwise as it was. */
  depositedText: string;
  notes: string;
  /** Folios with several orders to pick from. */
  ambiguousFolios: string[];
}

function cut(text: string, max: number): string {
  return text.length <= max ? text : `${cutText(text, max - 1)}…`;
}

/**
 * The orders and adjustments a statement brings to the settlement:
 * - every order it takes (its own and the pending ones of the period);
 * - one adjustment with what the statement pays of those orders apart from what TienditApp expects of them;
 * - one with the folios it cannot take here (of another settlement, after the period, that bring nothing or of no order)
 *   and the lines whose folio cannot be read;
 * - one per concept of the lines without a folio, with the reason its words suggest.
 * Folios with several orders to pick from are left for the person, and their amount in no adjustment. Never past 100
 * adjustments: the concepts that do not fit go together in one. The deposit is filled only when the file adds up
 * (`fillDeposit`): a line left out, or a total of the file that is not what the lines add up to, would balance it wrong.
 */
export function proposeFromMatch(
  match: PlatformStatementMatch,
  form: SettlementForm,
  fileName: string,
  lineCount: number,
  fillDeposit = true,
): StatementProposal {
  const taken = match.folios.filter((folio) => takesFolio(folio.status) && folio.orders.length > 0);
  const ambiguous = match.folios.filter((folio) => folio.status === PlatformStatementMatchStatus.Ambiguous);
  const kept = form.adjustments.filter((row) => !row.description.startsWith(STATEMENT_PREFIX));

  const proposals: { kind: PlatformSettlementAdjustmentKind; description: string; cents: number }[] = [];
  const differenceCents = taken.reduce((sum, folio) => sum + toCents(folio.difference ?? 0), 0);
  const withDifference = taken.filter((folio) => toCents(folio.difference ?? 0) !== 0).length;
  if (differenceCents !== 0) {
    proposals.push({
      kind: PlatformSettlementAdjustmentKind.Other,
      description: `${STATEMENT_PREFIX}diferencia en ${withDifference === 1 ? '1 pedido' : `${withDifference} pedidos`}`,
      cents: differenceCents,
    });
  }
  const left = match.folios.filter((folio) => !takesFolio(folio.status) && folio.status !== PlatformStatementMatchStatus.Ambiguous);
  const leftCents = left.reduce((sum, folio) => sum + toCents(folio.statementAmount), 0)
    + match.unreadable.reduce((sum, line) => sum + toCents(line.amount), 0);
  if (leftCents !== 0) {
    const folios = [...left.map((folio) => folio.folio), ...match.unreadable.map((line) => line.folio)].join(', ');
    proposals.push({
      kind: PlatformSettlementAdjustmentKind.Other,
      description: cut(`${STATEMENT_PREFIX}folios que no entran en esta liquidación (${folios})`, MAX_DESCRIPTION_LENGTH),
      cents: leftCents,
    });
  }
  for (const concept of match.concepts) {
    const cents = toCents(concept.amount);
    if (cents === 0) continue;
    proposals.push({
      kind: kindForConcept(concept.concept),
      description: cut(`${STATEMENT_PREFIX}${concept.concept || 'sin concepto'}`, MAX_DESCRIPTION_LENGTH),
      cents,
    });
  }

  const room = Math.max(0, MAX_ADJUSTMENTS - kept.length);
  let fitting = proposals;
  let mergedConcepts = 0;
  if (proposals.length > room && room > 0) {
    const rest = proposals.slice(room - 1);
    mergedConcepts = rest.length;
    fitting = [
      ...proposals.slice(0, room - 1),
      {
        kind: PlatformSettlementAdjustmentKind.Other,
        description: `${STATEMENT_PREFIX}otros ${rest.length} conceptos`,
        cents: rest.reduce((sum, proposal) => sum + proposal.cents, 0),
      },
    ].filter((proposal) => proposal.cents !== 0);
  } else if (proposals.length > room) {
    fitting = [];
  }
  const imported = fitting.map<AdjustmentRow>((proposal) => ({
    key: newAdjustmentKey(),
    kind: proposal.kind,
    description: proposal.description,
    amountText: centsToText(proposal.cents),
  }));

  const note = `${STATEMENT_PREFIX}${fileName}, ${lineCount === 1 ? '1 renglón' : `${lineCount.toLocaleString('es-MX')} renglones`}.`;
  const notes = form.notes.includes(note) ? form.notes : [form.notes.trim(), note].filter(Boolean).join(' ');
  const statementCents = toCents(match.statementTotal);

  return {
    orderIds: taken.map((folio) => folio.orders[0].orderId),
    orders: taken.map((folio) => folio.orders[0]),
    ambiguousOrders: ambiguous.flatMap((folio) => folio.orders),
    adjustments: [...kept, ...imported],
    importedAdjustments: imported.length,
    replacedAdjustments: form.adjustments.length - kept.length,
    mergedConcepts,
    droppedAdjustments: proposals.length > 0 && room === 0 ? proposals.length : 0,
    overLimit: fitting.some((proposal) => Math.abs(proposal.cents) > MAX_ADJUSTMENT_AMOUNT * 100),
    depositedText: fillDeposit && form.depositedText.trim() === '' ? centsToText(statementCents) : form.depositedText,
    notes: notes.length <= MAX_NOTES_LENGTH ? notes : form.notes,
    ambiguousFolios: ambiguous.map((folio) => folio.folio),
  };
}
