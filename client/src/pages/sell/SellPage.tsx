import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { productsApi } from '../../api/productsApi';
import { appointmentsApi } from '../../api/appointmentsApi';
import { ordersApi } from '../../api/ordersApi';
import { getApiErrorMessage } from '../../api/errors';
import { useCustomerAccount, useRefreshCustomerCredit } from '../../api/customerCredit';
import { useFocusMode } from '../../components/layout/focusMode';
import OpenDrawerModal from '../../components/cash/OpenDrawerModal';
import type { ChargeResult } from '../../components/sales/ChargePanel';
import PendingSalesModal from '../../components/sales/PendingSalesModal';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { useOnlineStatus } from '../../hooks/useOnlineStatus';
import { usePendingSales } from '../../hooks/usePendingSales';
import { useWorkingBranch } from '../../hooks/useWorkingBranch';
import { isNetworkError } from '../../offline/saleQueue';
import { newUuid } from '../../utils/uuid';
import type { CreateOrderRequest, ProductDto } from '../../types';
import { BusinessType, OrderChannel, OrderPaymentMethod, OrderType, TenantCapability } from '../../types';
import { getAccess } from '../../utils/access';
import { isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { appointmentSaleProblem } from '../../utils/homeVisits';
import { hasCapability } from '../../utils/tenantCapabilities';
import { allowsFractions } from '../../utils/units';
import { hasOptions } from '../../utils/productOptions';
import { ROUND_STOCK_WARNING_TITLE, STOCK_WARNING_TITLE, stockWarningMessage } from '../../utils/stockWarnings';
import CartPanel from './CartPanel';
import CatalogPanel from './CatalogPanel';
import ChargeModal from './ChargeModal';
import CompletedSaleModal from './CompletedSaleModal';
import MeasureModal from './MeasureModal';
import OptionsModal from './OptionsModal';
import SellHeader from './SellHeader';
import SellNotices from './SellNotices';
import SellPhoneBar from './SellPhoneBar';
import { keepSaleOnDevice } from './offlineSale';
import { checkSale } from './saleChecks';
import { buildOrderRequest, chargeFundsOf, countArticles, saleTotals } from './saleRequest';
import { sendSale } from './sendSale';
import {
  describeSale,
  initialDetails,
  linesFromDraft,
  readSellDraft,
  refreshLines,
  restoreDetails,
  toDraft,
  writeSellDraft,
  type PendingCharge,
  type SellDraft,
} from './sellDraft';
import {
  COUNTER_ORIGIN,
  MAX_LINES,
  NO_SALE_DETAILS,
  categoriesInUse,
  preselectedOptions,
  readAppointmentId,
  readChatOrigin,
  readCustomerPrefill,
  readTabTarget,
  saleOfAppointment,
  scannedProduct,
  filterCatalog,
  type CategoryFilter,
  type ChatOrigin,
  type CompletedSale,
  type CustomerPrefill,
  type MeasureRequest,
  type OptionsRequest,
  type SaleDetails,
  type TabTarget,
} from './sellModel';
import { useCashDrawer } from './useCashDrawer';
import { useF9 } from './useF9';
import { useSaleDelivery } from './useSaleDelivery';
import { useSalePlatforms } from './useSalePlatforms';
import { useSellCart } from './useSellCart';
import { useSellCatalog } from './useSellCatalog';
import { useTabOrder } from './useTabOrder';
import './SellPage.css';

/**
 * Vender: touch catalog, ticket and charge in one screen. The cart survives a reload, F9 charges and
 * the server computes the final total (the sale is rejected if a price changed since it was shown).
 */
export default function SellPage() {
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
  // The sale being built starts where it came from (an order of a platform brought back sells from the whole catalog).
  const [details, setDetails] = useState<SaleDetails>(() => initialDetails(tabTarget ? null : readSellDraft(tenantId)));
  // A folio left empty says so once a register was attempted, not while the cashier is still choosing products.
  const [folioChecked, setFolioChecked] = useState(false);
  // A long id half typed says so once the cashier leaves it or tries to register, not at its first character.
  const [orderIdChecked, setOrderIdChecked] = useState(false);
  const [showDetails, setShowDetails] = useState(false);
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all');
  const [chargeOpen, setChargeOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [saleError, setSaleError] = useState('');
  const [completedSale, setCompletedSale] = useState<CompletedSale | null>(null);
  const [cartOpenOnPhone, setCartOpenOnPhone] = useState(false);
  const [showOpenDrawer, setShowOpenDrawer] = useState(false);
  // Every charge enters a drawer: without one, Cobrar opens the cashier's drawer first and then the charge.
  const [chargeAfterOpening, setChargeAfterOpening] = useState(false);
  const online = useOnlineStatus();
  const pendingSales = usePendingSales();
  const [showPendingSales, setShowPendingSales] = useState(false);
  // The charge sent for the sale on screen and not confirmed: kept with the draft until the sale is recorded, so a
  // retry, also after a reload, sends the same key and never charges twice. Another cart is another sale.
  const pendingCharge = useRef<PendingCharge | null>(null);
  const workingBranch = useWorkingBranch();
  const navigate = useNavigate();
  const [choosing, setChoosing] = useState<OptionsRequest | null>(null);
  // Read once: the link fills the customer after the saved draft is restored. Only a link from a chat (with its
  // conversation) makes it a WhatsApp order; the customer card just fills the customer in.
  const pendingCustomer = useRef<CustomerPrefill | null>(readCustomerPrefill(searchParams));
  const pendingChatOrigin = useRef<ChatOrigin | null>(readChatOrigin(searchParams));
  // «Cobrar» in the agenda (?appointment=): read once, after the saved sale is brought back.
  const pendingAppointment = useRef<string | null>(readAppointmentId(searchParams));
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
        setCart(linesFromDraft(draft.lines, byId, toCartLine));
        // The cashier may have taken the sale to the counter before this first load (it waits without connection):
        // that choice stays, as when going back to the counter at any other moment.
        const restored = restoreDetails(draft, originChosen.current && isDeliveryPlatform(draft.channel));
        setDetails(restored.details);
        setShowDetails(restored.showDetails);
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
      setCart((current) => refreshLines(current, byId, toCartLine));
    }
  }, [setCart, tabTarget, tenantId, toCartLine]);

  const platforms = useSalePlatforms(access.sell, details.channel);
  // A sale brought back from a platform turned off since goes back to the counter.
  if (platforms.turnedOff) setDetails((current) => ({ ...current, ...COUNTER_ORIGIN }));
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

  const tabOrder = useTabOrder(tabTarget, setSaleError);

  // The customer (or the appointment) is already in the sale: a reload must not fill it again.
  useEffect(() => {
    if (searchParams.has('phone') || searchParams.has('appointment')) setSearchParams({}, { replace: true });
  }, [searchParams, setSearchParams]);

  // The appointment to charge: its customer, its service of the catalog and its visit charge. The server checks it again
  // and charges what the appointment says.
  const chargeAppointment = useCallback(async (appointmentId: string) => {
    try {
      const { data } = await appointmentsApi.getById(appointmentId);
      const problem = appointmentSaleProblem(data);
      if (problem) {
        toast.warning(problem);
        return;
      }
      // Another sale was on screen: the charge of the appointment takes its place only if the cashier says so.
      if (cart.length > 0 && !(await confirm({
        title: '¿Cobrar la cita?',
        message: 'Hay otra venta en el ticket. El cobro de la cita la reemplaza.',
        confirmLabel: 'Cobrar la cita',
        cancelLabel: 'Seguir con la venta',
      }))) return;
      const { appointment, service, freeVisit } = saleOfAppointment(data, products);
      pendingCharge.current = null;
      setCart(service ? [toCartLine(service, 1, '', [])] : []);
      // A counter sale of the business, for the customer of the appointment: no platform and no delivery.
      setDetails({ ...NO_SALE_DETAILS, customerName: data.customerName, customerPhone: data.customerPhone, appointment });
      setShowDetails(true);
      setSaleError('');
      if (freeVisit) toast.info('La visita es sin costo: agrega al ticket lo que cobres.');
      else if (!service) toast.info('Agrega al ticket el servicio de la cita.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se encontró la cita.'));
    }
  }, [cart.length, confirm, products, setCart, toCartLine, toast]);

  // The appointment's deposit as it holds it now (Fase 31O). The server refuses a sale that took off another one (returned
  // at the agenda meanwhile, or a sale saved on the device before it was taken off): the screen reads it again, so the
  // ticket and the next charge say what the server holds.
  const refreshAppointmentDeposit = useCallback(async (appointmentId: string) => {
    try {
      const { data } = await appointmentsApi.getById(appointmentId);
      const { deposit } = saleOfAppointment(data, products).appointment;
      setDetails((current) => (current.appointment?.id === appointmentId
        ? { ...current, appointment: { ...current.appointment, deposit } }
        : current));
    } catch {
      // Read again with the next refusal.
    }
  }, [products]);

  // Once the catalog is in (its first load also brings back the sale saved on the device).
  useEffect(() => {
    const appointmentId = pendingAppointment.current;
    if (!appointmentId || catalog.loading) return;
    pendingAppointment.current = null;
    void chargeAppointment(appointmentId);
  }, [catalog.loading, chargeAppointment]);

  const drawer = useCashDrawer(access.cash, tenantId, workingBranch.branch?.id);
  const { cashClosed, drawerBranch } = drawer;
  // The sale is made where the cashier's drawer is; without one, at the branch this device works at.
  const saleBranchId = drawerBranch?.id ?? (workingBranch.hasSeveral ? workingBranch.branch?.id : undefined);

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

  const hasServices = cart.some((line) => line.isService);

  // The appointment the sale charges: a counter sale, never a tab, a platform's order or a delivery.
  const appointment = !tabTarget && !platform ? details.appointment ?? null : null;
  const { zones, delivery, deliveryOffered, deliveryZone } = useSaleDelivery(
    access.sell && !tabTarget, !tabTarget && !platform && !appointment, details.delivery, hasServices,
  );

  const totals = useMemo(
    () => saleTotals(cart, pricesIncludeTax, defaultTaxRate, deliveryZone, appointment?.visitFee),
    [cart, pricesIncludeTax, deliveryZone, defaultTaxRate, appointment?.visitFee],
  );
  const { phoneError, folioProblem, orderIdProblem, platformProblem, folioError, orderIdError, deliveryProblem, deliveryHint } = checkSale({
    details,
    platform,
    delivery,
    deliveryZone,
    zones,
    totals,
    hasServices,
    folioChecked,
    orderIdChecked,
  });

  const categoriesWithProducts = useMemo(() => categoriesInUse(catalog.categories, products), [catalog.categories, products]);
  const hasFeatured = products.some((product) => product.isFeatured);
  const visibleProducts = useMemo(() => filterCatalog(products, categoryFilter, search), [categoryFilter, products, search]);

  const addProduct = useCallback((product: ProductDto) => {
    setSaleError('');
    if (hasOptions(product)) {
      // Variants and extras: choose them (and how many) before it goes to the ticket.
      setChoosing({ product, optionIds: preselectedOptions(product), quantityText: '1' });
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
    setOrderIdChecked(false);
    setSaleError('');
  };

  /** The counter or a platform: a counter sale carries no folio; another platform keeps the folio typed. */
  const changeChannel = (channel: OrderChannel) => {
    if (draftLoadedFor.current !== tenantId) originChosen.current = true;
    setSaleError('');
    setFolioChecked(false);
    setOrderIdChecked(false);
    // An order of a platform is taken by its courier: it never goes to an address of the business's zones.
    setDetails((current) => (isDeliveryPlatform(channel) ? { ...current, channel, delivery: null } : { ...current, ...COUNTER_ORIGIN }));
  };

  const handleSearchKeyDown = async (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    const term = search.trim();
    if (!term) return;
    // A barcode scanner types the code and presses Enter: add the exact match.
    const exact = scannedProduct(products, term);
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
      if (orderIdProblem) setOrderIdChecked(true);
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
      if (orderIdProblem) setOrderIdChecked(true);
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
  }, [cart.length, submitting, tabTarget, canCharge, pricesPending, platformProblem, folioProblem, orderIdProblem, deliveryProblem, cashClosed, online]);

  const orderRequest = (): CreateOrderRequest => buildOrderRequest({
    cart, details, totals, platform, chatOrigin, delivery, appointment, isFood, hasServices, branchId: saleBranchId,
  });

  /** Keeps a charged sale on the device when the connection is down; false when this device cannot keep it. */
  const queueOfflineSale = async (request: CreateOrderRequest, charge: ChargeResult) => {
    if (!tenantId || !user) return false;
    const order = await keepSaleOnDevice({ tenantId, userId: user.id, request, charge, cart, totals, pricesIncludeTax });
    if (!order) return false;
    clearSale();
    setChargeOpen(false);
    setCompletedSale({ order, offline: true });
    return true;
  };

  // Fiado (Fase 26A): read when the charge opens, for a counter sale with the customer's phone; the server checks the
  // limit again as it sells.
  const creditPhone = canCharge && online && !platform && !delivery && !tabTarget
    ? details.customerPhone.replace(/\D/g, '')
    : '';
  const creditAccount = useCustomerAccount({ phone: creditPhone }, chargeOpen && creditPhone.length >= 10);
  const refreshCredit = useRefreshCustomerCredit();
  // What the customer has in their favor (Fase 26D) pays like another method; the server checks it again as it charges.
  const { credit, balance } = chargeFundsOf(creditAccount.data, details.customerName);

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
    // The sale of an appointment needs the server too: it checks the appointment as it charges it.
    // A sale on credit needs the server too: it checks the customer's limit as it sells, and so does one paid with the
    // balance in the customer's favor (Fase 26D): it checks what they have left.
    const usesBalance = !!charge?.payments.some((payment) => payment.method === OrderPaymentMethod.CustomerBalance);
    const canQueue = !!charge && !charge.onCredit && !usesBalance && !tabTarget && !chatOrigin && !hasServices && !platform
      && !delivery && !appointment;
    // The same key on a retry returns this sale instead of charging twice; another cart is another sale.
    const draft = toDraft(cart, details);
    const sale = describeSale(draft);
    const pending = pendingCharge.current?.sale === sale ? pendingCharge.current : { key: newUuid(), sale };
    pendingCharge.current = pending;
    // Saved before sending: a reload while the charge is on its way brings the cart back with this key.
    saveDraft(draft);
    const request: CreateOrderRequest = {
      ...orderRequest(),
      idempotencyKey: pending.key,
      payments: charge?.payments.map((payment) => ({
        amount: payment.amount,
        method: payment.method,
        reference: payment.reference,
        evidenceUrl: payment.evidenceUrl,
        tip: payment.tip,
        cashReceived: payment.cashReceived,
      })),
      onCredit: charge?.onCredit || undefined,
    };
    if (canQueue && !navigator.onLine && await queueOfflineSale(request, charge)) {
      setSubmitting(false);
      return;
    }
    try {
      const order = await sendSale(request, confirm);
      // Nothing was recorded: the cart stays, with its key, while the cashier checks Pedidos.
      if (!order) return;
      const stockWarning = stockWarningMessage(order.stockWarnings);
      if (stockWarning) toast.warning(stockWarning, { title: STOCK_WARNING_TITLE });
      clearSale();
      setChargeOpen(false);
      if (charge?.onCredit || usesBalance) refreshCredit();
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
      // So may the deposit of the appointment it charges.
      if (request.appointmentId) void refreshAppointmentDeposit(request.appointmentId);
      // Refused on credit, or with the balance: what the customer owes or has may have changed at another register, so the
      // panel reads it again.
      if (charge?.onCredit || usesBalance) refreshCredit();
    } finally {
      setSubmitting(false);
    }
  };

  const sendToKitchen = async () => {
    if (!tabTarget || !validateSale()) return;
    setSubmitting(true);
    setSaleError('');
    try {
      const request = orderRequest();
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
  useF9(platform ? registerPlatformOrder : openCharge, dialogOpen);

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
      activePlatforms={platforms.offered}
      platform={platform}
      onChannelChange={changeChannel}
      folioError={folioError}
      orderIdError={orderIdError}
      onOrderIdBlur={() => setOrderIdChecked(true)}
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
      appointment={appointment}
      onDismissAppointment={() => setDetails((current) => ({ ...current, appointment: null }))}
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

      <SellNotices
        offlineCatalogAt={catalog.offlineCatalogAt}
        cashClosed={cashClosed}
        hasPlatforms={platforms.active.length > 0}
        onOpenDrawer={() => setShowOpenDrawer(true)}
      />

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
          drawer.opened(session);
          // The device keeps working where the drawer is.
          if (session.isMine && session.branchId) workingBranch.setBranch(session.branchId);
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

      <SellPhoneBar
        articles={countArticles(cart)}
        total={totals.total}
        actionLabel={tabTarget ? 'Enviar' : platform ? 'Registrar' : canCharge ? 'Cobrar' : 'Guardar'}
        onAction={tabTarget
          ? () => void sendToKitchen()
          : platform ? registerFromPhoneBar : canCharge ? openCharge : () => void submitSale(null)}
        actionDisabled={cart.length === 0 || submitting || pricesPending || (platform !== null && !online)}
        ticketOpen={cartOpenOnPhone}
        onOpenTicket={() => setCartOpenOnPhone(true)}
        onCloseTicket={() => setCartOpenOnPhone(false)}
      >
        {cartPanel}
      </SellPhoneBar>

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

      <ChargeModal
        show={chargeOpen}
        // What the customer already paid online to book the appointment (Fase 31O) is not charged again.
        amountDue={Math.max(0, totals.total - (appointment?.deposit ?? 0))}
        paidOnline={appointment?.deposit ?? null}
        total={totals.total}
        submitting={submitting}
        error={saleError}
        // The tip of a platform's order is in its app, for the courier.
        allowTip={!platform}
        credit={credit}
        balance={balance}
        onSubmit={(charge) => void submitSale(charge)}
        onClose={() => setChargeOpen(false)}
      />

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
