import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Modal, Offcanvas } from 'react-bootstrap';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, MAX_ACCOUNT_ADDRESSES, type AccountAddress } from '../../api/accountApi';
import { getApiErrorMessage } from '../../api/errors';
import { publicMenuApi, type PlacePublicOrder, type PublicMenuProduct, type PublicOrderReceipt } from '../../api/publicMenuApi';
import PhotoOrInitial from '../../components/common/PhotoOrInitial';
import { useCustomerAccount } from '../../hooks/useCustomerAccount';
import { useScrollFade } from '../../hooks/useScrollFade';
import type { ProductOptionGroupDto } from '../../types';
import { formatMoney } from '../../utils/money';
import {
  WEEK,
  dayLabel,
  describeDay,
  describeOpeningStatus,
  describeSpecialDay,
  describeTodayHours,
} from '../../utils/openingHours';
import {
  belowMinimumMessage,
  estimateDeliveryTotals,
  meetsMinimum,
  normalizePostalCode,
  notCoveredMessage,
  type DeliveryTerms,
} from '../../utils/homeDelivery';
import { estimateOrderTotals, resolveProductVat } from '../../utils/orderTotals';
import { isPostalCode } from '../../utils/postalCodes';
import {
  getLineKey,
  getOptionsLabel,
  getPriceWithOptions,
  getSelectionError,
} from '../../utils/productOptions';
import { localPhone } from '../../utils/phones';
import { readPostalCode } from '../../utils/postalCodePreference';
import {
  MAX_CART_LINES,
  MAX_LINE_QUANTITY,
  readCart,
  restoreLines,
  saveCart,
  type CartLine,
} from '../../utils/savedCart';
import { previewBusinessTheme } from '../../utils/theme';
import { lineAmount } from '../../utils/units';
import { newUuid } from '../../utils/uuid';
import { parsePaysWith, paysWithProblem } from '../../utils/courierCash';
import PublicDeliveryFields, { type PublicDeliveryAddress } from './PublicDeliveryFields';
import './PublicMenuPage.css';

type MenuLine = CartLine<PublicMenuProduct>;

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

const NO_ADDRESS: PublicDeliveryAddress = { postalCode: '', addressLine: '', references: '', paysWith: '' };

/**
 * What came back of a saved cart, or of «Pedir otra vez»; '' when everything did. `skipped` are the products of the
 * order sold by weight, which the cart does not take.
 */
function restoredMessage(kept: number, dropped: number, skipped: number, reorder: boolean): string {
  if (dropped === 0 && skipped === 0) return '';
  if (kept === 0) {
    return reorder
      ? 'Los productos de ese pedido ya no están disponibles ahora. Mira el menú de hoy.'
      : 'Los productos que dejaste en tu pedido ya no están disponibles ahora.';
  }
  const missing = [
    dropped === 1 ? '1 producto ya no está en el menú de ahora' : '',
    dropped > 1 ? `${dropped} productos ya no están en el menú de ahora` : '',
    skipped === 1 ? '1 producto se pide por peso: agrégalo con su cantidad' : '',
    skipped > 1 ? `${skipped} productos se piden por peso: agrégalos con su cantidad` : '',
  ].filter(Boolean);
  return `Agregamos lo que sigue disponible; ${missing.join('; ')}.`;
}

/**
 * The public menu of a business (/m/:slug), without signing in: the catalog the business published and, when
 * it takes orders, a cart that sends the order with the customer's name and phone. A table QR adds ?mesa=.
 * The cart stays on this device until it is sent, and «Pedir otra vez» fills it with what the menu still has. With a
 * customer account (Fase 24B) the name and phone are already written, its addresses are offered and the order is in
 * its orders.
 * Its header says whether the business is open now (the API works it out in the business's time and writes the words),
 * its hours of the week, its special days ahead and its address with a map. A business with delivery zones takes the
 * order to the customer's address: the server says what it charges to their postal code, and the total adds it.
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
  const [toAddress, setToAddress] = useState(false);
  const [address, setAddress] = useState<PublicDeliveryAddress>(NO_ADDRESS);
  const queryClient = useQueryClient();
  const { customer, signedIn } = useCustomerAccount();
  // The saved address chosen; null while another one is written; undefined until the cart first goes to an address.
  const [addressChoice, setAddressChoice] = useState<string | null | undefined>(undefined);
  const [keepAddress, setKeepAddress] = useState(true);
  const [addressLabel, setAddressLabel] = useState('');
  const [restoredFor, setRestoredFor] = useState<string | null>(null);
  const [restoredNotice, setRestoredNotice] = useState('');
  const [prefilledFor, setPrefilledFor] = useState<string | null>(null);
  // The key of the order being sent: the same order sent again (an answer lost) keeps it, and the server places it once.
  const attempt = useRef<{ signature: string; key: string } | null>(null);

  // At one of its tables the customer is already there; a kitchen without premises with zones only delivers.
  const deliveryOffered = !!data?.deliveryAvailable && !atTable;
  const deliveryRequired = deliveryOffered && !!data?.deliveryOnly;
  const delivering = deliveryOffered && (deliveryRequired || toAddress);
  const addresses = useQuery({
    queryKey: ['me', 'addresses'],
    queryFn: ({ signal }) => accountApi.getAddresses(signal),
    enabled: !!customer && deliveryOffered,
  });
  const savedAddresses = useMemo(() => (customer ? addresses.data ?? [] : []), [customer, addresses.data]);

  // The customer account's name and phone, written once its session is known; they can still be changed.
  if (customer && prefilledFor !== customer.id) {
    setPrefilledFor(customer.id);
    setName((current) => current || `${customer.firstName} ${customer.lastName}`.trim());
    setPhone((current) => current || localPhone(customer.phoneNumber));
  }

  // The cart this device kept for this store, with what its menu has now (and «Pedir otra vez»), once per store.
  if (data && restoredFor !== slug) {
    setRestoredFor(slug);
    const saved = readCart(slug);
    if (saved) {
      const restored = restoreLines(saved.lines, data.categories.flatMap((item) => item.products));
      setLines(restored.lines);
      setRestoredNotice(restoredMessage(restored.lines.length, restored.dropped, saved.skipped ?? 0, saved.reorderOf !== undefined));
    }
  }

  // The first time the order goes to an address: the account's last one used, or the postal code given in Explorar.
  const chooseAddress = (saved: AccountAddress | null) => {
    setAddressChoice(saved?.id ?? null);
    setAddress((current) => (saved
      ? { ...current, postalCode: saved.postalCode, addressLine: saved.addressLine, references: saved.references ?? '' }
      : { ...current, postalCode: readPostalCode(), addressLine: '', references: '' }));
  };
  if (delivering && addressChoice === undefined && (!customer || !addresses.isPending)) {
    // Not over an address the customer started writing while the account's ones loaded.
    if (savedAddresses[0] && address.addressLine.trim() === '') {
      chooseAddress(savedAddresses[0]);
    } else {
      setAddressChoice(null);
      const remembered = readPostalCode();
      if (remembered && !address.postalCode) setAddress((current) => ({ ...current, postalCode: remembered }));
    }
  }
  const usingSavedAddress = savedAddresses.some((saved) => saved.id === addressChoice);
  const canKeepAddress = !!customer && !usingSavedAddress && savedAddresses.length < MAX_ACCOUNT_ADDRESSES;
  const postalCode = normalizePostalCode(address.postalCode);
  const quote = useQuery({
    queryKey: ['public-delivery-quote', slug, postalCode],
    queryFn: () => publicMenuApi.getDeliveryQuote(slug, postalCode),
    enabled: delivering && isPostalCode(postalCode),
    retry: false,
    staleTime: 60_000,
  });
  const covered = delivering && isPostalCode(postalCode) && quote.data?.covered ? quote.data : null;
  const terms = useMemo<DeliveryTerms | null>(() => (covered
    ? { fee: covered.fee ?? 0, minimumOrder: covered.minimumOrder ?? 0, freeDeliveryFrom: covered.freeDeliveryFrom }
    : null), [covered]);

  const totals = useMemo(() => {
    const totalsLines = lines.map((line) => ({
      unitPrice: line.unitPrice,
      quantity: line.quantity,
      ...resolveProductVat(line.product, data?.defaultTaxRate ?? 16),
    }));
    const pricesIncludeTax = data?.pricesIncludeTax ?? true;
    return terms
      ? estimateDeliveryTotals(totalsLines, pricesIncludeTax, terms, data?.defaultTaxRate ?? 16)
      : { ...estimateOrderTotals(totalsLines, pricesIncludeTax), productsTotal: null, fee: null };
  }, [data, lines, terms]);
  const checkingQuote = delivering && isPostalCode(postalCode) && quote.isFetching;
  const deliveryProblem = !delivering || checkingQuote ? ''
    : !isPostalCode(postalCode) ? (postalCode ? 'El código postal lleva 5 dígitos.' : '')
    : quote.isError ? getApiErrorMessage(quote.error, 'No pudimos revisar tu código postal. Intenta de nuevo.')
    : quote.data && !quote.data.covered ? notCoveredMessage(postalCode)
    : terms && totals.productsTotal !== null && !meetsMinimum(terms, totals.productsTotal)
      ? belowMinimumMessage(postalCode, terms.minimumOrder)
      : '';
  // «¿Con cuánto pagas?» covers the total with its delivery, once its fee is known.
  const paysWithIssue = delivering && terms ? paysWithProblem(address.paysWith, totals.total) : '';
  const paysWithTyped = parsePaysWith(address.paysWith);
  // To an address the order waits for the server's answer for the postal code.
  const deliveryBlocked = delivering && (checkingQuote || !terms || deliveryProblem !== '' || paysWithIssue !== '');
  const itemCount = lines.reduce((sum, line) => sum + line.quantity, 0);
  const categories = data?.categories ?? [];
  const visible = category === 'all' ? categories : categories.filter((item) => item.id === category);
  const { ref: chipsRef, fadeClass: chipsFade } = useScrollFade<HTMLDivElement>(categories.length);

  // The cart stays on this device until it is sent or emptied.
  const businessName = data?.businessName;
  useEffect(() => {
    if (restoredFor !== slug || !businessName) return;
    saveCart(slug, {
      businessName,
      lines: lines.map((line) => ({ productId: line.product.id, optionIds: line.optionIds, quantity: line.quantity })),
    });
  }, [lines, restoredFor, slug, businessName]);

  const add = (product: PublicMenuProduct, optionIds: string[]) => {
    const key = getLineKey(product.id, optionIds);
    setLines((current) => {
      if (current.some((line) => line.key === key)) {
        return current.map((line) => (line.key === key ? { ...line, quantity: Math.min(line.quantity + 1, MAX_LINE_QUANTITY) } : line));
      }
      if (current.length >= MAX_CART_LINES) return current;
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
    .map((line) => (line.key === key ? { ...line, quantity: Math.min(line.quantity + delta, MAX_LINE_QUANTITY) } : line))
    .filter((line) => line.quantity > 0));

  const send = async (event: React.FormEvent) => {
    event.preventDefault();
    if (lines.length === 0 || sending || deliveryBlocked) return;
    setSending(true);
    setError('');
    const keeping = delivering && canKeepAddress && keepAddress;
    const order: PlacePublicOrder = {
      customerName: name.trim(),
      customerPhone: phone.trim(),
      notes: notes.trim() || null,
      tableLabel: tableLabel || null,
      delivery: delivering
        ? {
          postalCode,
          addressLine: address.addressLine.trim(),
          references: address.references.trim() || null,
          paysWith: typeof paysWithTyped === 'number' ? paysWithTyped : null,
        }
        : undefined,
      items: lines.map((line) => ({
        productId: line.product.id,
        quantity: line.quantity,
        optionIds: line.optionIds,
        notes: null,
      })),
      expectedTotal: totals.total,
      saveAddress: keeping,
      addressLabel: keeping ? addressLabel.trim() || null : null,
    };
    // Another order (a product, the address or the total changed) gets another key.
    const signature = JSON.stringify(order);
    if (attempt.current?.signature !== signature) attempt.current = { signature, key: newUuid() };
    try {
      const result = await publicMenuApi.placeOrder(slug, { ...order, idempotencyKey: attempt.current.key });
      attempt.current = null;
      setReceipt(result);
      setLines([]);
      setCartOpen(false);
      setRestoredNotice('');
      if (customer && delivering) void queryClient.invalidateQueries({ queryKey: ['me', 'addresses'] });
      if (result.linkedToAccount) void queryClient.invalidateQueries({ queryKey: ['me', 'orders'] });
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
  // The API writes what the menu says about its hours (todayDate tells); an older one sends only the times.
  const fromServer = hours?.todayDate != null;
  const legacyStatus = hours && !fromServer ? describeOpeningStatus(hours) : null;
  // Without weekly hours and without a special day today there is no status to show (statusLabel is null).
  const status = hours && fromServer
    ? (hours.statusLabel ? { isOpen: hours.isOpen, label: hours.statusLabel, detail: hours.statusDetail ?? null } : null)
    : legacyStatus;
  // Paused while it is closed: its hours say when to come back (at one of its tables the API takes the order outside
  // its hours, but not on a special day it closes).
  const closedByHours = data.ordersPaused && hours?.isOpen === false;
  const closedNotice = hours && fromServer
    ? hours.closedNotice
    : `Fuera de horario no recibimos pedidos en línea${legacyStatus?.when ? `: vuelve ${legacyStatus.when}` : ''}. Mientras, puedes ver el menú.`;
  const today = hours?.todayHours;
  const specialDays = hours?.specialDays ?? [];
  const hoursSummary = !hours ? null
    : !fromServer ? `Hoy: ${describeDay(hours.intervals, hours.today)}`
    : today ? `Hoy: ${describeTodayHours(today)}` : 'Días especiales';
  // Without weekly hours and without special days ahead (a special day today alone) there is nothing to open.
  const hoursOpen = !!hours && (hours.intervals.length > 0 || specialDays.length > 0);
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
          {receipt.deliveryFee != null && (
            <p className="mb-1 small">
              {receipt.deliveryFee > 0 ? <>Incluye envío de <span className="ta-money">{formatMoney(receipt.deliveryFee)}</span></> : 'Envío gratis'}
            </p>
          )}
          <p className="text-muted">
            {receipt.confirmed
              ? `${receipt.businessName} ya lo está preparando.`
              : `${receipt.businessName} lo confirmará en unos minutos.`}
          </p>
          {receipt.linkedToAccount && <p className="small mb-0">Lo encuentras en tus pedidos.</p>}
          <div className="d-grid gap-2 mt-3" style={{ maxWidth: '20rem', margin: '0 auto' }}>
            {followUp && (
              <a className="btn btn-success btn-touch" href={followUp} target="_blank" rel="noopener noreferrer">
                <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Avisar por WhatsApp
              </a>
            )}
            {receipt.linkedToAccount && <Link className="btn btn-light btn-touch" to="/mis-pedidos">Ver mis pedidos</Link>}
            <button type="button" className="btn btn-light btn-touch" onClick={() => setReceipt(null)}>Hacer otro pedido</button>
          </div>
          {!signedIn && (
            <p className="small text-muted mt-3 mb-0">
              ¿Pides seguido? <Link to="/register?cuenta=cliente">Crea tu cuenta</Link> y tus datos quedan listos para la próxima vez.
            </p>
          )}
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

      {(hours || data.address || data.deliveryOnly || deliveryOffered) && (
        <section className="pm-info" aria-label="Horario y ubicación">
          {hours && !hoursOpen && (
            <p className="pm-today">
              <i className="ri-time-line" aria-hidden="true"></i>
              <span className="flex-grow-1">{hoursSummary}</span>
            </p>
          )}
          {hours && hoursOpen && (
            <details className="pm-hours">
              <summary>
                <i className="ri-time-line" aria-hidden="true"></i>
                <span className="flex-grow-1">{hoursSummary}</span>
                <i className="ri-arrow-down-s-line pm-hours-toggle" aria-hidden="true"></i>
              </summary>
              {hours.intervals.length > 0 && (
                <ul className="pm-week list-unstyled" aria-label="Horario de la semana">
                  {WEEK.map((day) => (
                    <li key={day} aria-current={day === hours.today ? 'date' : undefined}>
                      <span>{dayLabel(day)}</span>
                      <span>{describeDay(hours.intervals, day)}</span>
                    </li>
                  ))}
                </ul>
              )}
              {specialDays.length > 0 && (
                <ul className="pm-special-days list-unstyled" aria-label="Días especiales">
                  {specialDays.map((day) => <li key={day.from}>{describeSpecialDay(day)}</li>)}
                </ul>
              )}
            </details>
          )}
          {hours?.upcomingNotice && (
            <p className="pm-upcoming text-muted">
              <i className="ri-calendar-event-line" aria-hidden="true"></i>
              <span className="flex-grow-1">{hours.upcomingNotice}</span>
            </p>
          )}
          {/* A kitchen without premises: the food goes to the customer, and the server sends no address. */}
          {data.deliveryOnly && (
            <p className="pm-address">
              <i className="ri-e-bike-2-line" aria-hidden="true"></i>
              <span className="flex-grow-1">Solo a domicilio: no tenemos local para visitar.</span>
            </p>
          )}
          {deliveryOffered && !data.deliveryOnly && (
            <p className="pm-address">
              <i className="ri-e-bike-2-line" aria-hidden="true"></i>
              <span className="flex-grow-1">También a domicilio: al pedir te decimos si llegamos a tu código postal.</span>
            </p>
          )}
          {data.address && !data.deliveryOnly && (
            <p className="pm-address">
              <i className="ri-map-pin-line" aria-hidden="true"></i>
              <span className="flex-grow-1">{data.address}</span>
              <a className="text-nowrap" href={mapsUrl(data.address)} target="_blank" rel="noopener noreferrer">Cómo llegar</a>
            </p>
          )}
        </section>
      )}

      {restoredNotice && (
        <div className="alert alert-info pm-restored d-flex align-items-start gap-2" role="status">
          <span className="flex-grow-1">{restoredNotice}</span>
          <button type="button" className="btn-close" aria-label="Cerrar aviso" onClick={() => setRestoredNotice('')}></button>
        </div>
      )}

      {closedByHours && closedNotice ? (
        <div className="alert alert-info mx-3 mt-3 mb-0" role="status">{closedNotice}</div>
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
            {deliveryOffered && (
              <PublicDeliveryFields
                required={deliveryRequired}
                toAddress={toAddress}
                onToAddressChange={setToAddress}
                address={address}
                onAddressChange={(changes) => setAddress((current) => ({ ...current, ...changes }))}
                terms={terms}
                checking={checkingQuote}
                problem={deliveryProblem}
                pricesIncludeTax={data.pricesIncludeTax}
                total={terms ? totals.total : null}
                savedAddresses={savedAddresses}
                selectedAddressId={addressChoice ?? null}
                onSelectAddress={chooseAddress}
                save={canKeepAddress
                  ? { checked: keepAddress, label: addressLabel, onCheckedChange: setKeepAddress, onLabelChange: setAddressLabel }
                  : null}
              />
            )}
            {totals.fee !== null && totals.productsTotal !== null && (
              <>
                {/* What the customer pays for each, with IVA, so the two add up to the total. */}
                <div className="d-flex justify-content-between small">
                  <span>Productos</span><span className="ta-money">{formatMoney(totals.productsTotal)}</span>
                </div>
                <div className="d-flex justify-content-between small">
                  <span>Envío a domicilio</span>
                  <span className="ta-money">
                    {totals.fee > 0 ? formatMoney(Math.round((totals.total - totals.productsTotal) * 100) / 100) : 'Gratis'}
                  </span>
                </div>
              </>
            )}
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
            <button type="submit" className="btn btn-primary btn-touch btn-touch-lg w-100 mt-3" disabled={sending || lines.length === 0 || deliveryBlocked}>
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
