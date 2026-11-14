import { Link } from 'react-router-dom';
import type { QuoteSettings } from '../../api/quotesApi';
import { DEPOSIT_PERCENTS } from '../../utils/quotes';

interface QuoteDepositSettingProps {
  id: string;
  settings: QuoteSettings;
  /** Owners and admins change it; the rest read it. */
  canChange: boolean;
  /** Another setting is saving: a change meanwhile waits. */
  busy: boolean;
  /** The share being saved, shown while it saves. */
  pending: number | null;
  onChange: (percent: number) => void;
}

/**
 * The deposit to accept a quote from its link (Fase 31N, ADR 0094): the share of what is left to pay the customer pays
 * online, with the business's Mercado Pago account; paying it accepts the quote and makes it its service order. It is
 * offered while the business can collect it; one already asked stays and can be turned off.
 */
export default function QuoteDepositSetting({ id, settings, canChange, busy, pending, onChange }: QuoteDepositSettingProps) {
  const saved = settings.depositPercent ?? 0;
  const value = pending ?? saved;
  const percents = DEPOSIT_PERCENTS.includes(saved) || saved === 0 ? DEPOSIT_PERCENTS : [...DEPOSIT_PERCENTS, saved].sort((a, b) => a - b);
  const unavailable = !settings.depositAvailable;

  return (
    <>
      <h2 className="h6 mt-3 mb-1">Anticipo</h2>
      <p className="text-muted small mb-2">
        {saved
          ? `Para aceptar la cotización desde su enlace, tu cliente paga en línea el ${saved} % de lo que queda por pagar con tu cuenta de Mercado Pago. Al aprobarse el pago, la cotización queda aceptada y se vuelve su orden de servicio, con el anticipo pagado. Cada versión pide el anticipo que tenía al enviarla.`
          : 'Tus clientes aceptan la cotización desde su enlace sin pagar nada en línea.'}
        {unavailable && (
          <>
            {' '}
            {saved
              ? 'Ahora tu cuenta de Mercado Pago no puede cobrar: revísala en '
              : 'Para pedir anticipo, conecta tu cuenta de Mercado Pago en '}
            <Link to="/settings/online-payments">Pagos en línea</Link>.
          </>
        )}
      </p>
      {canChange && (
        <div>
          <label className="form-label small mb-1" htmlFor={id}>Anticipo para aceptar</label>
          <select
            id={id}
            className="form-select form-select-sm"
            style={{ width: '13rem' }}
            value={value}
            // A share can be asked only while the business can collect it; one already asked can always go off.
            disabled={unavailable && saved === 0}
            aria-disabled={busy || undefined}
            onChange={(e) => {
              if (busy) return;
              onChange(Number(e.target.value));
            }}
          >
            <option value={0}>Sin anticipo</option>
            {percents.map((percent) => (
              <option key={percent} value={percent} disabled={unavailable && percent !== saved}>
                {percent} % de lo que queda por pagar
              </option>
            ))}
          </select>
        </div>
      )}
    </>
  );
}
