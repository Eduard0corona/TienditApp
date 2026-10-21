import type { PlatformSettlementOrder, PlatformStatementLine, PlatformStatementMatch } from '../../api/platformSettlements';
import { PlatformSettlementAdjustmentKind, PlatformStatementMatchStatus } from '../../types';
import { toCents } from '../../utils/money';
import {
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

/** The limits of the API (MatchPlatformStatementQuery and PlatformOrderNumber). */
export const MAX_STATEMENT_LINES = 5_000;
export const MAX_FOLIO_INPUT_LENGTH = 60;
export const MAX_CONCEPT_LENGTH = 200;

export const TOO_MANY_LINES_MESSAGE = 'Se importan hasta 5,000 renglones a la vez.';
export const CHOOSE_COLUMNS_MESSAGE = 'Elige la columna del folio y la del importe.';
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

/**
 * An amount as a statement writes it: «$1,234.56», «-1,234.56», «(1,234.56)», «1.234,56», «12,5» or «1234.5 MXN».
 * Undefined when it is not one, when it has more than two decimals or when it goes past what a deposit holds.
 */
export function parseStatementAmount(text: string): number | undefined {
  let value = text.replace(/\s/g, '').replace(/MXN/gi, '');
  if (value === '') return undefined;
  let negative = false;
  if (/^\(.*\)$/.test(value)) {
    negative = true;
    value = value.slice(1, -1);
  }
  if (/^[-−–]/.test(value)) {
    negative = !negative;
    value = value.slice(1);
  } else if (/[-−–]$/.test(value)) {
    negative = !negative;
    value = value.slice(0, -1);
  }
  value = value.replace(/^\$/, '');
  if (!/^[\d.,]+$/.test(value) || !/\d/.test(value)) return undefined;

  const lastDot = value.lastIndexOf('.');
  const lastComma = value.lastIndexOf(',');
  let plain: string;
  if (lastDot >= 0 && lastComma >= 0) {
    // Both: the last one is the decimal point.
    const [thousands, decimal] = lastDot > lastComma ? [',', '.'] : ['.', ','];
    const [whole, cents = ''] = [value.slice(0, value.lastIndexOf(decimal)), value.slice(value.lastIndexOf(decimal) + 1)];
    if (whole.includes(decimal) || !groupsOfThree(whole, thousands)) return undefined;
    plain = `${whole.split(thousands).join('')}.${cents}`;
  } else if (lastComma >= 0) {
    const decimals = value.length - lastComma - 1;
    // One comma with one or two digits after it is a decimal comma («12,5»); otherwise it groups thousands («1,234»).
    if (value.indexOf(',') === lastComma && decimals >= 1 && decimals <= 2) plain = value.replace(',', '.');
    else if (groupsOfThree(value, ',')) plain = value.split(',').join('');
    else return undefined;
  } else if (value.indexOf('.') !== lastDot) {
    // Several dots only group thousands («1.234.567»).
    if (!groupsOfThree(value, '.')) return undefined;
    plain = value.split('.').join('');
  } else {
    plain = value;
  }

  if (!/^\d+(\.\d{0,2})?$/.test(plain)) return undefined;
  const amount = Number(plain);
  if (amount > MAX_DEPOSITED_AMOUNT) return undefined;
  return negative && amount !== 0 ? -amount : amount;
}

function groupsOfThree(value: string, separator: string): boolean {
  const groups = value.split(separator);
  return groups.length === 1 || (groups[0].length >= 1 && groups[0].length <= 3 && groups.slice(1).every((group) => /^\d{3}$/.test(group)));
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
}

const TOTAL_ROW = /^(sub ?)?total(es)?( general)?$/;

/**
 * The lines of a statement (the rows after its header) with the columns chosen. A row without an amount that can be read is
 * left out and said; so is a row of totals of the file («Total» in a cell, and no folio but that word), which would count
 * everything twice.
 */
export function readStatement(rows: { line: number; cells: string[] }[], columns: StatementColumns): StatementReading {
  const lines: PlatformStatementLine[] = [];
  const skipped: SkippedLine[] = [];
  let totalCents = 0;
  for (const row of rows) {
    const cell = (index: number) => (index >= 0 ? (row.cells[index] ?? '').trim() : '');
    const folio = cell(columns.folio);
    const amountText = cell(columns.amount);
    const concept = cell(columns.concept).replace(/\s+/g, ' ');
    if (folio === '' && amountText === '' && concept === '') continue;
    if ((folio === '' || TOTAL_ROW.test(normalizeHeader(folio))) && row.cells.some((value) => TOTAL_ROW.test(normalizeHeader(value)))) {
      skipped.push({ line: row.line, reason: 'Es un renglón de totales del archivo.' });
      continue;
    }
    const amount = parseStatementAmount(amountText);
    if (amount === undefined) {
      skipped.push({ line: row.line, reason: amountText === '' ? 'No tiene importe.' : `No se pudo leer el importe «${amountText}».` });
      continue;
    }
    if (folio.length > MAX_FOLIO_INPUT_LENGTH) {
      skipped.push({ line: row.line, reason: 'Su folio es demasiado largo para ser de la plataforma.' });
      continue;
    }
    lines.push({ row: row.line, folio: folio || null, amount, concept: concept.slice(0, MAX_CONCEPT_LENGTH) || null });
    totalCents += toCents(amount);
  }
  return { lines, skipped, totalCents };
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
  /** The adjustments of the settlement: those written by hand, and the statement's in place of any imported before. */
  adjustments: AdjustmentRow[];
  /** How many of the statement's adjustments there are. */
  importedAdjustments: number;
  /** Lo depositado, when its box was empty. */
  depositedText: string;
  notes: string;
  /** Folios with several pending orders: the person picks which one. */
  ambiguousFolios: string[];
}

function cut(text: string, max: number): string {
  return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}

/**
 * The orders and adjustments a statement brings to the settlement:
 * - every order it takes (its own and the pending ones of the period);
 * - one adjustment with what the statement pays of those orders apart from what TienditApp expects of them;
 * - one with the folios it cannot take here (of another settlement, after the period, that bring nothing or of no order)
 *   and the lines whose folio cannot be read;
 * - one per concept of the lines without a folio, with the reason its words suggest.
 * Folios with several pending orders are left for the person to pick. Never past 100 adjustments: the concepts that do not
 * fit go together in one.
 */
export function proposeFromMatch(match: PlatformStatementMatch, form: SettlementForm, fileName: string, lineCount: number): StatementProposal {
  const taken = match.folios.filter((folio) => takesFolio(folio.status) && folio.orders.length > 0);
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
  if (proposals.length > room) {
    const rest = proposals.slice(Math.max(0, room - 1));
    fitting = room === 0 ? [] : [
      ...proposals.slice(0, room - 1),
      {
        kind: PlatformSettlementAdjustmentKind.Other,
        description: `${STATEMENT_PREFIX}otros ${rest.length} conceptos`,
        cents: rest.reduce((sum, proposal) => sum + proposal.cents, 0),
      },
    ].filter((proposal) => proposal.cents !== 0);
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
    adjustments: [...kept, ...imported],
    importedAdjustments: imported.length,
    depositedText: form.depositedText.trim() === '' ? centsToText(statementCents) : form.depositedText,
    notes: notes.length <= MAX_NOTES_LENGTH ? notes : form.notes,
    ambiguousFolios: match.folios.filter((folio) => folio.status === PlatformStatementMatchStatus.Ambiguous).map((folio) => folio.folio),
  };
}
