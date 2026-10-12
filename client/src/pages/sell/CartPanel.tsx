import { Link } from 'react-router-dom';
import type { OrderChannel, OrderDto } from '../../types';
import { PlatformCollectedBy } from '../../types';
import { getPlatformName, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { formatMoney } from '../../utils/money';
import { getVatLabel, type OrderTotalsEstimate } from '../../utils/orderTotals';
import CartLines from './CartLines';
import PlatformOrderFields from './PlatformOrderFields';
import PlatformOriginPicker from './PlatformOriginPicker';
import SaleDetailsForm from './SaleDetailsForm';
import type { CartLine, ChatOrigin, SaleDetails, TabTarget } from './sellModel';

interface CartPanelProps {
  /** A table's tab: the sale goes to the kitchen instead of being charged. */
  tabTarget: TabTarget | null;
  /** The open tab a round goes to, once loaded. */
  tabOrder: OrderDto | null;
  chatOrigin: ChatOrigin | null;
  onDismissChat: () => void;
  cart: CartLine[];
  quantityDrafts: Record<string, string>;
  onChangeQuantity: (key: string, quantity: number) => void;
  onTypeQuantity: (line: CartLine, text: string) => void;
  onFinishTypingQuantity: (key: string) => void;
  onUpdateLine: (key: string, changes: Partial<CartLine>) => void;
  onClear: () => void;
  details: SaleDetails;
  onDetailsChange: (changes: Partial<SaleDetails>) => void;
  showDetails: boolean;
  onToggleDetails: () => void;
  isFood: boolean;
  phoneError: string;
  /** Estimated on the device; the server computes the final total. */
  totals: OrderTotalsEstimate;
  pricesIncludeTax: boolean;
  saleError: string;
  /** The charge dialog shows its own error. */
  chargeOpen: boolean;
  submitting: boolean;
  canCharge: boolean;
  onSendToKitchen: () => void;
  onOpenCharge: () => void;
  onSaveUnpaid: () => void;
  /** The delivery platforms active in the business: none, and Vender stays as at the counter. */
  activePlatforms: DeliveryPlatform[];
  /** The platform whose app took the order on screen, or null at the counter. */
  platform: DeliveryPlatform | null;
  onChannelChange: (channel: OrderChannel) => void;
  folioError: string;
  /** Services in the ticket: an order of a platform takes only products. */
  hasServices: boolean;
  /** An order of a platform is registered with connection only. */
  online: boolean;
  /** Registers the order of the platform without charging it. */
  onRegisterPlatformOrder: () => void;
}

/** The ticket of the sale: where it came from and where it goes, its lines, the customer, the totals and what to do with it. */
export default function CartPanel(props: CartPanelProps) {
  const { tabTarget, tabOrder, chatOrigin, cart, totals, submitting, platform } = props;
  // A chat is a WhatsApp order and a tab goes to a table: neither comes from a platform's app.
  const showOrigin = !tabTarget && !chatOrigin && props.activePlatforms.length > 0;
  const platformName = platform ? getPlatformName(platform) : '';
  const platformCharged = platform !== null && props.details.collectedBy === PlatformCollectedBy.Platform;

  return (
    <div className="sell-cart-panel">
      {tabTarget && (
        <div className="alert alert-info d-flex align-items-center justify-content-between gap-2 py-2 mb-2">
          <span className="small">
            <i className="ri-restaurant-line me-1" aria-hidden="true"></i>
            <strong>{tabTarget.label}</strong>
            {tabTarget.kind === 'round'
              ? tabOrder
                ? <> · ronda {(tabOrder.kitchenTickets?.length ?? 0) + 1} · llevan <span className="ta-money">{formatMoney(tabOrder.total)}</span></>
                : ' · nueva ronda'
              : ' · cuenta nueva'}
          </span>
          <Link to="/tables" className="btn btn-sm btn-light">Mesas</Link>
        </div>
      )}
      {chatOrigin && (
        <div className="alert alert-success d-flex align-items-center justify-content-between gap-2 py-2 mb-2">
          <span className="small">
            <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>
            Pedido por WhatsApp de <strong>{chatOrigin.name ?? chatOrigin.phone}</strong>
          </span>
          <button type="button" className="btn-close" aria-label="Ya no es un pedido por WhatsApp" onClick={props.onDismissChat}></button>
        </div>
      )}
      {showOrigin && (
        <PlatformOriginPicker
          platforms={props.activePlatforms}
          value={props.details.channel}
          onChange={props.onChannelChange}
          hasServices={props.hasServices}
          disabled={submitting}
        />
      )}
      {platform && (
        <PlatformOrderFields
          platform={platform}
          details={props.details}
          onChange={props.onDetailsChange}
          folioError={props.folioError}
          disabled={submitting}
          canCharge={props.canCharge}
        />
      )}
      <div className="d-flex align-items-center justify-content-between mb-2">
        <h2 className="h5 mb-0">Ticket</h2>
        {cart.length > 0 && (
          <button type="button" className="btn btn-sm btn-link text-danger" onClick={props.onClear} disabled={submitting}>
            Vaciar
          </button>
        )}
      </div>

      {cart.length === 0 ? (
        <div className="sell-empty text-muted">
          <i className="ri-shopping-basket-2-line" aria-hidden="true"></i>
          <p className="mb-0">Toca un producto para agregarlo.</p>
        </div>
      ) : (
        <CartLines
          cart={cart}
          quantityDrafts={props.quantityDrafts}
          onChangeQuantity={props.onChangeQuantity}
          onTypeQuantity={props.onTypeQuantity}
          onFinishTypingQuantity={props.onFinishTypingQuantity}
          onUpdateLine={props.onUpdateLine}
        />
      )}

      <SaleDetailsForm
        details={props.details}
        onChange={props.onDetailsChange}
        open={props.showDetails}
        onToggle={props.onToggleDetails}
        isFood={props.isFood}
        phoneError={props.phoneError}
        forPlatform={platform !== null}
      />

      <div className="sell-totals">
        {!props.pricesIncludeTax && (
          <div className="d-flex justify-content-between">
            <span>Subtotal</span>
            <span className="ta-money">{formatMoney(totals.subtotal)}</span>
          </div>
        )}
        {totals.breakdown.filter((line) => line.taxAmount > 0).map((line) => (
          <div className="d-flex justify-content-between small text-muted" key={`${line.ratePercent}-${line.isExempt}`}>
            <span>{props.pricesIncludeTax ? `Incluye ${getVatLabel(line)}` : getVatLabel(line)}</span>
            <span className="ta-money">{formatMoney(line.taxAmount)}</span>
          </div>
        ))}
        <div className="d-flex justify-content-between align-items-baseline sell-total">
          <span>Total</span>
          <span className="ta-money">{formatMoney(totals.total)}</span>
        </div>
      </div>

      {platform && (platformCharged || !props.canCharge) && (
        <p className="small text-muted mt-2 mb-0">
          {platformCharged ? `${platformName} ya lo cobró: no entra a tu caja.` : 'La caja lo cobra desde Pedidos cuando el repartidor pague.'}
        </p>
      )}
      {platform && !props.online && (
        <div className="alert alert-warning small py-2 mt-2 mb-0" role="status">
          <i className="ri-wifi-off-line me-1" aria-hidden="true"></i>
          Sin conexión no se pueden registrar pedidos de plataforma ni se guardan solos: déjalo en pantalla y regístralo cuando vuelva la conexión.
        </div>
      )}

      {props.saleError && !props.chargeOpen && <div className="alert alert-danger py-2 mt-2 mb-0">{props.saleError}</div>}

      {tabTarget ? (
        <div className="d-grid gap-2 mt-3">
          <button
            type="button"
            className="btn btn-primary btn-touch btn-touch-lg"
            onClick={props.onSendToKitchen}
            disabled={cart.length === 0 || submitting}
          >
            <i className="ri-restaurant-2-line me-1" aria-hidden="true"></i>
            {submitting ? 'Enviando…' : `Enviar a cocina · ${formatMoney(totals.total)}`}
          </button>
          <Link to="/tables" className="btn btn-light btn-touch">Cancelar</Link>
        </div>
      ) : platform ? (
        // One main action: register the order. When the courier pays the business, charging now is the other way out.
        <div className="d-grid gap-2 mt-3">
          <button
            type="button"
            className="btn btn-primary btn-touch btn-touch-lg"
            onClick={props.onRegisterPlatformOrder}
            disabled={cart.length === 0 || submitting || !props.online}
          >
            {submitting
              ? 'Registrando…'
              : <>Registrar pedido de {platformName} · {formatMoney(totals.total)} <span className="sell-kbd">F9</span></>}
          </button>
          {!platformCharged && props.canCharge && (
            <button
              type="button"
              className="btn btn-light btn-touch"
              onClick={props.onOpenCharge}
              disabled={cart.length === 0 || submitting || !props.online}
            >
              Cobrar ahora
            </button>
          )}
        </div>
      ) : props.canCharge ? (
        <div className="d-grid gap-2 mt-3">
          <button
            type="button"
            className="btn btn-primary btn-touch btn-touch-lg"
            onClick={props.onOpenCharge}
            disabled={cart.length === 0 || submitting}
          >
            Cobrar {formatMoney(totals.total)} <span className="sell-kbd">F9</span>
          </button>
          <button
            type="button"
            className="btn btn-light btn-touch"
            onClick={props.onSaveUnpaid}
            disabled={cart.length === 0 || submitting}
          >
            Guardar sin cobrar
          </button>
        </div>
      ) : (
        <div className="d-grid gap-2 mt-3">
          <button
            type="button"
            className="btn btn-primary btn-touch btn-touch-lg"
            onClick={props.onSaveUnpaid}
            disabled={cart.length === 0 || submitting}
          >
            {submitting ? 'Guardando…' : `Guardar pedido · ${formatMoney(totals.total)}`}
          </button>
          <p className="small text-muted text-center mb-0">La caja lo cobra desde Pedidos.</p>
        </div>
      )}
    </div>
  );
}
