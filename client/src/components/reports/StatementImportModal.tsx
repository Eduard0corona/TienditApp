import { useId, useState, type ChangeEvent } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useMatchPlatformStatement, type PlatformStatementMatch } from '../../api/platformSettlements';
import { PlatformStatementMatchStatus } from '../../types';
import { CSV_EMPTY_MESSAGE, CSV_TOO_BIG_MESSAGE, MAX_CSV_BYTES, decodeCsv, parseCsv } from '../../utils/csv';
import { getPlatformName, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { formatMoney, formatSignedMoney, toCents } from '../../utils/money';
import { parseSignedAmount, MAX_DEPOSITED_AMOUNT, MAX_ADJUSTMENTS, type SettlementForm } from './settlementEdits';
import { ADJUSTMENT_KIND_LABELS, STATEMENT_STATUS_LABELS } from './settlementLabels';
import {
  CHOOSE_COLUMNS_MESSAGE,
  MAX_STATEMENT_LINES,
  NO_LINES_MESSAGE,
  STATEMENT_PREFIX,
  TOO_MANY_LINES_MESSAGE,
  guessColumns,
  kindForConcept,
  proposeFromMatch,
  readStatement,
  rememberedColumns,
  takesFolio,
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
const STORAGE_KEY = 'tienditapp.settlement.statement-columns';

function loadRemembered(platform: DeliveryPlatform): { folio: string; amount: string; concept: string } | null {
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(`${STORAGE_KEY}.${platform}`) ?? 'null');
    if (value && typeof value === 'object' && 'folio' in value && 'amount' in value && 'concept' in value) {
      return value as { folio: string; amount: string; concept: string };
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
 * compared by the API with the orders by folio; applying it marks the orders and writes the adjustments, which the person
 * reviews before saving the settlement.
 */
export default function StatementImportModal({ platform, settlementId, form, pickedIds, onHide, onApply }: StatementImportModalProps) {
  const id = useId();
  const name = getPlatformName(platform);
  const matching = useMatchPlatformStatement();
  const [file, setFile] = useState<LoadedFile | null>(null);
  const [columns, setColumns] = useState<StatementColumns>({ folio: -1, amount: -1, concept: -1 });
  const [error, setError] = useState('');
  const [result, setResult] = useState<{ match: PlatformStatementMatch; reading: StatementReading } | null>(null);

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
    const [header, ...rows] = parseCsv(decodeCsv(await chosen.arrayBuffer()));
    if (!header || rows.length === 0) {
      setFile(null);
      setError(CSV_EMPTY_MESSAGE);
      return;
    }
    const loaded = { name: chosen.name, headers: header.cells.map((cell) => cell.trim()), rows };
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

  const proposal = result && file ? proposeFromMatch(result.match, form, file.name, result.reading.lines.length) : null;
  const preview = file ? readStatement(file.rows.slice(0, 3), columns) : null;
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
              solo se comparan sus renglones con tus pedidos por folio: no se guarda nada hasta que guardes la liquidación.
            </p>
            <label className="form-label" htmlFor={`${id}-file`}>Archivo CSV</label>
            <input id={`${id}-file`} type="file" className="form-control" accept=".csv,text/csv,text/plain" onChange={(event) => void choose(event)} />
            {file && (
              <>
                <p className="small text-muted mt-2 mb-3">
                  {file.name}: {file.rows.length === 1 ? '1 renglón' : `${file.rows.length.toLocaleString('es-MX')} renglones`} después del encabezado.
                </p>
                <div className="row g-3">
                  {column('Folio del pedido', 'folio')}
                  {column('Importe', 'amount')}
                  {column('Concepto (opcional)', 'concept', true)}
                </div>
                {preview && preview.lines.length > 0 && (
                  <>
                    <p className="small fw-semibold mt-3 mb-1">Así se leen los primeros renglones</p>
                    <ul className="list-unstyled small mb-0">
                      {preview.lines.map((line) => (
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
        ) : proposal && <Comparison name={name} result={result} proposal={proposal} form={form} pickedIds={pickedIds} />}
      </Modal.Body>
      <Modal.Footer>
        {result ? (
          <>
            <button type="button" className="btn btn-light me-auto" onClick={() => setResult(null)}>Elegir otro archivo</button>
            <button type="button" className="btn btn-light" onClick={onHide}>Cancelar</button>
            <button type="button" className="btn btn-primary" onClick={() => proposal && onApply(proposal)}>Aplicar a la liquidación</button>
          </>
        ) : (
          <>
            <button type="button" className="btn btn-light" onClick={onHide}>Cancelar</button>
            <button type="button" className="btn btn-primary" disabled={!file || matching.isPending} onClick={() => void compare()}>
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
}

/** What the statement says against the orders, and what applying it does. */
function Comparison({ name, result, proposal, form, pickedIds }: ComparisonProps) {
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
  const replaced = form.adjustments.some((row) => row.description.startsWith(STATEMENT_PREFIX));
  const typed = parseSignedAmount(form.depositedText, MAX_DEPOSITED_AMOUNT);
  const statementCents = toCents(match.statementTotal);
  const brought = new Set(proposal.orderIds);
  const leaving = pickedIds.filter((orderId) => !brought.has(orderId)).length;

  return (
    <>
      <p className="mb-2">
        El estado de cuenta suma <span className="fw-semibold">{formatMoney(match.statementTotal)}</span> en{' '}
        {reading.lines.length === 1 ? '1 renglón' : `${reading.lines.length.toLocaleString('es-MX')} renglones`}.
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
          {taken.length === 1 ? '1 pedido' : `${taken.length} pedidos`}: el estado de cuenta paga {formatMoney(match.matchedTotal)} y
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

      <div className="alert alert-info small mt-3 mb-0">
        Al aplicar se incluyen {proposal.orderIds.length === 1 ? 'el pedido que entra' : `los ${proposal.orderIds.length} pedidos que entran`}
        {proposal.importedAdjustments > 0
          ? ` y se agregan ${proposal.importedAdjustments === 1 ? '1 ajuste' : `${proposal.importedAdjustments} ajustes`} con lo demás`
          : ''}
        {replaced ? ', en lugar de los del estado de cuenta que ya habías importado' : ''}.
        {leaving > 0 && ` ${leaving === 1 ? 'Sale 1 pedido incluido' : `Salen ${leaving} pedidos incluidos`} que el estado de cuenta no trae: siguen pendientes.`}
        {proposal.adjustments.length >= MAX_ADJUSTMENTS && ' Una liquidación lleva hasta 100 ajustes: los conceptos que no cupieron van juntos.'}
        {' '}
        {form.depositedText.trim() === ''
          ? `Lo depositado será ${formatMoney(match.statementTotal)}.`
          : typed !== undefined && toCents(typed) !== statementCents
            ? `Lo depositado (${formatMoney(typed)}) no es lo que suma el estado de cuenta: revísalo.`
            : ''}
        {' '}Revisa los motivos de los ajustes antes de guardar.
      </div>
    </>
  );
}
