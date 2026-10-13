import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { Modal, Offcanvas } from 'react-bootstrap';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { productsApi } from '../../api/productsApi';
import { ordersApi } from '../../api/ordersApi';
import { cashApi } from '../../api/cashApi';
import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import { useDeliveryPlatformChannels } from '../../api/queries';
import { useDeliveryZones } from '../../api/deliveryZones';
import { useFocusMode } from '../../components/layout/focusMode';
import OpenDrawerModal from '../../components/cash/OpenDrawerModal';
import ChargePanel, { type ChargeResult } from '../../components/sales/ChargePanel';
import PendingSalesModal from '../../components/sales/PendingSalesModal';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { useOnlineStatus } from '../../hooks/useOnlineStatus';
import { usePendingSales } from '../../hooks/usePendingSales';
import { useWorkingBranch } from '../../hooks/useWorkingBranch';
import { buildOfflineTicketOrder } from '../../offline/offlineTicket';
import { enqueueSale, isNetworkError } from '../../offline/saleQueue';
import { newUuid } from '../../utils/uuid';
import type { CreateOrderRequest, OrderDto, ProductDto } from '../../types';
import { BusinessType, OrderChannel, OrderType, TenantCapability } from '../../types';
import { getAccess } from '../../utils/access';
import { formatDateTime } from '../../utils/dates';
import {
  PLATFORM_ONLY_PRODUCTS_MESSAGE,
  getPlatformFolioProblem,
  isDeliveryPlatform,
  normalizePlatformFolio,
  type DeliveryPlatform,
} from '../../utils/deliveryPlatforms';
import { getChannelLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import {
  ADDRESS_LINE_REQUIRED_MESSAGE,
  belowMinimumMessage,
  estimateDeliveryTotals,
  findDeliveryZone,
  meetsMinimum,
  normalizePostalCode,
  notCoveredMessage,
} from '../../utils/homeDelivery';
import { estimateOrderTotals } from '../../utils/orderTotals';
import { isPostalCode } from '../../utils/postalCodes';
import { hasCapability } from '../../utils/tenantCapabilities';
import { allowsFractions } from '../../utils/units';
import { getSelectionError, hasOptions } from '../../utils/productOptions';
import { ROUND_STOCK_WARNING_TITLE, STOCK_WARNING_TITLE, stockWarningMessage } from '../../utils/stockWarnings';
import CartPanel from './CartPanel';
import CatalogPanel from './CatalogPanel';
import CompletedSaleModal from './CompletedSaleModal';
import MeasureModal from './MeasureModal';
import OptionsModal from './OptionsModal';
import SellHeader from './SellHeader';
import { describeSale, readSellDraft, writeSellDraft, type PendingCharge, type SellDraft } from './sellDraft';
import {
  COUNTER_ORIGIN,
  isPlausiblePhone,
  MAX_LINES,
  MAX_QUANTITY_PER_LINE,
  NO_SALE_DETAILS,
  readChatOrigin,
  readCustomerPrefill,
  readTabTarget,
  type CartLine,
  type CategoryFilter,
  type ChatOrigin,
  type CompletedSale,
  type CustomerPrefill,
  type MeasureRequest,
  type OptionsRequest,
  type SaleDetails,
  type TabTarget,
} from './sellModel';
import { useSellCart } from './useSellCart';
import { useSellCatalog } from './useSellCatalog';
import './SellPage.css';

/** The sale on screen as the device keeps it. */
function toDraft(cart: CartLine[], details: SaleDetails): SellDraft {
  return {
    lines: cart.map((line) => ({ productId: line.productId, quantity: line.quantity, notes: line.notes, optionIds: line.optionIds })),
    ...details,
  };
}

/**
 * Vender: touch catalog, ticket and charge in one screen. The cart survives a reload, F9 charges and
 * the server computes the final total (the sale is rejected if a price changed since it was shown).
 */
export default function SellPage() {
  const ticketTitleId = useId();
  const chargeTitleId = useId();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const toast = useToast();
  const { confirm } = useConfirm();
  const access = getAccess(user, capabilities);
  const focus = useFocusMode();
  const canCharge = access.charge;
  const tenantId = tenant?.id ?? '';
  const pricesIncludeTax = tenant?.pricesIncludeTax ?? false;
  const defaultTaxRate = tenant?.defaultTaxRate ?? 16;
  const isFood = tenant?.businessType === BusinessType.Food;
  const canReceiveServices = hasCapability(capabilities, TenantCapability.Service);
  const hasKitchen = hasCapability(capabilities, TenantCapability.Kitchen);

  const warnTooManyLines = useCallback(() => toast.warning(`Una venta admite hasta ${MAX_LINES} productos distintos.`), [toast]);
  const {
    cart,
    setCart,
    toCartLine,
    addOne,
    addChosen,
    setMeasured,
    changeQuantity,
    quantityDrafts,
    typeQuantity,
    finishTypingQuantity,
    updateLine,
  } = useSellCart(defaultTaxRate, warnTooManyLines);
  const [searchParams, setSearchParams] = useSearchParams();
  // A table's tab (new or a new round): read once; the sale then goes to the kitchen instead of being charged.
  const [tabTarget] = useState<TabTarget | null>(() => readTabTarget(searchParams));
  // The sale being built starts where it came from: an order of a delivery platform brought back from the device sells
  // from the whole catalog from its first load (its lines come back with it once the catalog is in).
  const [details, setDetails] = useState<SaleDetails>(() => {
    const draft = tabTarget ? null : readSellDraft(tenantId);
    return draft && isDeliveryPlatform(draft.channel)
      ? { ...NO_SALE_DETAILS, channel: draft.channel, externalOrderNumber: draft.externalOrderNumber, collectedBy: draft.collectedBy }
      : { ...NO_SALE_DETAILS, delivery: draft?.delivery ?? null };
  });
  // A folio left empty says so once a register was attempted, not while the cashier is still choosing products.
  const [folioChecked, setFolioChecked] = useState(false);
  const [showDetails, setShowDetails] = useState(false);
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all');
  const [chargeOpen, setChargeOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [saleError, setSaleError] = useState('');
  const [completedSale, setCompletedSale] = useState<CompletedSale | null>(null);
  const [cartOpenOnPhone, setCartOpenOnPhone] = useState(false);
  const [cashClosed, setCashClosed] = useState(false);
  const [showOpenDrawer, setShowOpenDrawer] = useState(false);
  // Every charge enters a drawer: without one, Cobrar opens the cashier's drawer first and then the charge.
  const [chargeAfterOpening, setChargeAfterOpening] = useState(false);
  const [drawerBranch, setDrawerBranch] = useState<{ id: string; name: string } | null>(null);
  const online = useOnlineStatus();
  const pendingSales = usePendingSales();
  const [showPendingSales, setShowPendingSales] = useState(false);
  // The charge sent for the sale on screen and not confirmed: kept with the draft until the sale is recorded, so a
  // retry, also after a reload, sends the same key and never charges twice. Another cart is another sale.
  const pendingCharge = useRef<PendingCharge | null>(null);
  const workingBranch = useWorkingBranch();
  // The sale is made where the cashier's drawer is; without one, at the branch this device works at.
  const saleBranchId = drawerBranch?.id ?? (workingBranch.hasSeveral ? workingBranch.branch?.id : undefined);
  const navigate = useNavigate();
  const [tabOrder, setTabOrder] = useState<OrderDto | null>(null);
  const [choosing, setChoosing] = useState<OptionsRequest | null>(null);
  // Read once: the link fills the customer after the saved draft is restored. Only a link from a chat (with its
  // conversation) makes it a WhatsApp order; the customer card just fills the customer in.
  const pendingCustomer = useRef<CustomerPrefill | null>(readCustomerPrefill(searchParams));
  const pendingChatOrigin = useRef<ChatOrigin | null>(readChatOrigin(searchParams));
  const [chatOrigin, setChatOrigin] = useState<ChatOrigin | null>(null);
  const [measure, setMeasure] = useState<MeasureRequest | null>(null);
  const draftLoadedFor = useRef('');
  // The cashier chose where the sale comes from before the saved sale was brought back (see syncCart).
  const originChosen = useRef(false);
  const searchInputRef = useRef<HTMLInputElement>(null);

  /** Keeps the ticket in line with the catalog just loaded; the first load brings back the sale being built. */
  const syncCart = useCallback((loaded: ProductDto[]) => {
    const byId = new Map(loaded.map((product) => [product.id, product]));
    if (draftLoadedFor.current !== tenantId) {
      // First load for this business: bring back the sale that was being built (a table's round starts empty).
      draftLoadedFor.current = tenantId;
      const draft = tabTarget ? null : readSellDraft(tenantId);
      if (draft) {
        setCart(draft.lines.flatMap((line) => {
          const product = byId.get(line.productId);
          const optionIds = line.optionIds ?? [];
          // Options that changed since the draft was saved drop the line instead of selling something else.
          if (!product || getSelectionError(product.optionGroups, optionIds)) return [];
          return [toCartLine(product, Math.min(line.quantity, MAX_QUANTITY_PER_LINE), line.notes, optionIds)];
        }));
        // The cashier may have taken the sale to the counter before this first load (it waits without connection):
        // that choice stays, as when going back to the counter at any other moment.
        const toCounter = originChosen.current && isDeliveryPlatform(draft.channel);
        setDetails({
          customerName: draft.customerName,
          customerPhone: draft.customerPhone,
          tableNumber: draft.tableNumber,
          orderNotes: draft.orderNotes,
          channel: draft.channel,
          externalOrderNumber: draft.externalOrderNumber,
          collectedBy: draft.collectedBy,
          delivery: draft.delivery ?? null,
          ...(toCounter ? COUNTER_ORIGIN : {}),
        });
        // The name for the bag of a platform's order is in sight with its folio: behind the link there are only notes.
        setShowDetails(isDeliveryPlatform(draft.channel) && !toCounter
          ? Boolean(draft.orderNotes)
          : Boolean(draft.customerName || draft.customerPhone || draft.tableNumber || draft.orderNotes));
        // A charge left on its way by a reload: the retry sends its key, unless the cart came back different.
        pendingCharge.current = draft.pendingCharge ?? null;
      }
      const customer = pendingCustomer.current;
      if (customer) {
        pendingCustomer.current = null;
        // A customer with a phone (from their card or a chat) is never an order of a platform: it is a counter sale.
        setDetails((current) => ({ ...current, ...COUNTER_ORIGIN, customerPhone: customer.phone, customerName: customer.name ?? '' }));
        setShowDetails(true);
      }
      if (pendingChatOrigin.current) {
        setChatOrigin(pendingChatOrigin.current);
        pendingChatOrigin.current = null;
      }
    } else {
      // Reload (a price changed): keep the cart with current prices and drop what is no longer sold.
      setCart((current) => current.flatMap((line) => {
        const product = byId.get(line.productId);
        if (!product || getSelectionError(product.optionGroups, line.optionIds)) return [];
        return [{ ...toCartLine(product, line.quantity, line.notes, line.optionIds), showNotes: line.showNotes }];
      }));
    }
  }, [setCart, tabTarget, tenantId, toCartLine]);

  // Rappi, Uber Eats and DiDi Food as the business set them up: only the active ones are offered.
  const platformChannels = useDeliveryPlatformChannels(access.sell);
  const activePlatforms = useMemo(
    () => (platformChannels.data ?? []).flatMap((row) => (row.isActive && isDeliveryPlatform(row.channel) ? [row.channel] : [])),
    [platformChannels.data],
  );
  // A sale brought back from the device may come from a platform the business turned off since (or whose list did not
  // load): once that is known, it goes back to the counter.
  if (!platformChannels.isPending && isDeliveryPlatform(details.channel) && !activePlatforms.includes(details.channel)) {
    setDetails((current) => ({ ...current, ...COUNTER_ORIGIN }));
  }
  // While the list is not known yet (without connection it waits), the platform of a sale brought back stays offered:
  // its order is kept as it is, and the cashier can still take Vender back to the counter.
  const offeredPlatforms = useMemo(
    () => (platformChannels.isPending && activePlatforms.length === 0 && isDeliveryPlatform(details.channel)
      ? [details.channel]
      : activePlatforms),
    [activePlatforms, details.channel, platformChannels.isPending],
  );
  // The platform whose app took the order on screen; a chat is a WhatsApp order and a tab goes to a table.
  const platform: DeliveryPlatform | null = !tabTarget && !chatOrigin && isDeliveryPlatform(details.channel) ? details.channel : null;

  const catalog = useSellCatalog(tenantId, syncCart, { includeOutOfSchedule: platform !== null, priceChannel: platform });
  const { products } = catalog;
  // The ticket shows the prices of where the sale comes from (the platform's app, or the menu) once its catalog is in:
  // until then nothing is charged or registered at the prices of the other one.
  const pricesPending = catalog.pricesFor !== (platform ?? null);
  // The options and quantity dialogs show and add a product as the catalog has it now: its prices change with where the
  // sale comes from, also while a dialog is open.
  const choosingNow = useMemo(
    () => choosing && { ...choosing, product: products.find((candidate) => candidate.id === choosing.product.id) ?? choosing.product },
    [choosing, products],
  );
  const measureNow = useMemo(
    () => measure && { ...measure, product: products.find((candidate) => candidate.id === measure.product.id) ?? measure.product },
    [measure, products],
  );

  useEffect(() => {
    // A round goes to an open tab: show what it already has.
    if (tabTarget?.kind !== 'round') return;
    let cancelled = false;
    ordersApi.getById(tabTarget.orderId)
      .then(({ data }) => {
        if (!cancelled) setTabOrder(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setSaleError(getApiErrorMessage(err, 'No se encontró la cuenta.'));
      });
    return () => {
      cancelled = true;
    };
  }, [tabTarget]);

  // The customer is already in the form: a reload must not fill it again.
  useEffect(() => {
    if (searchParams.has('phone')) setSearchParams({}, { replace: true });
  }, [searchParams, setSearchParams]);

  const workingBranchId = workingBranch.branch?.id;
  useEffect(() => {
    // Only people who manage the cash drawer can see it; for them a closed drawer is worth a warning. It is the
    // drawer that would take their payments at the branch this device sells at.
    if (!access.cash || !tenantId) return;
    let cancelled = false;
    cashApi.getCurrentSession(workingBranchId)
      .then(({ data }) => {
        if (cancelled) return;
        setCashClosed(!data);
        setDrawerBranch(data?.isMine && data.branchId ? { id: data.branchId, name: data.branchName ?? '' } : null);
      })
      .catch(() => {
        if (!cancelled) setCashClosed(false);
      });
    return () => {
      cancelled = true;
    };
  }, [access.cash, tenantId, workingBranchId]);

  /** Keeps the sale on screen, with the key of the charge sent for it (a table's round is not kept). */
  const saveDraft = useCallback((draft: SellDraft) => {
    if (!tenantId || draftLoadedFor.current !== tenantId || tabTarget) return;
    writeSellDraft(tenantId, { ...draft, pendingCharge: pendingCharge.current ?? undefined });
  }, [tabTarget, tenantId]);

  useEffect(() => {
    const draft = toDraft(cart, details);
    // The cart changed: the key of the charge sent before belongs to another sale.
    if (pendingCharge.current?.sale !== describeSale(draft)) pendingCharge.current = null;
    saveDraft(draft);
  }, [cart, details, saveDraft]);

  // A weighed product counts as one article, whatever it weighs.
  const itemCount = cart.reduce((sum, line) => sum + (line.fractional ? 1 : line.quantity), 0);
  const hasServices = cart.some((line) => line.isService);

  // To the customer's address (Fase 22C): with the active zones of the business, never for a tab, a platform or services.
  const zones = useDeliveryZones(access.sell && !tabTarget);
  const activeZones = useMemo(() => (zones.data ?? []).filter((zone) => zone.isActive), [zones.data]);
  const deliveryOffered = !tabTarget && !platform && !hasServices && activeZones.length > 0;
  const delivery = deliveryOffered ? details.delivery : null;
  const deliveryZone = delivery ? findDeliveryZone(activeZones, delivery.postalCode) : null;

  const totals = useMemo(() => {
    const lines = cart.map((line) => ({
      unitPrice: line.unitPrice,
      quantity: line.quantity,
      ratePercent: line.ratePercent,
      isExempt: line.isExempt,
    }));
    return deliveryZone
      ? estimateDeliveryTotals(lines, pricesIncludeTax, deliveryZone, defaultTaxRate)
      : { ...estimateOrderTotals(lines, pricesIncludeTax), productsTotal: null, fee: null };
  }, [cart, pricesIncludeTax, deliveryZone, defaultTaxRate]);
  // Quick feedback only: the server finds the zone, checks its minimum and charges its fee.
  const deliveryPostalCode = delivery ? normalizePostalCode(delivery.postalCode) : '';
  const deliveryProblem = !delivery ? ''
    : !isPostalCode(deliveryPostalCode) ? 'Escribe el código postal de la entrega (5 dígitos).'
    : !deliveryZone ? notCoveredMessage(deliveryPostalCode)
    : totals.productsTotal !== null && !meetsMinimum(deliveryZone, totals.productsTotal)
      ? belowMinimumMessage(deliveryPostalCode, deliveryZone.minimumOrder)
      : !delivery.addressLine.trim() ? ADDRESS_LINE_REQUIRED_MESSAGE
      : '';
  // Said once the cashier wrote a whole postal code, not while typing it.
  const deliveryHint = delivery && isPostalCode(deliveryPostalCode) ? deliveryProblem : '';
  // An order of a platform never sends the phone: what is in the hidden field does not stop it.
  const phoneError = !platform && details.customerPhone.trim() && !isPlausiblePhone(details.customerPhone)
    ? 'Revisa el teléfono: 10 dígitos o el número con lada internacional (+52…).'
    : '';
  // Quick feedback only: the server checks the folio, the platform and the products again.
  const folioProblem = platform ? getPlatformFolioProblem(platform, details.externalOrderNumber) : '';
  const platformProblem = platform && hasServices ? PLATFORM_ONLY_PRODUCTS_MESSAGE : folioProblem;
  const folioError = folioProblem && (folioChecked || details.externalOrderNumber.trim()) ? folioProblem : '';

  const categoriesWithProducts = useMemo(() => {
    const used = new Set(products.map((product) => product.categoryId));
    return catalog.categories
      .filter((category) => used.has(category.id))
      .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
  }, [catalog.categories, products]);
  const hasFeatured = products.some((product) => product.isFeatured);

  const visibleProducts = useMemo(() => {
    const term = search.trim().toLowerCase();
    return products
      .filter((product) => {
        if (categoryFilter === 'featured' && !product.isFeatured) return false;
        if (categoryFilter !== 'all' && categoryFilter !== 'featured' && product.categoryId !== categoryFilter) return false;
        if (!term) return true;
        return product.name.toLowerCase().includes(term)
          || (product.sku ?? '').toLowerCase().includes(term)
          || (product.barcode ?? '') === term;
      })
      .sort((a, b) => Number(b.isFeatured) - Number(a.isFeatured) || a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
  }, [categoryFilter, products, search]);

  const addProduct = useCallback((product: ProductDto) => {
    setSaleError('');
    if (hasOptions(product)) {
      // Variants and extras: choose them (and how many) before it goes to the ticket.
      const preselected = (product.optionGroups ?? [])
        .filter((group) => group.minSelections === 1 && group.maxSelections === 1)
        .flatMap((group) => group.options.filter((option) => option.isAvailable).slice(0, 1).map((option) => option.id));
      setChoosing({ product, optionIds: preselected, quantityText: '1' });
      return;
    }
    if (allowsFractions(product.unit)) {
      // Weighed or measured: ask how much instead of adding one.
      const existing = cart.find((line) => line.key === product.id);
      setMeasure({ product, text: existing ? String(existing.quantity) : '', replacing: Boolean(existing) });
      return;
    }
    addOne(product);
  }, [addOne, cart]);

  // Every sale starts at the counter again: the next order of a platform is chosen as it comes.
  const clearSale = () => {
    pendingCharge.current = null;
    setCart([]);
    setDetails(NO_SALE_DETAILS);
    setShowDetails(false);
    setFolioChecked(false);
    setSaleError('');
  };

  /** The counter or a platform: a counter sale carries no folio; another platform keeps the folio typed. */
  const changeChannel = (channel: OrderChannel) => {
    if (draftLoadedFor.current !== tenantId) originChosen.current = true;
    setSaleError('');
    setFolioChecked(false);
    // An order of a platform is taken by its courier: it never goes to an address of the business's zones.
    setDetails((current) => (isDeliveryPlatform(channel) ? { ...current, channel, delivery: null } : { ...current, ...COUNTER_ORIGIN }));
  };

  const handleSearchKeyDown = async (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    const term = search.trim();
    if (!term) return;
    // A barcode scanner types the code and presses Enter: add the exact match.
    const exact = products.find((product) => product.barcode === term || product.sku?.toLowerCase() === term.toLowerCase());
    if (exact) {
      addProduct(exact);
      setSearch('');
      return;
    }
    if (visibleProducts.length === 1) {
      addProduct(visibleProducts[0]);
      setSearch('');
      return;
    }
    if (visibleProducts.length === 0 && catalog.catalogTruncated) {
      try {
        // An order of a platform sells what its app took, whatever the hours here.
        const { data } = await productsApi.getAll({
          isAvailable: true, onlyAvailableNow: !platform, search: term, pageSize: 20, priceChannel: platform ?? undefined,
        });
        if (data.items.length === 1) {
          addProduct(data.items[0]);
          setSearch('');
        } else if (data.items.length > 1) {
          catalog.setProducts((current) => [...current, ...data.items.filter((item) => !current.some((p) => p.id === item.id))]);
        } else {
          toast.info(`No hay productos que coincidan con “${term}”.`);
        }
      } catch (err: unknown) {
        toast.error(getApiErrorMessage(err, 'No se pudo buscar el producto.'));
      }
    }
  };

  const validateSale = (): boolean => {
    if (cart.length === 0) {
      setSaleError('Agrega al menos un producto.');
      return false;
    }
    if (phoneError) {
      setSaleError(phoneError);
      setShowDetails(true);
      return false;
    }
    if (platformProblem) {
      if (folioProblem) setFolioChecked(true);
      setSaleError(platformProblem);
      return false;
    }
    if (deliveryProblem) {
      setSaleError(deliveryProblem);
      return false;
    }
    return true;
  };

  const openCharge = useCallback(() => {
    // A table's round is sent to the kitchen and charged when the table leaves (Mesas). Without the charge
    // permission (a waiter) the order is saved for the cashier.
    if (cart.length === 0 || submitting || tabTarget || !canCharge || pricesPending) return;
    // An order of a platform the courier pays now: its folio first, before taking the money.
    if (platformProblem) {
      if (folioProblem) setFolioChecked(true);
      setSaleError(platformProblem);
      return;
    }
    // The address first, before taking the money: the zone decides what it costs.
    if (deliveryProblem) {
      setSaleError(deliveryProblem);
      return;
    }
    setSaleError('');
    setCartOpenOnPhone(false);
    // Offline the sale waits on the device and its payments enter the drawer open when it is sent.
    if (cashClosed && online) {
      setChargeAfterOpening(true);
      setShowOpenDrawer(true);
      return;
    }
    setChargeOpen(true);
  }, [cart.length, submitting, tabTarget, canCharge, pricesPending, platformProblem, folioProblem, deliveryProblem, cashClosed, online]);

  const buildOrderRequest = (): CreateOrderRequest => ({
    customerName: details.customerName.trim() || undefined,
    // The app of a platform shares neither the customer's phone nor a table.
    customerPhone: platform ? undefined : details.customerPhone.trim() || undefined,
    customerNotes: details.orderNotes.trim() || undefined,
    // An order sent to an address has no table: the address says where it goes.
    tableNumber: isFood && !platform && !delivery ? details.tableNumber.trim() || undefined : undefined,
    // A service in the cart makes it a service order (pieces to track, if the service is trackable).
    orderType: hasServices ? OrderType.Service : OrderType.Retail,
    items: cart.map((line) => ({
      productId: line.productId,
      quantity: line.quantity,
      notes: line.notes.trim() || undefined,
      optionIds: line.optionIds.length > 0 ? line.optionIds : undefined,
    })),
    expectedTotal: totals.total,
    channel: chatOrigin ? OrderChannel.WhatsApp : platform ?? undefined,
    externalOrderNumber: platform ? normalizePlatformFolio(details.externalOrderNumber) : undefined,
    collectedBy: platform ? details.collectedBy : undefined,
    branchId: saleBranchId,
    delivery: delivery
      ? {
        postalCode: normalizePostalCode(delivery.postalCode),
        addressLine: delivery.addressLine.trim(),
        references: delivery.references.trim() || undefined,
      }
      : undefined,
  });

  /** Keeps a charged sale on the device when the connection is down; it is sent with its key when it comes back. */
  const queueOfflineSale = async (request: CreateOrderRequest, charge: ChargeResult) => {
    if (!tenantId || !user) return false;
    const capturedAt = new Date().toISOString();
    try {
      const pending = await enqueueSale({
        id: request.idempotencyKey ?? newUuid(),
        tenantId,
        userId: user.id,
        request: { ...request, idempotencyKey: undefined },
        total: totals.total,
        capturedAt,
      });
      const order = buildOfflineTicketOrder({
        folio: pending.folio,
        capturedAt,
        lines: cart,
        totals,
        pricesIncludeTax,
        payments: charge.payments.map((payment) => ({
          amount: payment.amount,
          method: payment.method,
          reference: payment.reference,
          tip: payment.tip,
          cashReceived: payment.cashReceived,
        })),
        customerName: request.customerName,
        tableNumber: request.tableNumber,
      });
      clearSale();
      setChargeOpen(false);
      setCompletedSale({ order, offline: true });
      return true;
    } catch {
      // No IndexedDB on this device (private window): the sale needs the connection.
      return false;
    }
  };

  /**
   * Sends the sale. A folio of a platform already registered today asks first: short folios repeat, so when it is
   * another order it goes again with the same key, confirmed; null when the cashier goes to check instead.
   */
  const sendSale = async (request: CreateOrderRequest): Promise<OrderDto | null> => {
    try {
      const { data } = await ordersApi.create(request);
      return data;
    } catch (err: unknown) {
      if (getApiErrorCode(err) !== 'possible_duplicate_order' || request.confirmPossibleDuplicate) throw err;
      const confirmed = await confirm({
        title: '¿Es otro pedido?',
        message: `Hoy ya registraste un pedido de ${getChannelLabel(request.channel)} con el folio ${request.externalOrderNumber}. `
          + 'Los folios cortos se repiten: si es otro pedido, regístralo; si no, búscalo en Pedidos.',
        confirmLabel: 'Registrar de todos modos',
        cancelLabel: 'Revisar',
      });
      if (!confirmed) return null;
      const { data } = await ordersApi.create({ ...request, confirmPossibleDuplicate: true });
      return data;
    }
  };

  const submitSale = async (charge: ChargeResult | null) => {
    if (!validateSale()) {
      setChargeOpen(false);
      return;
    }
    setSubmitting(true);
    setSaleError('');
    // Only a counter sale charged now can wait on the device: a tab, a chat order, a sale saved unpaid, a service
    // (its pieces are picked up with the server's folio) or an order of a platform (checked against today's folios)
    // need the server.
    const canQueue = !!charge && !tabTarget && !chatOrigin && !hasServices && !platform && !delivery;
    // The same key on a retry returns this sale instead of charging twice; another cart is another sale.
    const draft = toDraft(cart, details);
    const sale = describeSale(draft);
    const pending = pendingCharge.current?.sale === sale ? pendingCharge.current : { key: newUuid(), sale };
    pendingCharge.current = pending;
    // Saved before sending: a reload while the charge is on its way brings the cart back with this key.
    saveDraft(draft);
    const request: CreateOrderRequest = {
      ...buildOrderRequest(),
      idempotencyKey: pending.key,
      payments: charge?.payments.map((payment) => ({
        amount: payment.amount,
        method: payment.method,
        reference: payment.reference,
        evidenceUrl: payment.evidenceUrl,
        tip: payment.tip,
        cashReceived: payment.cashReceived,
      })),
    };
    if (canQueue && !navigator.onLine && await queueOfflineSale(request, charge)) {
      setSubmitting(false);
      return;
    }
    try {
      const order = await sendSale(request);
      // Nothing was recorded: the cart stays, with its key, while the cashier checks Pedidos.
      if (!order) return;
      const stockWarning = stockWarningMessage(order.stockWarnings);
      if (stockWarning) toast.warning(stockWarning, { title: STOCK_WARNING_TITLE });
      clearSale();
      setChargeOpen(false);
      if (charge) {
        setCompletedSale({ order });
      } else if (isDeliveryPlatform(order.channel)) {
        // The ticket goes in the bag: the order of a platform always ends with it, charged or not.
        setCartOpenOnPhone(false);
        setCompletedSale({ order });
      } else if (chatOrigin) {
        // From a chat the summary goes back to the customer, paid or not.
        setCompletedSale({ order, unpaid: true });
      } else {
        toast.success(canCharge
          ? `Pedido ${order.orderNumber} guardado. Cóbralo desde Pedidos.`
          : `Pedido ${order.orderNumber} guardado: la caja lo cobra desde Pedidos.`);
      }
    } catch (err: unknown) {
      if (canQueue && isNetworkError(err) && await queueOfflineSale(request, charge)) return;
      const message = getApiErrorMessage(err, 'No se pudo registrar la venta.');
      setSaleError(message);
      if (!charge) toast.error(message);
      // A price or availability may have changed: refresh the catalog and the cart prices.
      void catalog.reload();
    } finally {
      setSubmitting(false);
    }
  };

  const sendToKitchen = async () => {
    if (!tabTarget || !validateSale()) return;
    setSubmitting(true);
    setSaleError('');
    try {
      const request = buildOrderRequest();
      const { data: tab } = tabTarget.kind === 'round'
        ? await ordersApi.addRound(tabTarget.orderId, request.items)
        : await ordersApi.create({
          ...request,
          openTab: true,
          orderType: OrderType.Retail,
          diningTableId: tabTarget.tableId,
          // A tab without a table goes by its name.
          tableNumber: tabTarget.tableId ? undefined : tabTarget.label,
        });
      // The round left the stock as it went to the kitchen: the business sells and warns when supplies run short.
      const stockWarning = stockWarningMessage(tab.stockWarnings);
      if (stockWarning) toast.warning(stockWarning, { title: ROUND_STOCK_WARNING_TITLE });
      clearSale();
      toast.success(tabTarget.kind === 'round' ? 'Ronda enviada a cocina.' : `Cuenta de ${tabTarget.label} abierta y enviada a cocina.`);
      navigate('/tables');
    } catch (err: unknown) {
      setSaleError(getApiErrorMessage(err, 'No se pudo enviar a cocina.'));
      void catalog.reload();
    } finally {
      setSubmitting(false);
    }
  };

  const startNewSale = () => {
    setCompletedSale(null);
    setChatOrigin(null);
    window.setTimeout(() => searchInputRef.current?.focus(), 0);
  };

  /**
   * Registers the order of a platform without charging it: the platform charged it in its app (no drawer, no charge
   * permission needed), or the courier pays the business later in Pedidos.
   */
  const registerPlatformOrder = () => {
    if (cart.length === 0 || submitting || !online || pricesPending) return;
    void submitSale(null);
  };

  /** On a phone the folio is typed in the ticket's sheet: when it is missing, the sheet opens to say so. */
  const registerFromPhoneBar = () => {
    if (validateSale()) registerPlatformOrder();
    else setCartOpenOnPhone(true);
  };

  // F9 does the main action of the ticket: charge a counter sale, register an order of a platform. Not while a dialog of
  // Vender is open (charging, opening the drawer, an option, a weight): there it would register past what is on screen.
  // The ticket's sheet on a phone is not one of them: it is the ticket itself.
  const dialogOpen = chargeOpen || showOpenDrawer || choosing !== null || measure !== null || showPendingSales || completedSale !== null;
  const mainAction = useRef<() => void>(() => undefined);
  useEffect(() => {
    mainAction.current = dialogOpen ? () => undefined : platform ? registerPlatformOrder : openCharge;
  });
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'F9') {
        event.preventDefault();
        mainAction.current();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const cartPanel = (
    <CartPanel
      tabTarget={tabTarget}
      tabOrder={tabOrder}
      chatOrigin={chatOrigin}
      onDismissChat={() => setChatOrigin(null)}
      cart={cart}
      quantityDrafts={quantityDrafts}
      onChangeQuantity={changeQuantity}
      onTypeQuantity={typeQuantity}
      onFinishTypingQuantity={finishTypingQuantity}
      onUpdateLine={updateLine}
      onClear={clearSale}
      details={details}
      onDetailsChange={(changes) => setDetails((current) => ({ ...current, ...changes }))}
      showDetails={showDetails}
      onToggleDetails={() => setShowDetails((value) => !value)}
      isFood={isFood}
      phoneError={phoneError}
      totals={totals}
      pricesIncludeTax={pricesIncludeTax}
      saleError={saleError}
      chargeOpen={chargeOpen}
      submitting={submitting}
      canCharge={canCharge}
      onSendToKitchen={() => void sendToKitchen()}
      onOpenCharge={openCharge}
      onSaveUnpaid={() => void submitSale(null)}
      activePlatforms={offeredPlatforms}
      platform={platform}
      onChannelChange={changeChannel}
      folioError={folioError}
      hasServices={hasServices}
      online={online}
      onRegisterPlatformOrder={registerPlatformOrder}
      deliveryOffered={deliveryOffered}
      delivery={delivery}
      onDeliveryChange={(next) => {
        setSaleError('');
        setDetails((current) => ({ ...current, delivery: next }));
      }}
      deliveryZone={deliveryZone}
      deliveryProblem={deliveryHint}
      pricesPending={pricesPending}
      pricesError={catalog.loadError}
      onRetryPrices={() => void catalog.reload()}
    />
  );

  return (
    <div className="sell-page">
      <SellHeader
        focused={focus.isFocused}
        onEnterFocus={focus.enter}
        online={online}
        pendingSales={pendingSales.sales.length}
        failedPendingSales={pendingSales.failedCount}
        onShowPendingSales={() => setShowPendingSales(true)}
        branches={workingBranch.branches}
        hasSeveralBranches={workingBranch.hasSeveral}
        workingBranchId={workingBranch.branch?.id}
        onWorkingBranchChange={workingBranch.setBranch}
        drawerBranch={drawerBranch}
        showServiceIntake={canReceiveServices && access.sell}
        showOrders={access.orders}
      />

      {catalog.offlineCatalogAt && (
        <div className="alert alert-warning mt-3 mb-0" role="status">
          <i className="ri-wifi-off-line me-1" aria-hidden="true"></i>
          Sin conexión: vendes con el catálogo guardado ({formatDateTime(catalog.offlineCatalogAt)}). Las ventas cobradas se guardan
          en este dispositivo y se envían solas al volver la conexión.
        </div>
      )}

      {cashClosed && (
        <div className="alert alert-warning d-flex flex-wrap align-items-center justify-content-between gap-2 mt-3 mb-0" role="status">
          <span>
            <i className="ri-safe-2-line me-1" aria-hidden="true"></i>
            {activePlatforms.length > 0
              ? 'Tu caja está cerrada: ábrela para cobrar. Sin ella puedes guardar pedidos y registrar los que ya cobró una plataforma.'
              : 'Tu caja está cerrada: ábrela para cobrar. Todo cobro se registra en una caja; sin ella solo puedes guardar pedidos.'}
          </span>
          <button type="button" className="btn btn-sm btn-warning" onClick={() => setShowOpenDrawer(true)}>
            Abrir caja
          </button>
        </div>
      )}

      <OpenDrawerModal
        show={showOpenDrawer}
        onHide={() => {
          setShowOpenDrawer(false);
          setChargeAfterOpening(false);
        }}
        branches={workingBranch.branches}
        defaultBranchId={workingBranch.branch?.id}
        onOpened={(session) => {
          setShowOpenDrawer(false);
          setCashClosed(false);
          if (session.isMine && session.branchId) {
            setDrawerBranch({ id: session.branchId, name: session.branchName ?? '' });
            // The device keeps working where the drawer is.
            workingBranch.setBranch(session.branchId);
          }
          toast.success('Tu caja quedó abierta.');
          if (chargeAfterOpening) {
            setChargeAfterOpening(false);
            setChargeOpen(true);
          }
        }}
      />

      <div className="sell-layout mt-3">
        <CatalogPanel
          searchInputRef={searchInputRef}
          search={search}
          onSearchChange={setSearch}
          onSearchKeyDown={(event) => void handleSearchKeyDown(event)}
          categoryFilter={categoryFilter}
          onCategoryChange={setCategoryFilter}
          hasFeatured={hasFeatured}
          categories={categoriesWithProducts}
          loadError={catalog.loadError}
          onRetry={() => void catalog.reload()}
          catalogTruncated={catalog.catalogTruncated}
          loading={catalog.loading}
          hasProducts={products.length > 0}
          visibleProducts={visibleProducts}
          cart={cart}
          onAdd={addProduct}
          canManageCatalog={access.catalog}
        />

        <aside className="sell-cart card d-none d-lg-block" aria-label="Ticket de la venta">
          <div className="card-body">{cartPanel}</div>
        </aside>
      </div>

      {/* Phones: the ticket lives in a bottom sheet behind this bar. */}
      <div className="sell-phone-bar d-lg-none">
        <button type="button" className="btn btn-light btn-touch flex-grow-1" onClick={() => setCartOpenOnPhone(true)}>
          <i className="ri-shopping-basket-2-line me-1" aria-hidden="true"></i>
          {itemCount} {itemCount === 1 ? 'artículo' : 'artículos'} · <span className="ta-money">{formatMoney(totals.total)}</span>
        </button>
        <button
          type="button"
          className="btn btn-primary btn-touch"
          onClick={tabTarget
            ? () => void sendToKitchen()
            : platform ? registerFromPhoneBar : canCharge ? openCharge : () => void submitSale(null)}
          disabled={cart.length === 0 || submitting || pricesPending || (platform !== null && !online)}
        >
          {tabTarget ? 'Enviar' : platform ? 'Registrar' : canCharge ? 'Cobrar' : 'Guardar'}
        </button>
      </div>
      <Offcanvas show={cartOpenOnPhone} onHide={() => setCartOpenOnPhone(false)} placement="bottom" className="sell-sheet" aria-labelledby={ticketTitleId}>
        <Offcanvas.Header closeButton>
          <Offcanvas.Title id={ticketTitleId} as="span" className="visually-hidden">Ticket</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>{cartPanel}</Offcanvas.Body>
      </Offcanvas>

      <OptionsModal
        request={choosingNow}
        onChange={setChoosing}
        onConfirm={(product, optionIds, quantity) => {
          addChosen(product, optionIds, quantity);
          setChoosing(null);
        }}
        onClose={() => setChoosing(null)}
      />

      <MeasureModal
        request={measureNow}
        onChange={setMeasure}
        onConfirm={(product, quantity) => {
          setMeasured(product, quantity);
          setMeasure(null);
        }}
        onClose={() => setMeasure(null)}
      />

      <Modal show={chargeOpen} onHide={() => !submitting && setChargeOpen(false)} centered size="lg" fullscreen="sm-down" aria-labelledby={chargeTitleId}>
        <Modal.Header closeButton={!submitting}>
          <Modal.Title id={chargeTitleId} as="h2" className="h5">Cobrar</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {chargeOpen && (
            <ChargePanel
              amountDue={totals.total}
              submitting={submitting}
              error={saleError}
              // The tip of a platform's order is in its app, for the courier.
              allowTip={!platform}
              onSubmit={(charge) => void submitSale(charge)}
              onCancel={() => setChargeOpen(false)}
              uploadEvidence={async (file) => {
                const { data } = await ordersApi.uploadPaymentEvidence(file);
                return { url: data.url, fileName: data.fileName };
              }}
            />
          )}
        </Modal.Body>
      </Modal>

      {showPendingSales && <PendingSalesModal onClose={() => setShowPendingSales(false)} />}

      <CompletedSaleModal
        sale={completedSale}
        businessName={tenant?.name ?? 'TienditApp'}
        logoUrl={tenant?.logoImages?.small ?? tenant?.logoUrl}
        chatOrigin={chatOrigin}
        canChat={access.chat}
        canCharge={canCharge}
        hasKitchen={hasKitchen}
        onNewSale={startNewSale}
      />
    </div>
  );
}
