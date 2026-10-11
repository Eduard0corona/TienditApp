import { useEffect, useId, useMemo, useState } from 'react';
import { Modal, Offcanvas } from 'react-bootstrap';
import { useParams, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { publicMenuApi, type PublicMenuProduct, type PublicOrderReceipt } from '../../api/publicMenuApi';
import PhotoOrInitial from '../../components/common/PhotoOrInitial';
import { useScrollFade } from '../../hooks/useScrollFade';
import type { ProductOptionGroupDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { WEEK, dayLabel, describeDay, describeOpeningStatus } from '../../utils/openingHours';
import { estimateOrderTotals, resolveProductVat } from '../../utils/orderTotals';
import {
  getLineKey,
  getOptionsLabel,
  getPriceWithOptions,
  getSelectionError,
} from '../../utils/productOptions';
import { previewBusinessTheme } from '../../utils/theme';
import { lineAmount } from '../../utils/units';
import './PublicMenuPage.css';

interface MenuLine {
  key: string;
  product: PublicMenuProduct;
  optionIds: string[];
  optionsLabel: string;
  unitPrice: number;
  quantity: number;
}

// Same limits as the API (PlacePublicOrderCommandValidator).
const MAX_LINES = 50;
const MAX_QUANTITY = 99;

function whatsAppUrl(phone: string | null | undefined, text: string): string | null {
  const digits = (phone ?? '').replace(/\D/g, '');
  if (digits.length < 10) return null;
  const target = digits.length === 10 ? `52${digits}` : digits;
  return `https://wa.me/${target}?text=${encodeURIComponent(text)}`;
}

/** A map search of the address: opens the maps app on a phone. */
function mapsUrl(address: string): string {
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
}

function groupsOf(product: PublicMenuProduct): ProductOptionGroupDto[] {
  return (product.optionGroups ?? []) as ProductOptionGroupDto[];
}

/**
 * The public menu of a business (/m/:slug), without signing in: the catalog the business published and, when
 * it takes orders, a cart that sends the order with the customer's name and phone. A table QR adds ?mesa=.
 * Its header says whether the business is open now (the API works it out in the business's time), its hours of
 * the week and its address with a map.
 */
export default function PublicMenuPage() {
  const optionsTitleId = useId();
  const cartTitleId = useId();
  const { slug = '' } = useParams<{ slug: string }>();
  const [searchParams] = useSearchParams();
  const tableLabel = searchParams.get('mesa')?.trim().slice(0, 50) || '';
  const atTable = tableLabel !== '';
  const menu = useQuery({
    queryKey: ['public-menu', slug, tableLabel],
    queryFn: () => publicMenuApi.getMenu(slug, tableLabel || undefined),
    retry: false,
    staleTime: 60_000,
  });
  const data = menu.data;
  const menuTheme = data?.theme;
  useEffect(() => {
    // The customer sees the menu in the colors its business picked, whoever is signed in on this browser.
    if (menuTheme === undefined) return undefined;
    previewBusinessTheme(menuTheme);
    return () => previewBusinessTheme(undefined);
  }, [menuTheme]);
  const [category, setCategory] = useState<string>('all');
  const [lines, setLines] = useState<MenuLine[]>([]);
  const [choosing, setChoosing] = useState<{ product: PublicMenuProduct; optionIds: string[] } | null>(null);
  const [cartOpen, setCartOpen] = useState(false);
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [notes, setNotes] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');
  const [receipt, setReceipt] = useState<PublicOrderReceipt | null>(null);

  const totals = useMemo(() => estimateOrderTotals(
    lines.map((line) => ({
      unitPrice: line.unitPrice,
      quantity: line.quantity,
      ...resolveProductVat(line.product, data?.defaultTaxRate ?? 16),
    })),
    data?.pricesIncludeTax ?? true,
  ), [data, lines]);
  const itemCount = lines.reduce((sum, line) => sum + line.quantity, 0);
  const categories = data?.categories ?? [];
  const visible = category === 'all' ? categories : categories.filter((item) => item.id === category);
  const { ref: chipsRef, fadeClass: chipsFade } = useScrollFade<HTMLDivElement>(categories.length);

  const add = (product: PublicMenuProduct, optionIds: string[]) => {
    const key = getLineKey(product.id, optionIds);
    setLines((current) => {
      if (current.some((line) => line.key === key)) {
        return current.map((line) => (line.key === key ? { ...line, quantity: Math.min(line.quantity + 1, MAX_QUANTITY) } : line));
      }
      if (current.length >= MAX_LINES) return current;
      const groups = groupsOf(product);
      return [...current, {
        key,
        product,
        optionIds,
        optionsLabel: getOptionsLabel(groups, optionIds),
        unitPrice: getPriceWithOptions(product.price, groups, optionIds),
        quantity: 1,
      }];
    });
  };

  const tapProduct = (product: PublicMenuProduct) => {
    if (groupsOf(product).length > 0) {
      const preselected = groupsOf(product)
        .filter((group) => group.minSelections === 1 && group.maxSelections === 1)
        .flatMap((group) => group.options.filter((option) => option.isAvailable).slice(0, 1).map((option) => option.id));
      setChoosing({ product, optionIds: preselected });
      return;
    }
    add(product, []);
  };

  const toggleOption = (group: ProductOptionGroupDto, optionId: string) => {
    if (!choosing) return;
    const inGroup = new Set(group.options.map((option) => option.id));
    const selected = choosing.optionIds.includes(optionId);
    let optionIds: string[];
    if (group.maxSelections === 1) {
      const others = choosing.optionIds.filter((id) => !inGroup.has(id));
      optionIds = selected && group.minSelections === 0 ? others : [...others, optionId];
    } else if (selected) {
      optionIds = choosing.optionIds.filter((id) => id !== optionId);
    } else {
      if (choosing.optionIds.filter((id) => inGroup.has(id)).length >= group.maxSelections) return;
      optionIds = [...choosing.optionIds, optionId];
    }
    setChoosing({ ...choosing, optionIds });
  };

  const changeQuantity = (key: string, delta: number) => setLines((current) => current
    .map((line) => (line.key === key ? { ...line, quantity: Math.min(line.quantity + delta, MAX_QUANTITY) } : line))
    .filter((line) => line.quantity > 0));

  const send = async (event: React.FormEvent) => {
    event.preventDefault();
    if (lines.length === 0 || sending) return;
    setSending(true);
    setError('');
    try {
      const result = await publicMenuApi.placeOrder(slug, {
        customerName: name.trim(),
        customerPhone: phone.trim(),
        notes: notes.trim() || null,
        tableLabel: tableLabel || null,
        items: lines.map((line) => ({
          productId: line.product.id,
          quantity: line.quantity,
          optionIds: line.optionIds,
          notes: null,
        })),
        expectedTotal: totals.total,
      });
      setReceipt(result);
      setLines([]);
      setCartOpen(false);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo enviar el pedido. Intenta de nuevo.'));
      void menu.refetch();
    } finally {
      setSending(false);
    }
  };

  if (menu.isPending) {
    return <div className="pm-page"><p className="text-muted p-4" aria-busy="true">Cargando el menú…</p></div>;
  }
  if (menu.isError || !data) {
    return (
      <div className="pm-page">
        <div className="pm-empty">
          <i className="ri-store-2-line" aria-hidden="true"></i>
          <h1 className="h5">Este negocio no tiene menú en línea</h1>
          <p className="text-muted">Revisa el enlace o pregunta en el negocio.</p>
        </div>
      </div>
    );
  }

  const contact = whatsAppUrl(data.contactPhone, `Hola ${data.businessName}, vi su menú en línea.`);
  const hours = data.openingHours;
  const status = hours ? describeOpeningStatus(hours) : null;
  // Paused while it is closed: its hours say when to come back (at one of its tables the API takes the order anyway).
  const closedByHours = data.ordersPaused && status?.isOpen === false;
  // Once a product has a photo, the others show their initial so the names line up; without photos, only text.
  const withPhotos = categories.some((item) => item.products.some((product) => product.imageUrl));

  if (receipt) {
    const followUp = whatsAppUrl(receipt.contactPhone, `Hola, hice el pedido ${receipt.orderNumber} en su menú en línea.`);
    return (
      <div className="pm-page">
        <div className="pm-empty">
          <i className="ri-checkbox-circle-fill text-success" aria-hidden="true"></i>
          <h1 className="h4">¡Pedido enviado!</h1>
          <p className="mb-1">Folio <strong>{receipt.orderNumber}</strong> · Total <strong className="ta-money">{formatMoney(receipt.total)}</strong></p>
          <p className="text-muted">
            {receipt.confirmed
              ? `${receipt.businessName} ya lo está preparando.`
              : `${receipt.businessName} lo confirmará en unos minutos.`}
          </p>
          <div className="d-grid gap-2 mt-3" style={{ maxWidth: '20rem', margin: '0 auto' }}>
            {followUp && (
              <a className="btn btn-success btn-touch" href={followUp} target="_blank" rel="noopener noreferrer">
                <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Avisar por WhatsApp
              </a>
            )}
            <button type="button" className="btn btn-light btn-touch" onClick={() => setReceipt(null)}>Hacer otro pedido</button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="pm-page">
      <header className="pm-header">
        <PhotoOrInitial src={data.logoUrl} name={data.businessName} className="pm-logo" loading="eager" />
        <div className="pm-header-text">
          <h1 className="h5 mb-0">{data.businessName}</h1>
          {status && (
            <div className="pm-status small">
              <span className={`ta-chip ${status.isOpen ? 'ta-chip-ready' : 'ta-chip-done'}`}>{status.label}</span>
              {status.detail && <span className="text-muted">{status.detail}</span>}
            </div>
          )}
          {(atTable || !status) && (
            <div className="small text-muted">
              {atTable ? `Pedido para ${tableLabel}` : data.acceptsOrders ? 'Pide en línea' : 'Menú'}
            </div>
          )}
        </div>
        {contact && (
          <a className="btn btn-sm btn-success" href={contact} target="_blank" rel="noopener noreferrer" aria-label="Escribir por WhatsApp">
            <i className="ri-whatsapp-line" aria-hidden="true"></i>
          </a>
        )}
      </header>

      {(hours || data.address) && (
        <section className="pm-info" aria-label="Horario y ubicación">
          {hours && (
            <details className="pm-hours">
              <summary>
                <i className="ri-time-line" aria-hidden="true"></i>
                <span className="flex-grow-1">Hoy: {describeDay(hours.intervals, hours.today)}</span>
                <i className="ri-arrow-down-s-line pm-hours-toggle" aria-hidden="true"></i>
              </summary>
              <ul className="pm-week list-unstyled" aria-label="Horario de la semana">
                {WEEK.map((day) => (
                  <li key={day} aria-current={day === hours.today ? 'date' : undefined}>
                    <span>{dayLabel(day)}</span>
                    <span>{describeDay(hours.intervals, day)}</span>
                  </li>
                ))}
              </ul>
            </details>
          )}
          {data.address && (
            <p className="pm-address">
              <i className="ri-map-pin-line" aria-hidden="true"></i>
              <span className="flex-grow-1">{data.address}</span>
              <a className="text-nowrap" href={mapsUrl(data.address)} target="_blank" rel="noopener noreferrer">Cómo llegar</a>
            </p>
          )}
        </section>
      )}

      {closedByHours ? (
        <div className="alert alert-info mx-3 mt-3 mb-0" role="status">
          Fuera de horario no recibimos pedidos en línea{status?.when ? `: vuelve ${status.when}` : ''}. Mientras, puedes ver
          el menú.
        </div>
      ) : data.ordersPaused && (
        <div className="alert alert-warning mx-3 mt-3 mb-0" role="status">
          Por ahora no estamos recibiendo pedidos en línea. Puedes ver el menú y volver más tarde.
        </div>
      )}

      <nav className="pm-chips" aria-label="Categorías">
        <div ref={chipsRef} className={`pm-chips-row ta-scroll-fade ${chipsFade}`}>
          <button type="button" className={`btn btn-sm ${category === 'all' ? 'btn-primary' : 'btn-light'}`} onClick={() => setCategory('all')}>
            Todo
          </button>
          {categories.map((item) => (
            <button key={item.id} type="button" className={`btn btn-sm ${category === item.id ? 'btn-primary' : 'btn-light'}`} onClick={() => setCategory(item.id)}>
              {item.name}
            </button>
          ))}
        </div>
      </nav>

      <main className="pm-list">
        {visible.map((item) => (
          <section key={item.id} aria-label={item.name}>
            <h2 className="pm-category">{item.name}</h2>
            {item.products.map((product) => {
              const inCart = lines.filter((line) => line.product.id === product.id).reduce((sum, line) => sum + line.quantity, 0);
              return (
                <article key={product.id} className={`pm-product ${product.availableNow ? '' : 'unavailable'}`}>
                  {withPhotos && <PhotoOrInitial src={product.imageUrl} name={product.name} className="pm-product-image" />}
                  <div className="flex-grow-1 min-w-0">
                    <h3 className="pm-product-name">{product.name}</h3>
                    {product.description && <p className="pm-product-description">{product.description}</p>}
                    <div className="d-flex align-items-center gap-2 flex-wrap">
                      <span className="pm-price ta-money">{formatMoney(product.price)}</span>
                      {product.compareAtPrice && <s className="small text-muted ta-money">{formatMoney(product.compareAtPrice)}</s>}
                      {!product.availableNow && <span className="badge bg-light text-dark">No disponible ahora</span>}
                    </div>
                  </div>
                  {data.acceptsOrders && product.availableNow && (
                    <button type="button" className="btn btn-primary pm-add" onClick={() => tapProduct(product)} aria-label={`Agregar ${product.name}`}>
                      {inCart > 0 ? <span className="ta-money">{inCart}</span> : <i className="ri-add-line" aria-hidden="true"></i>}
                    </button>
                  )}
                </article>
              );
            })}
          </section>
        ))}
        {categories.length === 0 && <p className="text-muted text-center py-5">El menú todavía no tiene productos.</p>}
      </main>

      {data.acceptsOrders && lines.length > 0 && (
        <div className="pm-cart-bar">
          <button type="button" className="btn btn-primary btn-touch w-100" onClick={() => setCartOpen(true)}>
            Ver pedido · {itemCount} · <span className="ta-money">{formatMoney(totals.total)}</span>
          </button>
        </div>
      )}

      <Modal show={!!choosing} onHide={() => setChoosing(null)} centered scrollable fullscreen="sm-down" aria-labelledby={optionsTitleId}>
        {choosing && (
          <>
            <Modal.Header closeButton>
              <Modal.Title id={optionsTitleId} as="h2" className="h5">{choosing.product.name}</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              {groupsOf(choosing.product).map((group) => (
                <fieldset key={group.id} className="mb-3">
                  <legend className="fs-6 fw-semibold mb-1">
                    {group.name}{' '}
                    <span className="small text-muted fw-normal">
                      {group.minSelections > 0 ? '· obligatorio' : group.maxSelections > 1 ? `· hasta ${group.maxSelections}` : '· opcional'}
                    </span>
                  </legend>
                  {group.options.map((option) => {
                    const checked = choosing.optionIds.includes(option.id);
                    return (
                      <label key={option.id} className={`pm-option ${checked ? 'active' : ''} ${option.isAvailable ? '' : 'disabled'}`}>
                        <input
                          type={group.maxSelections === 1 ? 'radio' : 'checkbox'}
                          className="form-check-input me-2"
                          name={`pm-${group.id}`}
                          checked={checked}
                          disabled={!option.isAvailable}
                          onChange={() => toggleOption(group, option.id)}
                        />
                        <span className="flex-grow-1">{option.name}{!option.isAvailable && ' (agotado)'}</span>
                        {option.priceDelta !== 0 && (
                          <span className="small ta-money">{option.priceDelta > 0 ? '+' : '−'}{formatMoney(Math.abs(option.priceDelta))}</span>
                        )}
                      </label>
                    );
                  })}
                </fieldset>
              ))}
            </Modal.Body>
            <Modal.Footer>
              {getSelectionError(groupsOf(choosing.product), choosing.optionIds) && (
                <span className="text-danger small me-auto">{getSelectionError(groupsOf(choosing.product), choosing.optionIds)}</span>
              )}
              <button
                type="button"
                className="btn btn-primary btn-touch"
                disabled={Boolean(getSelectionError(groupsOf(choosing.product), choosing.optionIds))}
                onClick={() => {
                  add(choosing.product, choosing.optionIds);
                  setChoosing(null);
                }}
              >
                Agregar · {formatMoney(getPriceWithOptions(choosing.product.price, groupsOf(choosing.product), choosing.optionIds))}
              </button>
            </Modal.Footer>
          </>
        )}
      </Modal>

      <Offcanvas show={cartOpen} onHide={() => setCartOpen(false)} placement="bottom" className="pm-sheet" aria-labelledby={cartTitleId}>
        <Offcanvas.Header closeButton>
          <Offcanvas.Title id={cartTitleId} as="h2" className="h5">Tu pedido</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>
          <form onSubmit={(event) => void send(event)}>
            <ul className="list-unstyled mb-3">
              {lines.map((line) => (
                <li key={line.key} className="d-flex align-items-center gap-2 py-2 border-bottom">
                  <div className="flex-grow-1">
                    <div className="fw-semibold">{line.product.name}</div>
                    {line.optionsLabel && <div className="small text-muted">{line.optionsLabel}</div>}
                    <div className="small ta-money">{formatMoney(lineAmount(line.unitPrice, line.quantity))}</div>
                  </div>
                  <div className="btn-group" role="group" aria-label={`Cantidad de ${line.product.name}`}>
                    <button type="button" className="btn btn-light" onClick={() => changeQuantity(line.key, -1)} aria-label="Uno menos">−</button>
                    <span className="btn btn-light disabled ta-money">{line.quantity}</span>
                    <button type="button" className="btn btn-light" onClick={() => changeQuantity(line.key, 1)} aria-label="Uno más">+</button>
                  </div>
                </li>
              ))}
            </ul>
            <div className="d-flex justify-content-between fs-5 mb-3">
              <span>Total</span><strong className="ta-money">{formatMoney(totals.total)}</strong>
            </div>
            <div className="row g-2">
              <div className="col-sm-6">
                <label className="form-label small mb-1" htmlFor="pm-name">Tu nombre</label>
                <input id="pm-name" className="form-control" required maxLength={100} autoComplete="name"
                  value={name} onChange={(event) => setName(event.target.value)} />
              </div>
              <div className="col-sm-6">
                <label className="form-label small mb-1" htmlFor="pm-phone">Teléfono (WhatsApp)</label>
                <input id="pm-phone" className="form-control" required type="tel" inputMode="tel" maxLength={30} autoComplete="tel"
                  value={phone} onChange={(event) => setPhone(event.target.value)} />
              </div>
              <div className="col-12">
                <label className="form-label small mb-1" htmlFor="pm-notes">Notas (opcional)</label>
                <input id="pm-notes" className="form-control" maxLength={500} placeholder="Ej. para llevar, sin cebolla"
                  value={notes} onChange={(event) => setNotes(event.target.value)} />
              </div>
            </div>
            {error && <div className="alert alert-danger mt-3 mb-0">{error}</div>}
            <button type="submit" className="btn btn-primary btn-touch btn-touch-lg w-100 mt-3" disabled={sending || lines.length === 0}>
              {sending ? 'Enviando…' : `Enviar pedido · ${formatMoney(totals.total)}`}
            </button>
            <p className="small text-muted mt-2 mb-0">
              Usamos tu teléfono solo para avisarte de este pedido.
            </p>
          </form>
        </Offcanvas.Body>
      </Offcanvas>
    </div>
  );
}
