import type { CashSessionDto, CashTransactionDto } from '../../types';
import { CashSessionStatus } from '../../types';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import { describeDifference, getTransactionTypeLabel } from '../../pages/cash/cashFormat';
import { formatInBusinessZone } from '../../utils/dates';

interface CashCutTicketProps {
  businessName?: string;
  session: CashSessionDto;
  movements: CashTransactionDto[];
}

const dateTime = (value: string) => formatInBusinessZone(value, { dateStyle: 'short', timeStyle: 'short' });

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={`d-flex justify-content-between gap-2${strong ? ' fw-bold' : ''}`}>
      <span>{label}</span>
      <span>{value}</span>
    </div>
  );
}

/** The cut of one shift, printed for the cashier and whoever receives the cash, with room to sign. */
export default function CashCutTicket({ businessName, session, movements }: CashCutTicketProps) {
  const closed = session.status === CashSessionStatus.Closed;
  const expected = session.expectedClosingAmount ?? session.expectedBalance;
  return (
    <div>
      {businessName && <div className="text-center fw-bold">{businessName}</div>}
      <div className="text-center fw-bold">{closed ? 'Corte de caja' : 'Corte parcial (caja abierta)'}</div>
      <div className="text-center small mb-2">
        {session.openedBy}{session.branchName ? ` · ${session.branchName}` : ''}
      </div>
      <Row label="Abrió" value={dateTime(session.openedAt)} />
      {closed && session.closedAt && <Row label={`Cerró${session.closedBy ? ` (${session.closedBy})` : ''}`} value={dateTime(session.closedAt)} />}
      <hr className="my-1" />
      <Row label="Fondo inicial" value={formatMoney(session.openingAmount)} />
      <Row label="Entró en efectivo" value={formatMoney(session.inflows)} />
      <Row label="Salió en efectivo" value={formatMoney(session.outflows)} />
      <Row label="Debía haber" value={formatMoney(expected)} strong />
      {closed && (
        <>
          <Row label="Contado" value={formatMoney(session.closingAmount ?? 0)} strong />
          {session.closingDifference !== null && session.closingDifference !== undefined && (
            <Row label="Diferencia" value={describeDifference(session.closingDifference, formatMoney)} strong />
          )}
        </>
      )}
      <Row label="Tarjeta y otros (fuera de caja)" value={formatMoney(session.nonCashInflows)} />
      {session.cashCount.length > 0 && (
        <>
          <hr className="my-1" />
          <div className="small">Conteo</div>
          {session.cashCount.map((line) => (
            <Row key={line.denomination} label={`${line.quantity} × ${formatMoney(line.denomination)}`} value={formatMoney(line.quantity * line.denomination)} />
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
              value={formatSignedMoney(movement.amount)}
            />
          ))}
        </>
      )}
      {session.closingNotes && <div className="small mt-1">Nota: {session.closingNotes}</div>}
      <div className="mt-4 small">
        <div className="border-top pt-1 mb-4">Firma de quien entrega</div>
        <div className="border-top pt-1">Firma de quien recibe</div>
      </div>
      <div className="text-center small mt-2">Impreso el {dateTime(new Date().toISOString())}</div>
    </div>
  );
}
