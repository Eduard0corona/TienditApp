import { useEffect, useId, useRef, useState, type ChangeEvent, type RefObject } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useMatchPlatformStatement, type PlatformStatementMatch } from '../../api/platformSettlements';
import { PlatformStatementMatchStatus } from '../../types';
import { CSV_EMPTY_MESSAGE, CSV_NOT_TEXT_MESSAGE, CSV_TOO_BIG_MESSAGE, MAX_CSV_BYTES, decodeCsv, parseCsv, splitHeader } from '../../utils/csv';
import { getPlatformName, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { formatMoney, formatSignedMoney, toCents } from '../../utils/money';
import { parseSignedAmount, MAX_DEPOSITED_AMOUNT, type SettlementForm } from './settlementEdits';
import { ADJUSTMENT_KIND_LABELS, STATEMENT_STATUS_LABELS } from './settlementLabels';
import {
  CHOOSE_COLUMNS_MESSAGE,
  MAX_STATEMENT_LINES,
  NO_LINES_MESSAGE,
  TOO_MANY_LINES_MESSAGE,
  guessColumns,
  kindForConcept,
  proposeFromMatch,
  readStatement,
  readingAddsUp,
  rememberedColumns,
  takesFolio,
  TOTAL_ROW_REASON,
  type StatementColumns,
  type StatementProposal,
  type StatementReading,
} from './statementImport';

interface StatementImportModalProps {
  platform: DeliveryPlatform;
  /** The open settlement, or null for a new one. */
  settlementId: string | null;
  form: SettlementForm;
  /** The orders included now: those the statement does not bring leave. */
  pickedIds: string[];
  onHide: () => void;
  onApply: (proposal: StatementProposal) => void;
}

interface LoadedFile {
  name: string;
  headers: string[];
  rows: { line: number; cells: string[] }[];
}

/** The most folios each group of the comparison names; the rest are counted. */
const SHOWN = 10;
/** The rows the preview reads to show the first lines that can be read. */
const PREVIEWED_ROWS = 20;
const STORAGE_KEY = 'tienditapp.settlement.statement-columns';

function loadRemembered(platform: DeliveryPlatform): { folio: string; amount: string; concept: string } | null {
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(`${STORAGE_KEY}.${platform}`) ?? 'null');
    if (value && typeof value === 'object') {
      const { folio, amount, concept } = value as Record<string, unknown>;
      if (typeof folio === 'string' && typeof amount === 'string' && typeof concept === 'string') return { folio, amount, concept };
    }
  } catch {
    // Without storage (private window, blocked site data) the columns are guessed every time.
  }
  return null;
}

function remember(platform: DeliveryPlatform, file: LoadedFile, columns: StatementColumns) {
  const name = (index: number) => (index >= 0 ? file.headers[index] ?? '' : '');
  try {
    window.localStorage.setItem(`${STORAGE_KEY}.${platform}`, JSON.stringify({
      folio: name(columns.folio), amount: name(columns.amount), concept: name(columns.concept),
    }));
  } catch {
    // Only a convenience: the next file guesses them again.
  }
}

function names(folios: string[]): string {
  return folios.length <= SHOWN ? folios.join(', ') : `${folios.slice(0, SHOWN).join(', ')} y ${folios.length - SHOWN} más`;
}

/**
 * Importar el estado de cuenta (Fase 22H): the platform's CSV read in this browser, its columns chosen, and its lines
 * compared by the API with the orders by folio or long id (Fase 22I); applying it marks the orders and writes the
 * adjustments, which the person reviews before saving the settlement.
 */
export default function StatementImportModal({ platform, settlementId, form, pickedIds, onHide, onApply }: StatementImportModalProps) {
  const id = useId();
  const name = getPlatformName(platform);
  const matching = useMatchPlatformStatement();
  const [file, setFile] = useState<LoadedFile | null>(null);
  const [columns, setColumns] = useState<StatementColumns>({ folio: -1, amount: -1, concept: -1 });
  const [error, setError] = useState('');
  const [result, setResult] = useState<{ match: PlatformStatementMatch; reading: StatementReading } | null>(null);
  // The button pressed leaves the screen: the focus goes to what replaced it, so the keyboard and the reader follow.
  const [focusFile, setFocusFile] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const summaryRef = useRef<HTMLParagraphElement>(null);

  useEffect(() => {
    if (result) summaryRef.current?.focus();
    else if (focusFile) fileRef.current?.focus();
  }, [result, focusFile]);

  const choose = async (event: ChangeEvent<HTMLInputElement>) => {
    const chosen = event.target.files?.[0];
    event.target.value = '';
    setError('');
    setResult(null);
    if (!chosen) return;
    if (chosen.size > MAX_CSV_BYTES) {
      setFile(null);
      setError(CSV_TOO_BIG_MESSAGE);
      return;
    }
    const text = decodeCsv(await chosen.arrayBuffer());
    if (text === null) {
      setFile(null);
      setError(CSV_NOT_TEXT_MESSAGE);
      return;
    }
    const table = splitHeader(parseCsv(text));
    if (!table) {
      setFile(null);
      setError(CSV_EMPTY_MESSAGE);
      return;
    }
    const loaded = { name: chosen.name, headers: table.header, rows: table.rows };
    setFile(loaded);
    setColumns(rememberedColumns(loaded.headers, loadRemembered(platform)) ?? guessColumns(loaded.headers));
  };

  const compare = async () => {
    if (!file) return;
    if (columns.folio < 0 || columns.amount < 0 || columns.folio === columns.amount) {
      setError(CHOOSE_COLUMNS_MESSAGE);
      return;
    }
    const reading = readStatement(file.rows, columns);
    if (reading.lines.length === 0) {
      setError(NO_LINES_MESSAGE);
      return;
    }
    if (reading.lines.length > MAX_STATEMENT_LINES) {
      setError(TOO_MANY_LINES_MESSAGE);
      return;
    }
    setError('');
    try {
      const match = await matching.mutateAsync({
        channel: platform,
        settlementId,
        periodFrom: form.from,
        periodTo: form.to,
        lines: reading.lines,
      });
      remember(platform, file, columns);
      setResult({ match, reading });
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo comparar el estado de cuenta. Intenta de nuevo.'));
    }
  };

  const proposal = result && file
    ? proposeFromMatch(result.match, form, file.name, result.reading.lines.length, readingAddsUp(result.reading))
    : null;
  const chosenColumns = columns.folio >= 0 && columns.amount >= 0 && columns.folio !== columns.amount;
  const preview = file && chosenColumns ? readStatement(file.rows.slice(0, PREVIEWED_ROWS), columns).lines.slice(0, 3) : null;
  const column = (label: string, key: keyof StatementColumns, optional = false) => (
    <div className="col-12 col-sm-4">
      <label className="form-label" htmlFor={`${id}-${key}`}>{label}</label>
      <select
        id={`${id}-${key}`}
        className="form-select"
        value={columns[key]}
        onChange={(event) => setColumns({ ...columns, [key]: Number(event.target.value) })}
      >
        <option value={-1}>{optional ? 'Sin concepto' : 'Elige la columna'}</option>
        {file?.headers.map((header, index) => (
          <option key={index} value={index}>{header || `Columna ${index + 1}`}</option>
        ))}
      </select>
    </div>
  );

  return (
    <Modal show onHide={onHide} size="lg" centered aria-labelledby={`${id}-title`}>
      <Modal.Header closeButton>
        <Modal.Title id={`${id}-title`}>Importar estado de cuenta</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        {!result ? (
          <>
            <p className="small text-muted">
              Descarga el estado de cuenta de {name} del periodo en CSV (en Excel: Guardar como › CSV). El archivo se lee aquí y
              solo se comparan sus renglones con tus pedidos por folio o por ID largo: no se guarda nada hasta que guardes la
              liquidación.
            </p>
            <label className="form-label" htmlFor={`${id}-file`}>Archivo CSV</label>
            <input
              ref={fileRef}
              id={`${id}-file`}
              type="file"
              className="form-control"
              accept=".csv,text/csv,text/plain"
              onChange={(event) => void choose(event)}
            />
            {file && (
              <>
                <p className="small text-muted mt-2 mb-3">
                  {file.name}: {file.rows.length === 1 ? '1 renglón' : `${file.rows.length.toLocaleString('es-MX')} renglones`} después del encabezado.
                </p>
                <div className="row g-3">
                  {column('Folio o ID del pedido', 'folio')}
                  {column('Importe', 'amount')}
                  {column('Concepto (opcional)', 'concept', true)}
                </div>
                {preview && preview.length === 0 && (
                  <p className="small text-muted mt-3 mb-0">Con estas columnas, los primeros renglones no tienen un importe que se lea.</p>
                )}
                {preview && preview.length > 0 && (
                  <>
                    <p className="small fw-semibold mt-3 mb-1">Así se leen los primeros renglones</p>
                    <ul className="list-unstyled small mb-0">
                      {preview.map((line) => (
                        <li key={line.row}>
                          Línea {line.row}: {line.folio ? `folio ${line.folio}` : 'sin folio'} · {formatMoney(line.amount)}
                          {line.concept ? ` · ${line.concept}` : ''}
                        </li>
                      ))}
                    </ul>
                  </>
                )}
              </>
            )}
          </>
        ) : proposal && (
          <Comparison name={name} result={result} proposal={proposal} form={form} pickedIds={pickedIds} summaryRef={summaryRef} />
        )}
      </Modal.Body>
      <Modal.Footer>
        {result ? (
          <>
            <button
              type="button"
              className="btn btn-light me-auto"
              onClick={() => {
                setFocusFile(true);
                setResult(null);
              }}
            >
              Elegir otro archivo
            </button>
            <button type="button" className="btn btn-light" onClick={onHide}>Cancelar</button>
            <button type="button" className="btn btn-primary" onClick={() => proposal && onApply(proposal)}>Aplicar a la liquidación</button>
          </>
        ) : (
          <>
            <button type="button" className="btn btn-light" onClick={onHide}>Cancelar</button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={!file || matching.isPending}
              aria-busy={matching.isPending}
              onClick={() => void compare()}
            >
              {matching.isPending ? 'Comparando…' : 'Comparar con mis pedidos'}
            </button>
          </>
        )}
      </Modal.Footer>
    </Modal>
  );
}

interface ComparisonProps {
  name: string;
  result: { match: PlatformStatementMatch; reading: StatementReading };
  proposal: StatementProposal;
  form: SettlementForm;
  pickedIds: string[];
  summaryRef: RefObject<HTMLParagraphElement | null>;
}

function count(n: number, one: string, many: string): string {
  return n === 1 ? one : `${n.toLocaleString('es-MX')} ${many}`;
}

/** What the statement says against the orders, and what applying it does. */
function Comparison({ name, result, proposal, form, pickedIds, summaryRef }: ComparisonProps) {
  const { match, reading } = result;
  const taken = match.folios.filter((folio) => takesFolio(folio.status));
  const differing = taken.filter((folio) => toCents(folio.difference ?? 0) !== 0);
  const groups = [
    PlatformStatementMatchStatus.Ambiguous,
    PlatformStatementMatchStatus.Settled,
    PlatformStatementMatchStatus.AfterPeriod,
    PlatformStatementMatchStatus.NothingExpected,
    PlatformStatementMatchStatus.Unknown,
  ].map((status) => ({ status, folios: match.folios.filter((folio) => folio.status === status).map((folio) => folio.folio) }))
    .filter((group) => group.folios.length > 0);
  const typed = parseSignedAmount(form.depositedText, MAX_DEPOSITED_AMOUNT);
  const statementCents = toCents(match.statementTotal);
  const brought = new Set([...proposal.orderIds, ...proposal.ambiguousOrders.map((order) => order.orderId)]);
  const leaving = pickedIds.filter((orderId) => !brought.has(orderId)).length;
  const missed = reading.skipped.filter((line) => line.reason !== TOTAL_ROW_REASON).length;
  const fileTotalDiffers = reading.fileTotal !== undefined && reading.fileTotal.cents !== reading.totalCents;
  const orderCount = proposal.orderIds.length;

  const applying = [
    orderCount === 0
      ? 'Al aplicar no se incluye ningún pedido'
      : orderCount === 1 ? 'Al aplicar se incluye el pedido que entra' : `Al aplicar se incluyen los ${orderCount} pedidos que entran`,
    proposal.importedAdjustments > 0
      ? ` y se ${proposal.importedAdjustments === 1 ? 'agrega 1 ajuste' : `agregan ${proposal.importedAdjustments} ajustes`} con lo demás`
      : '',
    proposal.replacedAdjustments > 0
      ? `${proposal.importedAdjustments > 0 ? ', en lugar de' : '; se quitan'} ${proposal.replacedAdjustments === 1 ? 'el ajuste' : `los ${proposal.replacedAdjustments} ajustes`} del estado de cuenta que ya habías importado`
      : '',
    '.',
  ].join('');

  return (
    <>
      <p ref={summaryRef} tabIndex={-1} className="mb-2">
        El estado de cuenta suma <span className="fw-semibold">{formatMoney(match.statementTotal)}</span> en{' '}
        {count(reading.lines.length, '1 renglón', 'renglones')}.
      </p>
      {reading.skipped.length > 0 && (
        <details className="small mb-2">
          <summary>{reading.skipped.length === 1 ? 'Un renglón no se tomó' : `${reading.skipped.length} renglones no se tomaron`}</summary>
          <ul className="mb-0">
            {reading.skipped.slice(0, SHOWN).map((line) => <li key={line.line}>Línea {line.line}: {line.reason}</li>)}
            {reading.skipped.length > SHOWN && <li>Y {reading.skipped.length - SHOWN} más.</li>}
          </ul>
        </details>
      )}

      <h3 className="h6 mt-3">Pedidos que entran</h3>
      {taken.length === 0 ? (
        <p className="small text-muted">Ningún folio es un pedido de {name} que esta liquidación pueda tomar.</p>
      ) : (
        <p className="small mb-1">
          {count(taken.length, '1 pedido', 'pedidos')}: el estado de cuenta paga {formatMoney(match.matchedTotal)} y
          TienditApp espera {formatMoney(match.expectedTotal)}.
          {differing.length > 0 && ` ${differing.length === 1 ? 'Uno trae' : `${differing.length} traen`} una diferencia:`}
        </p>
      )}
      {differing.length > 0 && (
        <ul className="small mb-2">
          {differing.slice(0, SHOWN).map((folio) => (
            <li key={folio.folio}>
              {folio.folio}: paga {formatMoney(folio.statementAmount)}, se esperaba {formatMoney(folio.expectedNet ?? 0)}{' '}
              ({formatSignedMoney(folio.difference ?? 0)})
            </li>
          ))}
          {differing.length > SHOWN && <li>Y {differing.length - SHOWN} más.</li>}
        </ul>
      )}

      {(groups.length > 0 || match.unreadable.length > 0) && (
        <>
          <h3 className="h6 mt-3">Folios que no entran aquí</h3>
          <ul className="small mb-2">
            {groups.map((group) => (
              <li key={group.status}>{STATEMENT_STATUS_LABELS[group.status]}: {names(group.folios)}</li>
            ))}
            {match.unreadable.length > 0 && (
              <li>No se leen como folios: {names(match.unreadable.map((line) => `${line.folio} (línea ${line.row})`))}</li>
            )}
          </ul>
        </>
      )}

      {match.concepts.length > 0 && (
        <>
          <h3 className="h6 mt-3">Otros conceptos</h3>
          <ul className="small mb-2">
            {match.concepts.slice(0, SHOWN).map((concept) => (
              <li key={concept.concept}>
                {concept.concept || 'Sin concepto'}: {formatSignedMoney(concept.amount)} · {ADJUSTMENT_KIND_LABELS[kindForConcept(concept.concept)]}
              </li>
            ))}
            {match.concepts.length > SHOWN && <li>Y {match.concepts.length - SHOWN} más.</li>}
          </ul>
        </>
      )}

      {(missed > 0 || fileTotalDiffers) && (
        <div className="alert alert-warning small mt-3 mb-0">
          {missed > 0 && `${missed === 1 ? 'Un renglón con importe no se tomó' : `${missed} renglones con importe no se tomaron`} (arriba dice por qué). `}
          {fileTotalDiffers && reading.fileTotal && (
            `El renglón de totales del archivo (línea ${reading.fileTotal.line}) dice ${formatMoney(reading.fileTotal.cents / 100)}, `
            + `y lo que se tomó suma ${formatMoney(reading.totalCents / 100)}. `
          )}
          Lo depositado no se llena solo: escríbelo como llegó al banco, y la diferencia dirá lo que falta.
        </div>
      )}

      <div className="alert alert-info small mt-3 mb-0">
        {applying}
        {leaving > 0 && ` ${leaving === 1 ? 'Sale 1 pedido incluido' : `Salen ${leaving} pedidos incluidos`} que el estado de cuenta no trae: siguen pendientes.`}
        {proposal.ambiguousFolios.length > 0 && (
          ` ${proposal.ambiguousFolios.length === 1 ? 'Un folio tiene' : `${proposal.ambiguousFolios.length} folios tienen`} varios pedidos: elige en la lista cuál incluir.`
        )}
        {proposal.mergedConcepts > 0 && ` Una liquidación lleva hasta 100 ajustes: ${proposal.mergedConcepts} conceptos van juntos en uno.`}
        {proposal.droppedAdjustments > 0 && ' La liquidación ya tiene 100 ajustes escritos a mano: los del estado de cuenta no caben.'}
        {proposal.overLimit && ' Un ajuste pasa de $1,000,000, lo más que lleva uno: pártelo en dos antes de guardar.'}
        {' '}
        {form.depositedText.trim() === ''
          ? (proposal.depositedText.trim() !== '' ? `Lo depositado será ${formatMoney(match.statementTotal)}.` : '')
          : typed !== undefined && toCents(typed) !== statementCents
            ? `Lo depositado (${formatMoney(typed)}) no es lo que suma el estado de cuenta: revísalo.`
            : ''}
        {' '}Revisa los motivos de los ajustes antes de guardar.
      </div>
    </>
  );
}
