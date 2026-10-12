import { useEffect, useId, useMemo, useState, type FormEvent } from 'react';
import { Modal } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import { usePlatformPrices, useSavePlatformPrices } from '../../api/platformPrices';
import { useDeliveryPlatformChannels } from '../../api/queries';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { getAccess } from '../../utils/access';
import { getPlatformName, isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { DELIVERY_PLATFORM_CHANNELS } from '../../utils/labels';
import { formatMoney, formatSignedMoney } from '../../utils/money';
import {
  MAX_OPTION_PRICE_DELTA,
  MAX_PLATFORM_PRICE,
  buildSaveBody,
  countChanges,
  optionBox,
  optionKey,
  productBox,
  productKey,
  parseRaisePercent,
  raiseAmount,
  raisePrices,
  toInputText,
  type PriceEdits,
  type PriceListItem,
  type PriceListOption,
  type RaiseSettings,
} from './platformPriceEdits';
import './PlatformPrices.css';

/** A row of the list: a product, or one of its options right under it. */
type PriceRow =
  | { kind: 'product'; item: PriceListItem }
  | { kind: 'option'; item: PriceListItem; groupName: string; option: PriceListOption };

const PRICE_HINT = `Mayor a $0 y hasta $${MAX_PLATFORM_PRICE.toLocaleString('es-MX')}, con dos decimales.`;
const DELTA_HINT = `Hasta $${MAX_OPTION_PRICE_DELTA.toLocaleString('es-MX')}, con «-» si resta y dos decimales.`;
// The menu's price has its own column from a small tablet up; on a phone it goes under the name, so the box fits.
const MENU_COLUMN = 'text-end d-none d-sm-table-cell';
// Every row of a business on one page: the boxes keep what was typed while the person searches or filters.
const PAGE_SIZES = [1000];

function rowsOf(items: PriceListItem[]): PriceRow[] {
  return items.flatMap((item): PriceRow[] => [
    { kind: 'product', item },
    ...item.optionGroups.flatMap((group) => group.options.map((option): PriceRow => ({ kind: 'option', item, groupName: group.name, option }))),
  ]);
}

/**
 * Catálogo › Precios en apps: what each product costs in the app of Rappi, Uber Eats or DiDi Food, and what each of its
 * options adds there. Vender charges the orders of that platform with them, and the commission and the net follow; an
 * empty box charges the menu's price. Nothing is saved until the person saves: changing platform or leaving asks first.
 */
export default function PlatformPrices() {
  const raiseTitleId = useId();
  const filterId = useId();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const toast = useToast();
  const { confirm } = useConfirm();
  const channels = useDeliveryPlatformChannels(!!tenant);
  const activePlatforms = useMemo(
    () => (channels.data ?? []).flatMap((row) => (row.isActive && isDeliveryPlatform(row.channel) ? [row.channel] : [])),
    [channels.data],
  );
  const [chosen, setChosen] = useState<DeliveryPlatform | null>(null);
  // The first platform that is on, once known; Rappi while none is.
  const platform: DeliveryPlatform = chosen ?? activePlatforms[0] ?? DELIVERY_PLATFORM_CHANNELS[0];
  const name = getPlatformName(platform);
  const prices = usePlatformPrices(platform, !!tenant && !channels.isPending);
  const save = useSavePlatformPrices();
  const [edits, setEdits] = useState<PriceEdits>({});
  const [category, setCategory] = useState('');
  const [error, setError] = useState('');
  const [raising, setRaising] = useState(false);

  const items = useMemo(() => prices.data?.items ?? [], [prices.data]);
  const totalCount = prices.data?.totalCount ?? 0;
  const categories = useMemo(() => {
    const seen = new Map<string, string>();
    for (const item of items) if (!seen.has(item.categoryId)) seen.set(item.categoryId, item.categoryName);
    return [...seen].map(([id, label]) => ({ id, label }));
  }, [items]);
  const shownItems = useMemo(() => (category ? items.filter((item) => item.categoryId === category) : items), [items, category]);
  const rows = useMemo(() => rowsOf(shownItems), [shownItems]);
  const { changed, invalid } = useMemo(() => countChanges(items, edits), [items, edits]);
  // A box marked red in another category keeps the save off too: the bar says where to look.
  const invalidElsewhere = useMemo(() => invalid - countChanges(shownItems, edits).invalid, [invalid, shownItems, edits]);
  const isOn = activePlatforms.includes(platform);

  // A reload or a closed tab would lose what was typed: the browser asks first.
  useEffect(() => {
    if (changed === 0) return;
    const keep = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener('beforeunload', keep);
    return () => window.removeEventListener('beforeunload', keep);
  }, [changed]);

  // The first change fixes the platform on screen: a platform turned on or off meanwhile (the list of the active ones is read
  // again) never takes what was typed for another.
  const pinPlatform = () => setChosen((current) => current ?? platform);

  const type = (key: string, text: string) => {
    pinPlatform();
    setEdits((current) => ({ ...current, [key]: text }));
  };

  const changePlatform = async (next: DeliveryPlatform) => {
    if (next === platform) return;
    if (changed > 0) {
      const accepted = await confirm({
        title: 'Cambios sin guardar',
        message: changed === 1
          ? `El precio que cambiaste en ${name} se pierde si cambias de plataforma.`
          : `Los ${changed} precios que cambiaste en ${name} se pierden si cambias de plataforma.`,
        confirmLabel: 'Cambiar de plataforma',
        danger: true,
      });
      if (!accepted) return;
    }
    setEdits({});
    setError('');
    setChosen(next);
  };

  const discard = async () => {
    const accepted = await confirm({
      title: 'Descartar cambios',
      message: changed === 1 ? 'Se pierde el precio que cambiaste.' : `Se pierden los ${changed} precios que cambiaste.`,
      confirmLabel: 'Descartar',
      danger: true,
    });
    if (!accepted) return;
    setEdits({});
    setError('');
  };

  const submit = async () => {
    const body = buildSaveBody(items, edits);
    if (!body || body.products.length === 0) return;
    setError('');
    try {
      await save.mutateAsync({ channel: platform, body });
      setEdits({});
      toast.success(`Precios de ${name} guardados.`);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, `No se pudieron guardar los precios de ${name}. Intenta de nuevo.`));
    }
  };

  const renderRow = (row: PriceRow) => {
    if (row.kind === 'product') {
      const { item } = row;
      const current = productBox(item, edits);
      const hintId = `${filterId}-p-${item.productId}`;
      return (
        <>
          <td>
            <span className="fw-semibold">{item.name}</span>
            {!item.isAvailable && <span className="badge bg-secondary-subtle text-secondary ms-2">No disponible</span>}
            <div className="small text-muted">{item.categoryName}</div>
            <div className="small text-muted d-sm-none">Tu menú: <span className="ta-money">{formatMoney(item.menuPrice)}</span></div>
          </td>
          <td className={`${MENU_COLUMN} ta-money`}>{formatMoney(item.menuPrice)}</td>
          <td className="text-end">
            <input
              className={`form-control form-control-sm text-end platform-price-input ${current.valid ? '' : 'is-invalid'}`}
              inputMode="decimal"
              autoComplete="off"
              aria-label={`Precio de ${item.name} en ${name}`}
              aria-invalid={current.valid ? undefined : true}
              aria-describedby={current.valid ? undefined : hintId}
              placeholder={toInputText(item.menuPrice)}
              value={current.text}
              onChange={(event) => type(productKey(item.productId), event.target.value)}
              disabled={save.isPending}
            />
            {!current.valid && <div id={hintId} className="invalid-feedback d-block">{PRICE_HINT}</div>}
          </td>
        </>
      );
    }

    const { item, option, groupName } = row;
    const current = optionBox(option, edits);
    const hintId = `${filterId}-o-${option.id}`;
    return (
      <>
        <td className="platform-price-option">
          {option.name} <span className="small text-muted">· {groupName}</span>
          {!option.isAvailable && <span className="badge bg-secondary-subtle text-secondary ms-2">No disponible</span>}
          <div className="small text-muted d-sm-none">Tu menú: <span className="ta-money">{formatSignedMoney(option.menuPriceDelta)}</span></div>
        </td>
        <td className={`${MENU_COLUMN} ta-money small`}>{formatSignedMoney(option.menuPriceDelta)}</td>
        <td className="text-end">
          {/* No decimal keyboard: on an iPhone it has no minus, and a cheaper variant takes one. */}
          <input
            className={`form-control form-control-sm text-end platform-price-input ${current.valid ? '' : 'is-invalid'}`}
            autoComplete="off"
            aria-label={`${option.name} (${groupName}) de ${item.name} en ${name}`}
            aria-invalid={current.valid ? undefined : true}
            aria-describedby={current.valid ? undefined : hintId}
            placeholder={toInputText(option.menuPriceDelta)}
            value={current.text}
            onChange={(event) => type(optionKey(option.id), event.target.value)}
            disabled={save.isPending}
          />
          {!current.valid && <div id={hintId} className="invalid-feedback d-block">{DELTA_HINT}</div>}
        </td>
      </>
    );
  };

  return (
    <>
      <PageTitle title="Precios en apps" />

      <div className="card">
        <div className="card-body">
          <p className="text-muted">
            Lo que cobras por cada producto en la app de {name}. Vender lo usa en los pedidos de {name}, y con él se calculan la
            comisión y el neto. Déjalo vacío para cobrar el precio de tu menú.
          </p>
          {!channels.isPending && !isOn && (
            <div className="alert alert-info small">
              {name} está apagada: estos precios se usan cuando la actives en{' '}
              {access.businessSettings
                ? <Link to="/settings/business">Configuración › Negocio › Plataformas de reparto</Link>
                : 'Configuración › Negocio › Plataformas de reparto'}
              .
            </div>
          )}
          {totalCount > items.length && (
            <div className="alert alert-warning small">
              Aquí caben {items.length} de tus {totalCount} productos: los demás se cobran con el precio de tu menú.
            </div>
          )}
          {prices.isError && (
            <div className="alert alert-danger d-flex align-items-center justify-content-between gap-2">
              <span>{getApiErrorMessage(prices.error, `No se pudo cargar la lista de precios de ${name}.`)}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void prices.refetch()}>Reintentar</button>
            </div>
          )}
          {error && (
            <div className="alert alert-danger alert-dismissible">
              {error}
              <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => setError('')}></button>
            </div>
          )}

          <DataTable
            data={rows}
            columns={[
              { header: 'Producto' },
              { header: 'Tu menú', className: MENU_COLUMN },
              { header: `En ${name}`, className: 'text-end', width: 150 },
            ]}
            keyExtractor={(row) => (row.kind === 'product' ? productKey(row.item.productId) : optionKey(row.option.id))}
            loading={channels.isPending || prices.isPending}
            emptyMessage="No hay productos en tu catálogo."
            searchPlaceholder="Producto o categoría"
            // An option goes with its product: the search looks at the product.
            searchFilter={(row, search) =>
              row.item.name.toLowerCase().includes(search) || row.item.categoryName.toLowerCase().includes(search)}
            pageSizeOptions={PAGE_SIZES}
            filters={(
              <>
                <DataTableFilter label="Plataforma" htmlFor={`${filterId}-platform`}>
                  <select
                    id={`${filterId}-platform`}
                    className="form-select form-select-sm"
                    value={platform}
                    onChange={(event) => void changePlatform(Number(event.target.value) as DeliveryPlatform)}
                    disabled={save.isPending}
                  >
                    {DELIVERY_PLATFORM_CHANNELS.map((channel) => (
                      <option key={channel} value={channel}>
                        {getPlatformName(channel)}{channels.isPending || activePlatforms.includes(channel) ? '' : ' (apagada)'}
                      </option>
                    ))}
                  </select>
                </DataTableFilter>
                <DataTableFilter label="Categoría" htmlFor={`${filterId}-category`}>
                  <select
                    id={`${filterId}-category`}
                    className="form-select form-select-sm"
                    value={category}
                    onChange={(event) => setCategory(event.target.value)}
                  >
                    <option value="">Todas</option>
                    {categories.map((option) => <option key={option.id} value={option.id}>{option.label}</option>)}
                  </select>
                </DataTableFilter>
              </>
            )}
            toolbarEnd={(
              <button
                type="button"
                className="btn btn-light btn-sm"
                onClick={() => setRaising(true)}
                disabled={shownItems.length === 0 || save.isPending}
              >
                <i className="ri-percent-line me-1" aria-hidden="true"></i>Calcular precios
              </button>
            )}
            renderRow={renderRow}
          />

          <div className="platform-prices-bar">
            <span className="small text-muted me-auto" role="status">
              {changed === 0
                ? 'Sin cambios por guardar.'
                : invalid > 0
                  ? invalidElsewhere > 0 ? 'Revisa los precios marcados en rojo, también en otras categorías.' : 'Revisa los precios marcados en rojo.'
                  : changed === 1 ? '1 precio cambiado.' : `${changed} precios cambiados.`}
            </span>
            <button type="button" className="btn btn-light" onClick={() => void discard()} disabled={changed === 0 || save.isPending}>
              Descartar
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => void submit()}
              disabled={changed === 0 || invalid > 0 || save.isPending}
            >
              {save.isPending ? 'Guardando…' : 'Guardar cambios'}
            </button>
          </div>
        </div>
      </div>

      <RaiseModal
        show={raising}
        titleId={raiseTitleId}
        platformName={name}
        items={shownItems}
        categoryName={categories.find((option) => option.id === category)?.label}
        onCancel={() => setRaising(false)}
        onRaise={(settings) => {
          pinPlatform();
          setEdits((current) => raisePrices(shownItems, current, settings));
          setRaising(false);
        }}
      />
    </>
  );
}

interface RaiseModalProps {
  show: boolean;
  titleId: string;
  platformName: string;
  /** The products of the list (the category chosen), with their options. */
  items: PriceListItem[];
  categoryName?: string;
  onCancel: () => void;
  onRaise: (settings: RaiseSettings) => void;
}

/** The menu's prices plus a percent, rounded up: it fills the boxes, and the person checks them before saving. */
function RaiseModal({ show, titleId, platformName, items, categoryName, onCancel, onRaise }: RaiseModalProps) {
  const id = useId();
  const [percentText, setPercentText] = useState('');
  const [roundTo, setRoundTo] = useState<RaiseSettings['roundTo']>(1);
  const [replaceAll, setReplaceAll] = useState(false);
  const percent = parseRaisePercent(percentText);
  const sample = items[0];
  const scope = categoryName
    ? items.length === 1 ? `el producto de «${categoryName}»` : `los ${items.length} productos de «${categoryName}»`
    : items.length === 1 ? 'tu producto' : `tus ${items.length} productos`;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (percent === undefined) return;
    onRaise({ percent, roundTo, replaceAll });
  };

  return (
    <Modal show={show} onHide={onCancel} aria-labelledby={titleId}>
      <form onSubmit={submit} noValidate>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Calcular precios de {platformName}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small text-muted">
            Tu precio más un porcentaje, para {scope}. Revisa los precios antes de guardar.
          </p>
          <div className="mb-3">
            <label className="form-label" htmlFor={`${id}-percent`}>Tu precio más (%)</label>
            <input
              id={`${id}-percent`}
              className={`form-control ${percentText && percent === undefined ? 'is-invalid' : ''}`}
              inputMode="decimal"
              autoComplete="off"
              aria-describedby={`${id}-percent-help`}
              value={percentText}
              onChange={(event) => setPercentText(event.target.value)}
            />
            <div id={`${id}-percent-help`} className={percentText && percent === undefined ? 'invalid-feedback d-block' : 'form-text'}>
              {percentText && percent === undefined ? 'Escribe de 0.01 a 200, con dos decimales como máximo.' : 'Por ejemplo, 25 para cobrar 25 % más que en tu menú.'}
            </div>
          </div>
          <div className="mb-3">
            <label className="form-label" htmlFor={`${id}-round`}>Redondear hacia arriba</label>
            <select
              id={`${id}-round`}
              className="form-select"
              aria-describedby={`${id}-round-help`}
              value={roundTo}
              onChange={(event) => setRoundTo(Number(event.target.value) as RaiseSettings['roundTo'])}
            >
              <option value={1}>Al peso</option>
              <option value={5}>A $5</option>
              <option value={10}>A $10</option>
            </select>
            <div id={`${id}-round-help`} className="form-text">Las opciones se redondean al peso.</div>
          </div>
          <div className="form-check">
            <input
              id={`${id}-replace`}
              type="checkbox"
              className="form-check-input"
              checked={replaceAll}
              onChange={(event) => setReplaceAll(event.target.checked)}
            />
            <label className="form-check-label" htmlFor={`${id}-replace`}>También los que ya tienen precio</label>
          </div>
          {sample && percent !== undefined && (
            <p className="small mb-0 mt-3">
              Por ejemplo: {sample.name}, de <span className="ta-money">{formatMoney(sample.menuPrice)}</span> a{' '}
              <span className="ta-money">{formatMoney(raiseAmount(sample.menuPrice, percent, roundTo))}</span>.
            </p>
          )}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onCancel}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={percent === undefined}>Calcular</button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
