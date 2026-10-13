import type { ReactNode } from 'react';
import type { CashSessionDto, CashTransactionDto } from '../../types';
import { CashSessionStatus } from '../../types';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import { describeDifference, getTransactionTypeLabel, personLabel } from '../../pages/cash/cashFormat';
import { formatInBusinessZone } from '../../utils/dates';

interface CashCutTicketProps {
  businessName?: string;
  session: CashSessionDto;
  movements: CashTransactionDto[];
}

const dateTime = (value: string) => formatInBusinessZone(value, { dateStyle: 'short', timeStyle: 'short' });

/** An amount in one piece: on a narrow roll its sign never stays on the line above. */
const amount = (text: string) => <span className="text-nowrap">{text}</span>;

/** "04/10/26, 1:25 p.m.": on a narrow roll it wraps after the date, never inside the time. */
function DateTime({ value }: { value: string }) {
  return (
    <>
      {formatInBusinessZone(value, { dateStyle: 'short' })},{' '}
      <span className="text-nowrap">{formatInBusinessZone(value, { timeStyle: 'short' })}</span>
    </>
  );
}

function Row({ label, value, strong }: { label: string; value: ReactNode; strong?: boolean }) {
  return (
    <div className={`ta-ticket-row${strong ? ' fw-bold' : ''}`}>
      <span>{label}</span>
      <span>{value}</span>
    </div>
  );
}

/** Who opened or closed the drawer (their name, or their user when the API has none), on its own line under the row. */
function Person({ name, user }: { name?: string | null; user: string }) {
  return <div className="small ta-ticket-wrap">{personLabel(name, user)}</div>;
}

/** The cut of one shift, printed for the cashier and whoever receives the cash, with room to sign. */
export default function CashCutTicket({ businessName, session, movements }: CashCutTicketProps) {
  const closed = session.status === CashSessionStatus.Closed;
  const expected = session.expectedClosingAmount ?? session.expectedBalance;
  return (
    <div>
      {businessName && <div className="text-center fw-bold">{businessName}</div>}
      <div className="text-center fw-bold">{closed ? 'Corte de caja' : 'Corte parcial (caja abierta)'}</div>
      {session.branchName && <div className="text-center small ta-ticket-wrap">{session.branchName}</div>}
      <div className="mt-2">
        <Row label="Abrió" value={<DateTime value={session.openedAt} />} />
        <Person name={session.openedByName} user={session.openedBy} />
        {closed && session.closedAt && (
          <>
            <Row label="Cerró" value={<DateTime value={session.closedAt} />} />
            {session.closedBy && <Person name={session.closedByName} user={session.closedBy} />}
          </>
        )}
      </div>
      <hr className="my-1" />
      <Row label="Fondo inicial" value={amount(formatMoney(session.openingAmount))} />
      <Row label="Entró en efectivo" value={amount(formatMoney(session.inflows))} />
      <Row label="Salió en efectivo" value={amount(formatMoney(session.outflows))} />
      <Row label="Debía haber" value={amount(formatMoney(expected))} strong />
      {closed && (
        <>
          <Row label="Contado" value={amount(formatMoney(session.closingAmount ?? 0))} strong />
          {session.closingDifference !== null && session.closingDifference !== undefined && (
            // "Faltan $50.00" may wrap at its space; the amount never has a sign.
            <Row label="Diferencia" value={describeDifference(session.closingDifference, formatMoney)} strong />
          )}
        </>
      )}
      <Row label="Tarjeta y otros (fuera de caja)" value={amount(formatMoney(session.nonCashInflows))} />
      {session.cashCount.length > 0 && (
        <>
          <hr className="my-1" />
          <div className="small">Conteo</div>
          {session.cashCount.map((line) => (
            <Row key={line.denomination} label={`${line.quantity} × ${formatMoney(line.denomination)}`} value={amount(formatMoney(line.quantity * line.denomination))} />
          ))}
        </>
      )}
      {movements.length > 0 && (
        <>
          <hr className="my-1" />
          <div className="small">Movimientos</div>
          {movements.map((movement) => (
            <Row
              key={movement.id}
              label={`${formatInBusinessZone(movement.occurredAt, { timeStyle: 'short' })} ${getTransactionTypeLabel(movement.transactionType)}`}
              value={amount(formatSignedMoney(movement.amount))}
            />
          ))}
        </>
      )}
      {session.closingNotes && <div className="small mt-1 ta-ticket-wrap">Nota: {session.closingNotes}</div>}
      <div className="mt-4 small">
        <div className="border-top pt-1 mb-4">Firma de quien entrega</div>
        <div className="border-top pt-1">Firma de quien recibe</div>
      </div>
      <div className="text-center small mt-2">Impreso el {dateTime(new Date().toISOString())}</div>
    </div>
  );
}
