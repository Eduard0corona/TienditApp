import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Offcanvas } from 'react-bootstrap';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { accountApi, MAX_ACCOUNT_ADDRESSES, type AccountAddress } from '../../api/accountApi';
import { getApiErrorMessage } from '../../api/errors';
import { publicMenuApi, type PlacePublicOrder, type PublicMenuProduct, type PublicOrderReceipt } from '../../api/publicMenuApi';
import PhotoOrInitial from '../../components/common/PhotoOrInitial';
import FeaturedProducts from '../../components/store/FeaturedProducts';
import MenuProductRow from '../../components/store/MenuProductRow';
import ProductSheet from '../../components/store/ProductSheet';
import StoreHero from '../../components/store/StoreHero';
import StoreInfoModal from '../../components/store/StoreInfoModal';
import CopyTrackingLink from '../../components/tracking/CopyTrackingLink';
import { useActiveSection } from '../../hooks/useActiveSection';
import { useCustomerAccount } from '../../hooks/useCustomerAccount';
import { useScrollFade } from '../../hooks/useScrollFade';
import { formatMoney } from '../../utils/money';
import { describeDay, describeOpeningStatus, describeTodayHours } from '../../utils/openingHours';
import {
  belowMinimumMessage,
  describeDeliveryTerms,
  estimateDeliveryTotals,
  meetsMinimum,
  normalizePostalCode,
  notCoveredMessage,
  type DeliveryTerms,
} from '../../utils/homeDelivery';
import { estimateOrderTotals, resolveProductVat } from '../../utils/orderTotals';
import { isPostalCode } from '../../utils/postalCodes';
import { getOptionsLabel, getPriceWithOptions } from '../../utils/productOptions';
import { localPhone } from '../../utils/phones';
import { readPostalCode } from '../../utils/postalCodePreference';
import {
  MAX_CART_LINES,
  MAX_LINE_QUANTITY,
  cartLineKey,
  lineNotes,
  readCart,
  restoreLines,
  saveCart,
  type CartLine,
} from '../../utils/savedCart';
import { bookingPath } from '../../utils/onlineBooking';
import { featuredProducts, mapsUrl, optionGroupsOf, searchMenu } from '../../utils/storeMenu';
import { previewBusinessTheme } from '../../utils/theme';
import { lineAmount } from '../../utils/units';
import { newUuid } from '../../utils/uuid';
import { businessWhatsAppUrl } from '../../utils/whatsApp';
import { parsePaysWith, paysWithProblem } from '../../utils/courierCash';
import PublicDeliveryFields, { type PublicDeliveryAddress } from './PublicDeliveryFields';
import './PublicMenuPage.css';

type MenuLine = CartLine<PublicMenuProduct>;

/** The sticky bar of categories covers this much of the top while browsing: a section starts below it. */
const TABS_OFFSET = 64;

/** The element of a category's section, for its tab. */
const sectionId = (categoryId: string) => `pm-cat-${categoryId}`;

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
 * order to the customer's address: the server says what it charges to their postal code, and the total adds it. A business
 * that books appointments online (Fase 24D) leads to its booking page from here.
 * Since Fase 24E (ADR 0026) it has the shape of the delivery apps: the business's cover, a search, its featured products,
 * tabs of categories that follow the scroll, rows with the photo on the right and the «+» on it, a sheet for each product
 * with its options, instructions and how many, a floating button with the order, and the order as a checkout page.
 */
export default function PublicMenuPage() {
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
  const [lines, setLines] = useState<MenuLine[]>([]);
  const [viewing, setViewing] = useState<PublicMenuProduct | null>(null);
  const [infoOpen, setInfoOpen] = useState(false);
  const [query, setQuery] = useState('');
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
  // Whether delivery reaches the postal code, whatever the order adds up to: the header says it before the cart.
  const coverageProblem = !delivering || checkingQuote ? ''
    : !isPostalCode(postalCode) ? (postalCode ? 'El código postal lleva 5 dígitos.' : '')
    : quote.isError ? getApiErrorMessage(quote.error, 'No pudimos revisar tu código postal. Intenta de nuevo.')
    : quote.data && !quote.data.covered ? notCoveredMessage(postalCode)
    : '';
  const deliveryProblem = coverageProblem
    || (delivering && !checkingQuote && terms && totals.productsTotal !== null && !meetsMinimum(terms, totals.productsTotal)
      ? belowMinimumMessage(postalCode, terms.minimumOrder)
      : '');
  // «¿Con cuánto pagas?» covers the total with its delivery, once its fee is known.
  const paysWithIssue = delivering && terms ? paysWithProblem(address.paysWith, totals.total) : '';
  const paysWithTyped = parsePaysWith(address.paysWith);
  // To an address the order waits for the server's answer for the postal code.
  const deliveryBlocked = delivering && (checkingQuote || !terms || deliveryProblem !== '' || paysWithIssue !== '');
  const itemCount = lines.reduce((sum, line) => sum + line.quantity, 0);
  const categories = useMemo(() => data?.categories ?? [], [data]);
  const featured = useMemo(() => featuredProducts(categories), [categories]);
  const results = useMemo(() => searchMenu(categories, query), [categories, query]);
  const searching = query.trim() !== '';
  const sectionIds = useMemo(() => categories.map((item) => sectionId(item.id)), [categories]);
  const activeSection = useActiveSection(sectionIds, TABS_OFFSET);
  const { ref: tabsRef, element: tabsRow, fadeClass: tabsFade } = useScrollFade<HTMLDivElement>(categories.length);

  // The tab of the section being read stays in sight in its row.
  useEffect(() => {
    const tab = activeSection ? tabsRow?.querySelector<HTMLElement>(`[data-section="${activeSection}"]`) : null;
    if (!tabsRow || !tab || tabsRow.scrollWidth <= tabsRow.clientWidth) return;
    tabsRow.scrollTo?.({ left: tab.offsetLeft - (tabsRow.clientWidth - tab.offsetWidth) / 2 });
  }, [activeSection, tabsRow]);

  // The cart stays on this device until it is sent or emptied.
  const businessName = data?.businessName;
  useEffect(() => {
    if (restoredFor !== slug || !businessName) return;
    saveCart(slug, {
      businessName,
      lines: lines.map((line) => ({
        productId: line.product.id,
        optionIds: line.optionIds,
        quantity: line.quantity,
        ...(line.notes ? { notes: line.notes } : {}),
      })),
    });
  }, [lines, restoredFor, slug, businessName]);

  const add = (product: PublicMenuProduct, optionIds: string[], note?: string, quantity = 1) => {
    const lineNote = lineNotes(note);
    const key = cartLineKey(product.id, optionIds, lineNote);
    setLines((current) => {
      if (current.some((line) => line.key === key)) {
        return current.map((line) => (line.key === key ? { ...line, quantity: Math.min(line.quantity + quantity, MAX_LINE_QUANTITY) } : line));
      }
      if (current.length >= MAX_CART_LINES) return current;
      const groups = optionGroupsOf(product);
      return [...current, {
        key,
        product,
        optionIds,
        optionsLabel: getOptionsLabel(groups, optionIds),
        unitPrice: getPriceWithOptions(product.price, groups, optionIds),
        quantity: Math.min(quantity, MAX_LINE_QUANTITY),
        ...(lineNote ? { notes: lineNote } : {}),
      }];
    });
  };

  // The «+» adds a product at once; one with options opens its sheet to choose them.
  const quickAdd = (product: PublicMenuProduct) => {
    if (optionGroupsOf(product).length > 0) setViewing(product);
    else add(product, []);
  };
  const inCartOf = (product: PublicMenuProduct) => lines
    .filter((line) => line.product.id === product.id)
    .reduce((sum, line) => sum + line.quantity, 0);
  const canAdd = (product: PublicMenuProduct) => !!data?.acceptsOrders && product.availableNow;

  const goToSection = (categoryId: string) => {
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    document.getElementById(sectionId(categoryId))?.scrollIntoView?.({ behavior: reduced ? 'auto' : 'smooth', block: 'start' });
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
        notes: line.notes ?? null,
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

  const contact = businessWhatsAppUrl(data.contactPhone, `Hola ${data.businessName}, vi su menú en línea.`);
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
  const hoursSummary = !hours ? null
    : !fromServer ? `Hoy: ${describeDay(hours.intervals, hours.today)}`
    : today ? `Hoy: ${describeTodayHours(today)}` : 'Días especiales';
  // Once a product has a photo, the others show their initial so the names line up; without photos, only text.
  const withPhotos = categories.some((item) => item.products.some((product) => product.imageUrl));
  const deliveryHint = !delivering || !isPostalCode(postalCode) ? null
    : checkingQuote ? `Revisando si llegamos a ${postalCode}…`
    : coverageProblem ? <span className="text-danger">{coverageProblem}</span>
    : terms ? `A ${postalCode}: ${describeDeliveryTerms(terms, data.pricesIncludeTax)}`
    : null;

  if (receipt) {
    const followUp = businessWhatsAppUrl(receipt.contactPhone, `Hola, hice el pedido ${receipt.orderNumber} en su menú en línea.`);
    return (
      <div className="pm-page">
        <div className="pm-receipt">
          <span className="pm-receipt-icon"><i className="ri-check-line" aria-hidden="true"></i></span>
          <h1 className="h4">¡Pedido enviado!</h1>
          <p className="text-muted">
            {receipt.confirmed
              ? `${receipt.businessName} ya lo está preparando.`
              : `${receipt.businessName} lo confirmará en unos minutos.`}
          </p>
          <dl className="pm-receipt-card">
            <div><dt>Folio</dt><dd><strong>{receipt.orderNumber}</strong></dd></div>
            {receipt.deliveryFee != null && (
              <div>
                <dt>Envío</dt>
                <dd>{receipt.deliveryFee > 0 ? <span className="ta-money">{formatMoney(receipt.deliveryFee)}</span> : 'Envío gratis'}</dd>
              </div>
            )}
            <div><dt>Total</dt><dd><strong className="ta-money">{formatMoney(receipt.total)}</strong></dd></div>
          </dl>
          {receipt.linkedToAccount && <p className="small mb-0">Lo encuentras en tus pedidos.</p>}
          {receipt.trackingToken && !receipt.linkedToAccount && (
            <p className="small mb-0">Guarda el enlace de seguimiento para ver cómo va tu pedido.</p>
          )}
          <div className="pm-receipt-actions d-grid gap-2 mt-3">
            {receipt.trackingToken && (
              <Link className="btn btn-primary btn-touch" to={`/seguimiento#${receipt.trackingToken}`}>
                <i className="ri-route-line me-1" aria-hidden="true"></i>Seguir tu pedido
              </Link>
            )}
            {/* A guest keeps the link only if it saves it: an account finds the order in Mis pedidos. */}
            {receipt.trackingToken && !receipt.linkedToAccount && <CopyTrackingLink token={receipt.trackingToken} />}
            {followUp && (
              <a className="btn btn-soft-success btn-touch" href={followUp} target="_blank" rel="noopener noreferrer">
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

  const productRow = (product: PublicMenuProduct) => (
    <MenuProductRow
      key={product.id}
      product={product}
      inCart={inCartOf(product)}
      withPhotos={withPhotos}
      canAdd={canAdd(product)}
      onOpen={() => setViewing(product)}
      onAdd={() => quickAdd(product)}
    />
  );

  return (
    <div className="pm-page">
      <div className="pm-shell">
        <StoreHero
          menu={data}
          status={status}
          tableLabel={tableLabel}
          hoursSummary={hoursSummary}
          upcomingNotice={hours?.upcomingNotice ?? null}
          onOpenInfo={() => setInfoOpen(true)}
          delivery={{ offered: deliveryOffered, required: deliveryRequired, toAddress, onChange: setToAddress, hint: deliveryHint }}
          bookingPath={data.booksOnline && !atTable ? bookingPath(slug) : null}
          contactUrl={contact}
        />

        {restoredNotice && (
          <div className="alert alert-info pm-notice d-flex align-items-start gap-2" role="status">
            <span className="flex-grow-1">{restoredNotice}</span>
            <button type="button" className="btn-close" aria-label="Cerrar aviso" onClick={() => setRestoredNotice('')}></button>
          </div>
        )}

        {closedByHours && closedNotice ? (
          <div className="alert alert-info pm-notice" role="status">{closedNotice}</div>
        ) : data.ordersPaused && (
          <div className="alert alert-warning pm-notice" role="status">
            Por ahora no estamos recibiendo pedidos en línea. Puedes ver el menú y volver más tarde.
          </div>
        )}

        {categories.length > 0 && (
          <div className="pm-search">
            <i className="ri-search-line" aria-hidden="true"></i>
            <input
              type="search"
              className="form-control"
              aria-label={`Buscar en ${data.businessName}`}
              placeholder={`Buscar en ${data.businessName}`}
              value={query}
              maxLength={60}
              onChange={(event) => setQuery(event.target.value)}
            />
          </div>
        )}

        {searching ? (
          <main className="pm-main pm-results">
            <h2 className="pm-section-title" aria-live="polite">
              {results.length === 0 ? `No encontramos «${query.trim()}» en el menú` : results.length === 1 ? '1 producto' : `${results.length} productos`}
            </h2>
            <div className="pm-items">{results.map(productRow)}</div>
          </main>
        ) : (
          <>
            {featured.length > 0 && (
              <FeaturedProducts
                products={featured}
                inCart={inCartOf}
                canAdd={canAdd}
                onOpen={setViewing}
                onAdd={quickAdd}
              />
            )}
            <div className="pm-body">
              {categories.length > 0 && (
                <nav className="pm-tabs" aria-label="Categorías">
                  <div ref={tabsRef} className={`pm-tabs-row ta-scroll-fade ${tabsFade}`}>
                    {categories.map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        data-section={sectionId(item.id)}
                        className={`pm-tab ${activeSection === sectionId(item.id) ? 'active' : ''}`}
                        aria-current={activeSection === sectionId(item.id) ? 'true' : undefined}
                        onClick={() => goToSection(item.id)}
                      >
                        {item.name}
                      </button>
                    ))}
                  </div>
                </nav>
              )}
              <main className="pm-main">
                {categories.map((item) => (
                  <section key={item.id} id={sectionId(item.id)} className="pm-section" aria-labelledby={`${sectionId(item.id)}-title`}>
                    <h2 id={`${sectionId(item.id)}-title`} className="pm-section-title">{item.name}</h2>
                    <div className="pm-items">{item.products.map(productRow)}</div>
                  </section>
                ))}
                {categories.length === 0 && <p className="text-muted text-center py-5">El menú todavía no tiene productos.</p>}
              </main>
            </div>
          </>
        )}
      </div>

      {data.acceptsOrders && lines.length > 0 && (
        <div className="pm-cart-bar">
          <button
            type="button"
            className="btn btn-primary pm-cart-button"
            onClick={() => setCartOpen(true)}
            aria-label={`Ver pedido · ${itemCount} · ${formatMoney(totals.total)}`}
          >
            <span className="pm-cart-count ta-money" aria-hidden="true">{itemCount}</span>
            <span className="pm-cart-label" aria-hidden="true">Ver pedido</span>
            <span className="ta-money" aria-hidden="true">{formatMoney(totals.total)}</span>
          </button>
        </div>
      )}

      <ProductSheet
        product={viewing}
        acceptsOrders={data.acceptsOrders}
        onClose={() => setViewing(null)}
        onAdd={(product, optionIds, note, quantity) => {
          add(product, optionIds, note, quantity);
          setViewing(null);
        }}
      />

      <StoreInfoModal
        show={infoOpen}
        onHide={() => setInfoOpen(false)}
        menu={data}
        status={status}
        deliveryOffered={deliveryOffered}
        contactUrl={contact}
      />

      <Offcanvas show={cartOpen} onHide={() => setCartOpen(false)} placement="end" className="pm-checkout" aria-labelledby={cartTitleId}>
        <Offcanvas.Header closeButton>
          <div className="min-w-0">
            <Offcanvas.Title id={cartTitleId} as="h2" className="h5 mb-0">Tu pedido</Offcanvas.Title>
            <div className="small text-muted text-truncate">{data.businessName}</div>
          </div>
        </Offcanvas.Header>
        <Offcanvas.Body>
          <form id="pm-checkout-form" onSubmit={(event) => void send(event)}>
            <section className="pm-checkout-section" aria-labelledby={`${cartTitleId}-items`}>
              <h3 id={`${cartTitleId}-items`} className="pm-checkout-title">Tus productos</h3>
              <ul className="list-unstyled mb-1">
                {lines.map((line) => (
                  <li key={line.key} className="pm-line">
                    {withPhotos && <PhotoOrInitial src={line.product.imageUrl} name={line.product.name} className="pm-line-photo" />}
                    <div className="flex-grow-1 min-w-0">
                      <div className="fw-semibold">{line.product.name}</div>
                      {line.optionsLabel && <div className="small text-muted">{line.optionsLabel}</div>}
                      {line.notes && <div className="small text-muted fst-italic">«{line.notes}»</div>}
                      <div className="small ta-money">{formatMoney(lineAmount(line.unitPrice, line.quantity))}</div>
                    </div>
                    <div className="pm-stepper small" role="group" aria-label={`Cantidad de ${line.product.name}`}>
                      <button type="button" onClick={() => changeQuantity(line.key, -1)} aria-label="Uno menos">
                        <i className={line.quantity === 1 ? 'ri-delete-bin-line' : 'ri-subtract-line'} aria-hidden="true"></i>
                      </button>
                      <span className="ta-money">{line.quantity}</span>
                      <button type="button" onClick={() => changeQuantity(line.key, 1)} aria-label="Uno más">
                        <i className="ri-add-line" aria-hidden="true"></i>
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
              <button type="button" className="btn btn-link px-0" onClick={() => setCartOpen(false)}>
                <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar más productos
              </button>
            </section>

            <section className="pm-checkout-section" aria-labelledby={`${cartTitleId}-delivery`}>
              <h3 id={`${cartTitleId}-delivery`} className="pm-checkout-title">Entrega</h3>
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
              {atTable && <p className="mb-0"><i className="ri-restaurant-line me-1" aria-hidden="true"></i>Te lo llevamos a {tableLabel}.</p>}
              {!atTable && !delivering && (
                data.address ? (
                  <p className="pm-pickup mb-0">
                    <i className="ri-store-2-line" aria-hidden="true"></i>
                    <span className="flex-grow-1">Lo recoges en {data.address}</span>
                    <a className="text-nowrap" href={mapsUrl(data.address)} target="_blank" rel="noopener noreferrer">Cómo llegar</a>
                  </p>
                ) : <p className="text-muted mb-0">Lo recoges en el negocio.</p>
              )}
            </section>

            <section className="pm-checkout-section" aria-labelledby={`${cartTitleId}-you`}>
              <h3 id={`${cartTitleId}-you`} className="pm-checkout-title">Tus datos</h3>
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
                  <label className="form-label small mb-1" htmlFor="pm-notes">Notas para el negocio (opcional)</label>
                  <input id="pm-notes" className="form-control" maxLength={500} placeholder="Ej. para llevar, timbre descompuesto"
                    value={notes} onChange={(event) => setNotes(event.target.value)} />
                </div>
              </div>
            </section>

            <section className="pm-checkout-section pm-summary" aria-labelledby={`${cartTitleId}-summary`}>
              <h3 id={`${cartTitleId}-summary`} className="pm-checkout-title">Resumen</h3>
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
              <div className="d-flex justify-content-between fs-5">
                <span>Total</span><strong className="ta-money">{formatMoney(totals.total)}</strong>
              </div>
            </section>
            {error && <div className="alert alert-danger mb-0" role="alert">{error}</div>}
          </form>
        </Offcanvas.Body>
        <div className="pm-checkout-footer">
          <button type="submit" form="pm-checkout-form" className="btn btn-primary btn-touch btn-touch-lg w-100" disabled={sending || lines.length === 0 || deliveryBlocked}>
            {sending ? 'Enviando…' : `Hacer pedido · ${formatMoney(totals.total)}`}
          </button>
          <p className="small text-muted mt-2 mb-0">Usamos tu teléfono solo para avisarte de este pedido.</p>
        </div>
      </Offcanvas>
    </div>
  );
}
