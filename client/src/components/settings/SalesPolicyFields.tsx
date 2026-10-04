import { OrderNotificationPolicy, type TenantSalesPolicyDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { estimateOrderTotals } from '../../utils/orderTotals';

interface SalesPolicyFieldsProps {
  value: TenantSalesPolicyDto;
  onChange: (value: TenantSalesPolicyDto) => void;
  disabled?: boolean;
  idPrefix: string;
}

const RATE_OPTIONS = [
  { value: 16, label: '16 % (general)' },
  { value: 8, label: '8 % (región fronteriza)' },
  { value: 0, label: '0 %' },
];

const NOTIFICATION_OPTIONS: { value: OrderNotificationPolicy; label: string; help: string }[] = [
  {
    value: OrderNotificationPolicy.RemoteAndServices,
    label: 'Pedidos a distancia y servicios (recomendado)',
    help: 'Avisa de pedidos por WhatsApp o por el menú en línea y de servicios que el cliente recoge después. Una venta de mostrador no manda mensaje: el cliente ya tiene su ticket.',
  },
  {
    value: OrderNotificationPolicy.AllOrders,
    label: 'Todos los pedidos con teléfono',
    help: 'También las ventas de mostrador en las que capturas el teléfono del cliente.',
  },
  {
    value: OrderNotificationPolicy.Off,
    label: 'Ninguno',
    help: 'No se manda ningún aviso automático de pedidos. Puedes escribirle al cliente desde el chat.',
  },
];

/** How a $100 product is charged with each policy, computed like the server does. */
function example(pricesIncludeTax: boolean, rate: number) {
  return estimateOrderTotals([{ unitPrice: 100, quantity: 1, ratePercent: rate, isExempt: false }], pricesIncludeTax);
}

/** Sales policy of the business: IVA, the general rate, closing paid sales, the drawer and customer messages. */
export default function SalesPolicyFields({ value, onChange, disabled, idPrefix }: SalesPolicyFieldsProps) {
  const included = example(true, value.defaultTaxRate);
  const added = example(false, value.defaultTaxRate);

  return (
    <div className="sales-policy-fields">
      <fieldset className="mb-3">
        <legend className="form-label fw-semibold fs-6">¿Tus precios ya incluyen IVA?</legend>
        <div className="row g-2">
          <div className="col-md-6">
            <label className={`policy-option ${value.pricesIncludeTax ? 'selected' : ''}`} htmlFor={`${idPrefix}-included`}>
              <input
                id={`${idPrefix}-included`}
                type="radio"
                className="form-check-input"
                name={`${idPrefix}-prices`}
                checked={value.pricesIncludeTax}
                onChange={() => onChange({ ...value, pricesIncludeTax: true })}
                disabled={disabled}
              />
              <span>
                <strong>Sí, ya lo incluyen</strong>
                <span className="d-block small text-muted">
                  Un producto de $100 se cobra {formatMoney(included.total)}; el ticket muestra el IVA incluido
                  ({formatMoney(included.tax)}). Es lo más común en México.
                </span>
              </span>
            </label>
          </div>
          <div className="col-md-6">
            <label className={`policy-option ${!value.pricesIncludeTax ? 'selected' : ''}`} htmlFor={`${idPrefix}-added`}>
              <input
                id={`${idPrefix}-added`}
                type="radio"
                className="form-check-input"
                name={`${idPrefix}-prices`}
                checked={!value.pricesIncludeTax}
                onChange={() => onChange({ ...value, pricesIncludeTax: false })}
                disabled={disabled}
              />
              <span>
                <strong>No, el IVA se suma</strong>
                <span className="d-block small text-muted">
                  Un producto de $100 se cobra {formatMoney(added.total)} (IVA de {formatMoney(added.tax)} encima del precio).
                </span>
              </span>
            </label>
          </div>
        </div>
      </fieldset>

      <div className="row g-3 align-items-end">
        <div className="col-md-6">
          <label className="form-label fw-semibold" htmlFor={`${idPrefix}-rate`}>Tasa general de IVA</label>
          <select
            id={`${idPrefix}-rate`}
            className="form-select"
            value={value.defaultTaxRate}
            onChange={(event) => onChange({ ...value, defaultTaxRate: Number(event.target.value) })}
            disabled={disabled}
            aria-describedby={`${idPrefix}-rate-help`}
          >
            {RATE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
          <div id={`${idPrefix}-rate-help`} className="form-text">
            Se aplica a los productos sin tasa propia. Cada producto puede tener 16 %, 8 %, 0 % o ser exento.
          </div>
        </div>
        <div className="col-md-6">
          <div className="form-check form-switch">
            <input
              id={`${idPrefix}-autoclose`}
              type="checkbox"
              role="switch"
              className="form-check-input"
              checked={value.autoCloseCounterSales}
              onChange={(event) => onChange({ ...value, autoCloseCounterSales: event.target.checked })}
              disabled={disabled}
            />
            <label className="form-check-label" htmlFor={`${idPrefix}-autoclose`}>
              Cerrar solas las ventas de mostrador cuando quedan pagadas
            </label>
          </div>
          <div className="form-text">
            La comida que pasa por la pantalla de cocina se cierra cuando está lista.
          </div>
          {value.requireOpenCashSession !== undefined && (
            <>
              <div className="form-check form-switch mt-3">
                <input
                  id={`${idPrefix}-require-drawer`}
                  type="checkbox"
                  role="switch"
                  className="form-check-input"
                  checked={value.requireOpenCashSession}
                  onChange={(event) => onChange({ ...value, requireOpenCashSession: event.target.checked })}
                  disabled={disabled}
                />
                <label className="form-check-label" htmlFor={`${idPrefix}-require-drawer`}>
                  Pedir caja abierta para cobrar en efectivo
                </label>
              </div>
              <div className="form-text">
                Así ningún efectivo queda fuera del corte de un cajero. Tarjeta y transferencia no lo necesitan.
              </div>
            </>
          )}
        </div>
        {value.orderNotificationPolicy !== undefined && (
          <div className="col-md-6">
            <label className="form-label fw-semibold" htmlFor={`${idPrefix}-notifications`}>
              Avisos por WhatsApp al cliente
            </label>
            <select
              id={`${idPrefix}-notifications`}
              className="form-select"
              value={value.orderNotificationPolicy}
              onChange={(event) => onChange({
                ...value,
                orderNotificationPolicy: Number(event.target.value) as OrderNotificationPolicy,
              })}
              disabled={disabled}
              aria-describedby={`${idPrefix}-notifications-help`}
            >
              {NOTIFICATION_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </select>
            <div id={`${idPrefix}-notifications-help`} className="form-text">
              {NOTIFICATION_OPTIONS.find((option) => option.value === value.orderNotificationPolicy)?.help}
              {' '}Cada aviso es un mensaje de plantilla que Meta cobra.
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
