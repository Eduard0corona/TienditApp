import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Modal, Offcanvas } from 'react-bootstrap';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { productsApi } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import { ordersApi } from '../../api/ordersApi';
import { cashApi } from '../../api/cashApi';
import { chatApi } from '../../api/chatApi';
import { getApiErrorMessage } from '../../api/errors';
import BranchPicker from '../../components/branches/BranchPicker';
import ChargePanel, { type ChargeResult } from '../../components/sales/ChargePanel';
import SaleTicket from '../../components/sales/SaleTicket';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { useWorkingBranch } from '../../hooks/useWorkingBranch';
import type { CategoryDto, CreateOrderRequest, OrderDto, ProductDto } from '../../types';
import { BusinessType, OrderChannel, OrderType, TenantCapability, UnitType } from '../../types';
import { getAccess } from '../../utils/access';
import { formatMoney } from '../../utils/money';
import { estimateOrderTotals, getVatLabel, resolveProductVat } from '../../utils/orderTotals';
import { hasCapability } from '../../utils/tenantCapabilities';
import { buildTicketText, buildWhatsAppTicketUrl, printTicket } from '../../utils/ticket';
import {
  allowsFractions,
  formatQuantity,
  getQuantityStep,
  getUnitShortLabel,
  lineAmount,
  parseQuantity,
  roundQuantity,
} from '../../utils/units';
import { readSellDraft, writeSellDraft } from './sellDraft';
import {
  getLineKey,
  getOptionsLabel,
  getPriceWithOptions,
  getSelectionError,
  hasOptions,
} from '../../utils/productOptions';
import './SellPage.css';

// Same limits as the API (OrderLimits).
const MAX_QUANTITY_PER_LINE = 10_000;
const MAX_LINES = 200;
// The largest page the API serves; a bigger catalog is searched on the server.
const CATALOG_PAGE_SIZE = 500;

interface CartLine {
  /** Product plus its options: the same product with other options is another line. */
  key: string;
  productId: string;
  optionIds: string[];
  /** "Grande, Almendra". */
  optionsLabel: string;
  name: string;
  unitPrice: number;
  quantity: number;
  notes: string;
  showNotes: boolean;
  isService: boolean;
  ratePercent: number;
  isExempt: boolean;
  unit?: UnitType;
  /** Sold by weight, volume, length or time: the quantity admits three decimals. */
  fractional: boolean;
}

/** A product with variants or extras waiting for the options and quantity. */
interface OptionsRequest {
  product: ProductDto;
  optionIds: string[];
  quantityText: string;
}

/** Where the order goes when it is not a counter sale: a new tab or a round of an open one. */
type TabTarget =
  | { kind: 'new'; tableId?: string; label: string }
  | { kind: 'round'; orderId: string; label: string };

function readTabTarget(params: URLSearchParams): TabTarget | null {
  const orderId = params.get('tab');
  if (orderId) return { kind: 'round', orderId, label: params.get('label')?.trim() || 'la cuenta' };
  const tableId = params.get('table') ?? undefined;
  const label = params.get('label')?.trim();
  if (tableId || label) return { kind: 'new', tableId, label: label || 'Mesa' };
  return null;
}

/** A weighed or measured product waiting for its quantity. */
interface MeasureRequest {
  product: ProductDto;
  text: string;
  /** The product is already in the ticket: the quantity replaces the one there. */
  replacing: boolean;
}

/** Quick picks of the quantity dialog for each unit. */
function getQuickQuantities(unit?: UnitType): number[] {
  switch (unit) {
    case UnitType.G:
    case UnitType.Ml:
      return [100, 250, 500, 1000];
    case UnitType.Hour:
      return [0.5, 1, 1.5, 2];
    default:
      return [0.25, 0.5, 1, 2];
  }
}

interface CompletedSale {
  order: OrderDto;
  change: number;
  cashReceived: number;
  /** Saved without charging (a WhatsApp order paid on delivery). */
  unpaid?: boolean;
}

/** The chat a sale was started from (Vender opened with ?phone=&name=&conversation=). */
interface ChatOrigin {
  phone: string;
  name?: string;
  conversationId?: string;
}

function readChatOrigin(params: URLSearchParams): ChatOrigin | null {
  const phone = params.get('phone')?.trim();
  if (!phone) return null;
  return {
    phone,
    name: params.get('name')?.trim() || undefined,
    conversationId: params.get('conversation')?.trim() || undefined,
  };
}

type CategoryFilter = 'all' | 'featured' | string;

const SERVICE_UNITS: ReadonlyArray<number> = [UnitType.Hour, UnitType.Session, UnitType.Service];

function isServiceProduct(product: ProductDto): boolean {
  return product.unit !== undefined && SERVICE_UNITS.includes(product.unit);
}

// Quick feedback only; the API normalizes and validates the number.
function isPlausiblePhone(value: string): boolean {
  const digits = value.replace(/\D/g, '');
  return digits.length >= 8 && digits.length <= 15;
}

/**
 * Vender: touch catalog, ticket and charge in one screen. The cart survives a reload, F9 charges and
 * the server computes the final total (the sale is rejected if a price changed since it was shown).
 */
export default function SellPage() {
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const toast = useToast();
  const access = getAccess(user, capabilities);
  const tenantId = tenant?.id ?? '';
  const pricesIncludeTax = tenant?.pricesIncludeTax ?? false;
  const defaultTaxRate = tenant?.defaultTaxRate ?? 16;
  const isFood = tenant?.businessType === BusinessType.Food;
  const canReceiveServices = hasCapability(capabilities, TenantCapability.Service);

  const [products, setProducts] = useState<ProductDto[]>([]);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [catalogTruncated, setCatalogTruncated] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [cart, setCart] = useState<CartLine[]>([]);
  const [customerName, setCustomerName] = useState('');
  const [customerPhone, setCustomerPhone] = useState('');
  const [tableNumber, setTableNumber] = useState('');
  const [orderNotes, setOrderNotes] = useState('');
  const [showDetails, setShowDetails] = useState(false);
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all');
  const [chargeOpen, setChargeOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [saleError, setSaleError] = useState('');
  const [completedSale, setCompletedSale] = useState<CompletedSale | null>(null);
  const [cartOpenOnPhone, setCartOpenOnPhone] = useState(false);
  const [cashClosed, setCashClosed] = useState(false);
  const [drawerBranch, setDrawerBranch] = useState<{ id: string; name: string } | null>(null);
  const workingBranch = useWorkingBranch();
  // The sale is made where the cashier's drawer is; without one, at the branch this device works at.
  const saleBranchId = drawerBranch?.id ?? (workingBranch.hasSeveral ? workingBranch.branch?.id : undefined);
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  // A table's tab (new or a new round): read once; the sale then goes to the kitchen instead of being charged.
  const [tabTarget] = useState<TabTarget | null>(() => readTabTarget(searchParams));
  const [tabOrder, setTabOrder] = useState<OrderDto | null>(null);
  const [choosing, setChoosing] = useState<OptionsRequest | null>(null);
  // Read once: the link fills the customer after the saved draft is restored.
  const pendingChatOrigin = useRef<ChatOrigin | null>(readChatOrigin(searchParams));
  const [chatOrigin, setChatOrigin] = useState<ChatOrigin | null>(null);
  const [sendingToChat, setSendingToChat] = useState(false);
  const [measure, setMeasure] = useState<MeasureRequest | null>(null);
  // What the cashier is typing in a quantity box ("1." on the way to "1.5"), until the box loses focus.
  const [quantityDrafts, setQuantityDrafts] = useState<Record<string, string>>({});
  const draftLoadedFor = useRef('');
  const searchInputRef = useRef<HTMLInputElement>(null);

  const toCartLine = useCallback(
    (product: ProductDto, quantity: number, notes = '', optionIds: string[] = []): CartLine => {
      const vat = resolveProductVat(product, defaultTaxRate);
      return {
        key: getLineKey(product.id, optionIds),
        productId: product.id,
        optionIds,
        optionsLabel: getOptionsLabel(product.optionGroups, optionIds),
        name: product.name,
        unitPrice: getPriceWithOptions(product.priceAmount, product.optionGroups, optionIds),
        quantity,
        notes,
        showNotes: notes.length > 0,
        isService: isServiceProduct(product),
        ratePercent: vat.ratePercent,
        isExempt: vat.isExempt,
        unit: product.unit,
        fractional: allowsFractions(product.unit),
      };
    },
    [defaultTaxRate],
  );

  const loadCatalog = useCallback(async () => {
    if (!tenantId) return;
    try {
      const [productsResponse, categoriesResponse] = await Promise.all([
        productsApi.getAll({ isAvailable: true, onlyAvailableNow: true, pageNumber: 1, pageSize: CATALOG_PAGE_SIZE }),
        categoriesApi.getAll(),
      ]);
      const loaded = productsResponse.data.items;
      setProducts(loaded);
      setCategories(categoriesResponse.data);
      setCatalogTruncated(productsResponse.data.totalCount > loaded.length);
      setLoadError('');

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
          setCustomerName(draft.customerName);
          setCustomerPhone(draft.customerPhone);
          setTableNumber(draft.tableNumber);
          setOrderNotes(draft.orderNotes);
          setShowDetails(Boolean(draft.customerName || draft.customerPhone || draft.tableNumber || draft.orderNotes));
        }
        const origin = pendingChatOrigin.current;
        if (origin) {
          pendingChatOrigin.current = null;
          setCustomerPhone(origin.phone);
          setCustomerName(origin.name ?? '');
          setShowDetails(true);
          setChatOrigin(origin);
        }
      } else {
        // Reload (a price changed): keep the cart with current prices and drop what is no longer sold.
        setCart((current) => current.flatMap((line) => {
          const product = byId.get(line.productId);
          if (!product || getSelectionError(product.optionGroups, line.optionIds)) return [];
          return [{ ...toCartLine(product, line.quantity, line.notes, line.optionIds), showNotes: line.showNotes }];
        }));
      }
    } catch (err: unknown) {
      setLoadError(getApiErrorMessage(err, 'No se pudo cargar el catálogo. Revisa tu conexión e intenta de nuevo.'));
    } finally {
      setLoading(false);
    }
  }, [tabTarget, tenantId, toCartLine]);

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

  useEffect(() => {
    void loadCatalog();
  }, [loadCatalog]);

  // The customer is already in the form: a reload must not fill it again.
  useEffect(() => {
    if (searchParams.has('phone')) setSearchParams({}, { replace: true });
  }, [searchParams, setSearchParams]);

  useEffect(() => {
    // Only people who manage the cash drawer can see it; for them a closed drawer is worth a warning.
    if (!access.cash || !tenantId) return;
    let cancelled = false;
    cashApi.getCurrentSession()
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
  }, [access.cash, tenantId]);

  useEffect(() => {
    if (!tenantId || draftLoadedFor.current !== tenantId || tabTarget) return;
    writeSellDraft(tenantId, {
      lines: cart.map((line) => ({ productId: line.productId, quantity: line.quantity, notes: line.notes, optionIds: line.optionIds })),
      customerName,
      customerPhone,
      tableNumber,
      orderNotes,
    });
  }, [cart, customerName, customerPhone, orderNotes, tableNumber, tabTarget, tenantId]);

  const totals = useMemo(
    () => estimateOrderTotals(
      cart.map((line) => ({
        unitPrice: line.unitPrice,
        quantity: line.quantity,
        ratePercent: line.ratePercent,
        isExempt: line.isExempt,
      })),
      pricesIncludeTax,
    ),
    [cart, pricesIncludeTax],
  );
  // A weighed product counts as one article, whatever it weighs.
  const itemCount = cart.reduce((sum, line) => sum + (line.fractional ? 1 : line.quantity), 0);
  const hasServices = cart.some((line) => line.isService);
  const phoneError = customerPhone.trim() && !isPlausiblePhone(customerPhone)
    ? 'Revisa el teléfono: 10 dígitos o el número con lada internacional (+52…).'
    : '';

  const categoriesWithProducts = useMemo(() => {
    const used = new Set(products.map((product) => product.categoryId));
    return categories
      .filter((category) => used.has(category.id))
      .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
  }, [categories, products]);
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
    setCart((current) => {
      const existing = current.find((line) => line.key === product.id);
      if (existing) {
        return current.map((line) => (line.key === product.id
          ? { ...line, quantity: Math.min(line.quantity + 1, MAX_QUANTITY_PER_LINE) }
          : line));
      }
      if (current.length >= MAX_LINES) {
        toast.warning(`Una venta admite hasta ${MAX_LINES} productos distintos.`);
        return current;
      }
      return [...current, toCartLine(product, 1)];
    });
  }, [cart, toCartLine, toast]);

  const chosenQuantity = choosing ? parseQuantity(choosing.quantityText, choosing.product.unit) : null;
  const choiceError = choosing ? getSelectionError(choosing.product.optionGroups, choosing.optionIds) : null;

  const toggleOption = (groupId: string, optionId: string) => {
    if (!choosing) return;
    const group = choosing.product.optionGroups?.find((item) => item.id === groupId);
    if (!group) return;
    const inGroup = new Set(group.options.map((option) => option.id));
    const selected = choosing.optionIds.includes(optionId);
    let optionIds: string[];
    if (group.maxSelections === 1) {
      // A single choice works like radio buttons (an optional one can be cleared by tapping it again).
      const others = choosing.optionIds.filter((id) => !inGroup.has(id));
      optionIds = selected && group.minSelections === 0 ? others : [...others, optionId];
    } else if (selected) {
      optionIds = choosing.optionIds.filter((id) => id !== optionId);
    } else {
      const pickedInGroup = choosing.optionIds.filter((id) => inGroup.has(id)).length;
      if (pickedInGroup >= group.maxSelections) return;
      optionIds = [...choosing.optionIds, optionId];
    }
    setChoosing({ ...choosing, optionIds });
  };

  const confirmChoice = () => {
    if (!choosing || choiceError || chosenQuantity === null) return;
    const { product, optionIds } = choosing;
    const key = getLineKey(product.id, optionIds);
    setCart((current) => {
      if (current.some((line) => line.key === key)) {
        return current.map((line) => (line.key === key
          ? { ...line, quantity: Math.min(roundQuantity(line.quantity + chosenQuantity), MAX_QUANTITY_PER_LINE) }
          : line));
      }
      if (current.length >= MAX_LINES) {
        toast.warning(`Una venta admite hasta ${MAX_LINES} productos distintos.`);
        return current;
      }
      return [...current, toCartLine(product, Math.min(chosenQuantity, MAX_QUANTITY_PER_LINE), '', optionIds)];
    });
    setChoosing(null);
  };

  const measuredQuantity = measure ? parseQuantity(measure.text, measure.product.unit) : null;
  const measureTooLarge = measuredQuantity !== null && measuredQuantity > MAX_QUANTITY_PER_LINE;

  const confirmMeasure = () => {
    if (!measure || measuredQuantity === null || measureTooLarge) return;
    const { product } = measure;
    setCart((current) => {
      if (current.some((line) => line.key === product.id)) {
        return current.map((line) => (line.key === product.id ? { ...line, quantity: measuredQuantity } : line));
      }
      if (current.length >= MAX_LINES) {
        toast.warning(`Una venta admite hasta ${MAX_LINES} productos distintos.`);
        return current;
      }
      return [...current, toCartLine(product, measuredQuantity)];
    });
    setMeasure(null);
  };

  const changeQuantity = (key: string, quantity: number) => {
    const rounded = roundQuantity(quantity);
    setCart((current) => (rounded <= 0
      ? current.filter((line) => line.key !== key)
      : current.map((line) => (line.key === key
        ? { ...line, quantity: Math.min(rounded, MAX_QUANTITY_PER_LINE) }
        : line))));
  };

  const typeQuantity = (line: CartLine, text: string) => {
    setQuantityDrafts((current) => ({ ...current, [line.key]: text }));
    const parsed = parseQuantity(text, line.unit);
    if (parsed !== null) changeQuantity(line.key, parsed);
  };

  const finishTypingQuantity = (key: string) => {
    setQuantityDrafts((current) => {
      const next = { ...current };
      delete next[key];
      return next;
    });
  };

  const updateLine = (key: string, changes: Partial<CartLine>) => {
    setCart((current) => current.map((line) => (line.key === key ? { ...line, ...changes } : line)));
  };

  const clearSale = () => {
    setCart([]);
    setCustomerName('');
    setCustomerPhone('');
    setTableNumber('');
    setOrderNotes('');
    setShowDetails(false);
    setSaleError('');
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
    if (visibleProducts.length === 0 && catalogTruncated) {
      try {
        const { data } = await productsApi.getAll({ isAvailable: true, onlyAvailableNow: true, search: term, pageSize: 20 });
        if (data.items.length === 1) {
          addProduct(data.items[0]);
          setSearch('');
        } else if (data.items.length > 1) {
          setProducts((current) => [...current, ...data.items.filter((item) => !current.some((p) => p.id === item.id))]);
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
    return true;
  };

  const openCharge = useCallback(() => {
    // A table's round is sent to the kitchen and charged when the table leaves (Mesas).
    if (cart.length === 0 || submitting || tabTarget) return;
    setSaleError('');
    setCartOpenOnPhone(false);
    setChargeOpen(true);
  }, [cart.length, submitting, tabTarget]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'F9') {
        event.preventDefault();
        openCharge();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [openCharge]);

  const buildOrderRequest = (): CreateOrderRequest => ({
    customerName: customerName.trim() || undefined,
    customerPhone: customerPhone.trim() || undefined,
    customerNotes: orderNotes.trim() || undefined,
    tableNumber: isFood ? tableNumber.trim() || undefined : undefined,
    // A service in the cart makes it a service order (pieces to track, if the service is trackable).
    orderType: hasServices ? OrderType.Service : OrderType.Retail,
    items: cart.map((line) => ({
      productId: line.productId,
      quantity: line.quantity,
      notes: line.notes.trim() || undefined,
      optionIds: line.optionIds.length > 0 ? line.optionIds : undefined,
    })),
    expectedTotal: totals.total,
    channel: chatOrigin ? OrderChannel.WhatsApp : undefined,
    branchId: saleBranchId,
  });

  const submitSale = async (charge: ChargeResult | null) => {
    if (!validateSale()) {
      setChargeOpen(false);
      return;
    }
    setSubmitting(true);
    setSaleError('');
    try {
      const request = buildOrderRequest();
      const { data: order } = await ordersApi.create({
        ...request,
        payments: charge?.payments.map((payment) => ({
          amount: payment.amount,
          method: payment.method,
          reference: payment.reference,
          evidenceUrl: payment.evidenceUrl,
        })),
      });
      clearSale();
      setChargeOpen(false);
      if (charge) {
        setCompletedSale({ order, change: charge.change, cashReceived: charge.cashReceived });
      } else if (chatOrigin) {
        // From a chat the summary goes back to the customer, paid or not.
        setCompletedSale({ order, change: 0, cashReceived: 0, unpaid: true });
      } else {
        toast.success(`Pedido ${order.orderNumber} guardado. Cóbralo desde Pedidos.`);
      }
    } catch (err: unknown) {
      const message = getApiErrorMessage(err, 'No se pudo registrar la venta.');
      setSaleError(message);
      if (!charge) toast.error(message);
      // A price or availability may have changed: refresh the catalog and the cart prices.
      void loadCatalog();
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
      if (tabTarget.kind === 'round') {
        await ordersApi.addRound(tabTarget.orderId, request.items);
      } else {
        await ordersApi.create({
          ...request,
          openTab: true,
          orderType: OrderType.Retail,
          diningTableId: tabTarget.tableId,
          // A tab without a table goes by its name.
          tableNumber: tabTarget.tableId ? undefined : tabTarget.label,
        });
      }
      clearSale();
      toast.success(tabTarget.kind === 'round' ? 'Ronda enviada a cocina.' : `Cuenta de ${tabTarget.label} abierta y enviada a cocina.`);
      navigate('/tables');
    } catch (err: unknown) {
      setSaleError(getApiErrorMessage(err, 'No se pudo enviar a cocina.'));
      void loadCatalog();
    } finally {
      setSubmitting(false);
    }
  };

  const startNewSale = () => {
    setCompletedSale(null);
    setChatOrigin(null);
    window.setTimeout(() => searchInputRef.current?.focus(), 0);
  };

  const sendSummaryToChat = async () => {
    if (!completedSale || !chatOrigin || sendingToChat) return;
    setSendingToChat(true);
    try {
      await chatApi.sendMessage({
        phoneNumber: completedSale.order.customerPhone ?? chatOrigin.phone,
        message: ticketText,
        conversationId: chatOrigin.conversationId,
      });
      toast.success('Resumen enviado al chat.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo enviar el resumen al chat.'));
    } finally {
      setSendingToChat(false);
    }
  };

  const businessName = tenant?.name ?? 'TienditApp';
  const ticketText = completedSale ? buildTicketText(completedSale.order, {
    businessName,
    change: completedSale.change,
    cashReceived: completedSale.cashReceived,
  }) : '';

  const cartPanel = (
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
          <button type="button" className="btn-close" aria-label="Ya no es un pedido por WhatsApp" onClick={() => setChatOrigin(null)}></button>
        </div>
      )}
      <div className="d-flex align-items-center justify-content-between mb-2">
        <h2 className="h5 mb-0">Ticket</h2>
        {cart.length > 0 && (
          <button type="button" className="btn btn-sm btn-link text-danger" onClick={clearSale} disabled={submitting}>
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
        <ul className="sell-lines list-unstyled mb-2">
          {cart.map((line) => {
            const step = getQuantityStep(line.unit);
            const unitLabel = getUnitShortLabel(line.unit);
            const removesLine = line.quantity - step <= 0;
            const stepLabel = line.fractional ? `${formatQuantity(step, line.unit)} de` : 'uno de';
            return (
              <li key={line.key} className="sell-line">
                <div className="d-flex justify-content-between gap-2">
                  <div className="sell-line-name">
                    {line.name}
                    {line.optionsLabel && <div className="small sell-line-options">{line.optionsLabel}</div>}
                    <div className="small text-muted">
                      {line.fractional
                        ? `${formatQuantity(line.quantity, line.unit)} × ${formatMoney(line.unitPrice)}/${unitLabel}`
                        : `${formatMoney(line.unitPrice)} c/u`}
                    </div>
                  </div>
                  <strong className="ta-money">{formatMoney(lineAmount(line.unitPrice, line.quantity))}</strong>
                </div>
                <div className="d-flex align-items-center justify-content-between gap-2 mt-1">
                  <div className="sell-stepper" role="group" aria-label={`Cantidad de ${line.name}`}>
                    <button
                      type="button"
                      className="btn btn-light"
                      aria-label={removesLine ? `Quitar ${line.name}` : `Quitar ${stepLabel} ${line.name}`}
                      onClick={() => changeQuantity(line.key, line.quantity - step)}
                    >
                      <i className={removesLine ? 'ri-delete-bin-line' : 'ri-subtract-line'} aria-hidden="true"></i>
                    </button>
                    <input
                      className={`form-control text-center ta-money ${line.fractional ? 'sell-quantity-measured' : ''}`}
                      inputMode={line.fractional ? 'decimal' : 'numeric'}
                      aria-label={line.fractional ? `Cantidad de ${line.name} en ${unitLabel}` : `Cantidad de ${line.name}`}
                      value={quantityDrafts[line.key] ?? String(line.quantity)}
                      onChange={(event) => typeQuantity(line, event.target.value)}
                      onBlur={() => finishTypingQuantity(line.key)}
                    />
                    <button
                      type="button"
                      className="btn btn-light"
                      aria-label={`Agregar ${stepLabel} ${line.name}`}
                      onClick={() => changeQuantity(line.key, line.quantity + step)}
                    >
                      <i className="ri-add-line" aria-hidden="true"></i>
                    </button>
                  </div>
                  <button
                    type="button"
                    className={`btn btn-sm ${line.showNotes ? 'btn-soft-warning' : 'btn-light'}`}
                    onClick={() => updateLine(line.key, { showNotes: !line.showNotes })}
                    aria-expanded={line.showNotes}
                  >
                    <i className="ri-sticky-note-line me-1" aria-hidden="true"></i>Nota
                  </button>
                </div>
                {line.showNotes && (
                  <input
                    className="form-control form-control-sm mt-2"
                    placeholder="Ej. sin cebolla, para llevar"
                    aria-label={`Nota para ${line.name}`}
                    maxLength={300}
                    value={line.notes}
                    onChange={(event) => updateLine(line.key, { notes: event.target.value })}
                  />
                )}
              </li>
            );
          })}
        </ul>
      )}

      <button
        type="button"
        className="btn btn-link btn-sm px-0 mb-2"
        onClick={() => setShowDetails((value) => !value)}
        aria-expanded={showDetails}
      >
        <i className={`${showDetails ? 'ri-arrow-up-s-line' : 'ri-user-add-line'} me-1`} aria-hidden="true"></i>
        {showDetails ? 'Ocultar datos del cliente' : 'Cliente, mesa y notas (opcional)'}
      </button>
      {showDetails && (
        <div className="sell-details mb-2">
          <div className="row g-2">
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor="sell-customer-name">Nombre</label>
              <input
                id="sell-customer-name"
                className="form-control"
                maxLength={200}
                placeholder="Público en general"
                value={customerName}
                onChange={(event) => setCustomerName(event.target.value)}
              />
            </div>
            <div className="col-sm-6">
              <label className="form-label small mb-1" htmlFor="sell-customer-phone">Teléfono</label>
              <input
                id="sell-customer-phone"
                type="tel"
                inputMode="tel"
                autoComplete="tel"
                className={`form-control ${phoneError ? 'is-invalid' : ''}`}
                placeholder="55 1234 5678"
                value={customerPhone}
                onChange={(event) => setCustomerPhone(event.target.value)}
                aria-describedby="sell-customer-phone-help"
              />
              <div id="sell-customer-phone-help" className={phoneError ? 'invalid-feedback d-block' : 'form-text'}>
                {phoneError || 'Con teléfono la venta queda en su historial.'}
              </div>
            </div>
            {isFood && (
              <div className="col-sm-6">
                <label className="form-label small mb-1" htmlFor="sell-table">Mesa o referencia</label>
                <input
                  id="sell-table"
                  className="form-control"
                  maxLength={50}
                  value={tableNumber}
                  onChange={(event) => setTableNumber(event.target.value)}
                />
              </div>
            )}
            <div className={isFood ? 'col-sm-6' : 'col-12'}>
              <label className="form-label small mb-1" htmlFor="sell-notes">Notas del pedido</label>
              <input
                id="sell-notes"
                className="form-control"
                maxLength={1000}
                value={orderNotes}
                onChange={(event) => setOrderNotes(event.target.value)}
              />
            </div>
          </div>
        </div>
      )}

      <div className="sell-totals">
        {!pricesIncludeTax && (
          <div className="d-flex justify-content-between">
            <span>Subtotal</span>
            <span className="ta-money">{formatMoney(totals.subtotal)}</span>
          </div>
        )}
        {totals.breakdown.filter((line) => line.taxAmount > 0).map((line) => (
          <div className="d-flex justify-content-between small text-muted" key={`${line.ratePercent}-${line.isExempt}`}>
            <span>{pricesIncludeTax ? `Incluye ${getVatLabel(line)}` : getVatLabel(line)}</span>
            <span className="ta-money">{formatMoney(line.taxAmount)}</span>
          </div>
        ))}
        <div className="d-flex justify-content-between align-items-baseline sell-total">
          <span>Total</span>
          <span className="ta-money">{formatMoney(totals.total)}</span>
        </div>
      </div>

      {saleError && !chargeOpen && <div className="alert alert-danger py-2 mt-2 mb-0">{saleError}</div>}

      {tabTarget ? (
        <div className="d-grid gap-2 mt-3">
          <button
            type="button"
            className="btn btn-primary btn-touch btn-touch-lg"
            onClick={() => void sendToKitchen()}
            disabled={cart.length === 0 || submitting}
          >
            <i className="ri-restaurant-2-line me-1" aria-hidden="true"></i>
            {submitting ? 'Enviando…' : `Enviar a cocina · ${formatMoney(totals.total)}`}
          </button>
          <Link to="/tables" className="btn btn-light btn-touch">Cancelar</Link>
        </div>
      ) : (
        <div className="d-grid gap-2 mt-3">
          <button
            type="button"
            className="btn btn-primary btn-touch btn-touch-lg"
            onClick={openCharge}
            disabled={cart.length === 0 || submitting}
          >
            Cobrar {formatMoney(totals.total)} <span className="sell-kbd">F9</span>
          </button>
          <button
            type="button"
            className="btn btn-light btn-touch"
            onClick={() => void submitSale(null)}
            disabled={cart.length === 0 || submitting}
          >
            Guardar sin cobrar
          </button>
        </div>
      )}
    </div>
  );

  return (
    <div className="sell-page">
      <div className="sell-header d-flex flex-wrap align-items-center justify-content-between gap-2">
        <h1 className="h3 mb-0">Vender</h1>
        <div className="d-flex flex-wrap align-items-center gap-2">
          {workingBranch.hasSeveral && (drawerBranch ? (
            <span className="badge text-bg-light border" title="Las ventas se registran en la sucursal de tu caja abierta">
              <i className="ri-store-3-line me-1" aria-hidden="true"></i>{drawerBranch.name} · tu caja
            </span>
          ) : (
            <BranchPicker
              branches={workingBranch.branches}
              value={workingBranch.branch?.id}
              onChange={workingBranch.setBranch}
              label="Sucursal donde vendes"
            />
          ))}
          {canReceiveServices && access.sell && (
            <Link to="/orders/cart" className="btn btn-sm btn-light">
              <i className="ri-t-shirt-line me-1" aria-hidden="true"></i>Recibir servicio con piezas
            </Link>
          )}
          {access.orders && (
            <Link to="/orders" className="btn btn-sm btn-light">
              <i className="ri-file-list-3-line me-1" aria-hidden="true"></i>Pedidos
            </Link>
          )}
        </div>
      </div>

      {cashClosed && (
        <div className="alert alert-warning d-flex flex-wrap align-items-center justify-content-between gap-2 mt-3 mb-0" role="status">
          <span>
            <i className="ri-safe-2-line me-1" aria-hidden="true"></i>
            La caja está cerrada: los cobros se guardan, pero no entran al corte.
          </span>
          <Link to="/cash" className="btn btn-sm btn-warning">Abrir caja</Link>
        </div>
      )}

      <div className="sell-layout mt-3">
        <section className="sell-catalog" aria-label="Catálogo">
          <div className="sell-search">
            <i className="ri-search-line" aria-hidden="true"></i>
            <input
              ref={searchInputRef}
              type="search"
              className="form-control form-control-lg"
              placeholder="Buscar o escanear código"
              aria-label="Buscar producto o escanear código de barras"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              onKeyDown={(event) => void handleSearchKeyDown(event)}
              autoComplete="off"
            />
          </div>

          <div className="sell-chips" role="tablist" aria-label="Categorías">
            <button
              type="button"
              role="tab"
              aria-selected={categoryFilter === 'all'}
              className={`btn btn-touch ${categoryFilter === 'all' ? 'btn-primary' : 'btn-light'}`}
              onClick={() => setCategoryFilter('all')}
            >
              Todos
            </button>
            {hasFeatured && (
              <button
                type="button"
                role="tab"
                aria-selected={categoryFilter === 'featured'}
                className={`btn btn-touch ${categoryFilter === 'featured' ? 'btn-primary' : 'btn-light'}`}
                onClick={() => setCategoryFilter('featured')}
              >
                <i className="ri-star-line me-1" aria-hidden="true"></i>Favoritos
              </button>
            )}
            {categoriesWithProducts.map((category) => (
              <button
                key={category.id}
                type="button"
                role="tab"
                aria-selected={categoryFilter === category.id}
                className={`btn btn-touch ${categoryFilter === category.id ? 'btn-primary' : 'btn-light'}`}
                onClick={() => setCategoryFilter(category.id)}
              >
                {category.name}
              </button>
            ))}
          </div>

          {loadError && (
            <div className="alert alert-danger d-flex justify-content-between align-items-center gap-2">
              <span>{loadError}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void loadCatalog()}>Reintentar</button>
            </div>
          )}
          {catalogTruncated && (
            <div className="alert alert-info py-2" role="status">
              Tu catálogo tiene más de {CATALOG_PAGE_SIZE} productos: escribe el nombre o escanea el código y presiona Enter
              para buscar en todo el catálogo.
            </div>
          )}

          {loading ? (
            <div className="sell-products" aria-busy="true">
              {Array.from({ length: 8 }, (_, index) => <div key={index} className="sell-product sell-product-skeleton" />)}
            </div>
          ) : products.length === 0 && !loadError ? (
            <div className="card">
              <div className="card-body text-center py-5">
                <i className="ri-store-2-line fs-1 text-muted" aria-hidden="true"></i>
                <p className="mt-2 mb-3">Todavía no hay productos disponibles para vender.</p>
                {access.catalog && <Link to="/products/new" className="btn btn-primary">Agregar producto</Link>}
              </div>
            </div>
          ) : visibleProducts.length === 0 ? (
            <p className="text-muted py-4 text-center">No hay productos que coincidan con la búsqueda.</p>
          ) : (
            <div className="sell-products">
              {visibleProducts.map((product) => {
                const inCart = roundQuantity(cart
                  .filter((line) => line.productId === product.id)
                  .reduce((sum, line) => sum + line.quantity, 0));
                const unitLabel = allowsFractions(product.unit) ? getUnitShortLabel(product.unit) : '';
                const price = unitLabel ? `${formatMoney(product.priceAmount)}/${unitLabel}` : formatMoney(product.priceAmount);
                const inCartLabel = unitLabel ? formatQuantity(inCart, product.unit) : String(inCart);
                // The badge shows the number only: the tile already says the unit.
                const badge = inCart.toLocaleString('es-MX', { maximumFractionDigits: 3 });
                return (
                  <button
                    key={product.id}
                    type="button"
                    className={`sell-product ${inCart > 0 ? 'in-cart' : ''} ${unitLabel ? 'measured' : ''}`}
                    onClick={() => addProduct(product)}
                    aria-label={`Agregar ${product.name}, ${price}${inCart > 0 ? `, ${inCartLabel} en el ticket` : ''}`}
                  >
                    {product.images?.thumbnail && (
                      <img src={product.images.thumbnail} alt="" className="sell-product-image" loading="lazy" />
                    )}
                    <span className="sell-product-name">{product.name}</span>
                    <span className="sell-product-price ta-money">{price}</span>
                    {isServiceProduct(product) && <span className="sell-product-tag">Servicio</span>}
                    {unitLabel && !isServiceProduct(product) && <span className="sell-product-tag">Por {unitLabel}</span>}
                    {inCart > 0 && <span className="sell-product-count" aria-hidden="true">{badge}</span>}
                  </button>
                );
              })}
            </div>
          )}
        </section>

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
          onClick={tabTarget ? () => void sendToKitchen() : openCharge}
          disabled={cart.length === 0 || submitting}
        >
          {tabTarget ? 'Enviar' : 'Cobrar'}
        </button>
      </div>
      <Offcanvas show={cartOpenOnPhone} onHide={() => setCartOpenOnPhone(false)} placement="bottom" className="sell-sheet">
        <Offcanvas.Header closeButton>
          <Offcanvas.Title as="span" className="visually-hidden">Ticket</Offcanvas.Title>
        </Offcanvas.Header>
        <Offcanvas.Body>{cartPanel}</Offcanvas.Body>
      </Offcanvas>

      <Modal show={!!choosing} onHide={() => setChoosing(null)} centered scrollable fullscreen="sm-down">
        {choosing && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              confirmChoice();
            }}
          >
            <Modal.Header closeButton>
              <Modal.Title as="h2" className="h5">{choosing.product.name}</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              {(choosing.product.optionGroups ?? []).map((group) => {
                const single = group.maxSelections === 1;
                const pickedInGroup = group.options.filter((option) => choosing.optionIds.includes(option.id)).length;
                return (
                  <fieldset key={group.id} className="mb-3">
                    <legend className="fs-6 fw-semibold mb-1">
                      {group.name}{' '}
                      <span className="small text-muted fw-normal">
                        {group.minSelections > 0
                          ? single ? '· obligatorio' : `· elige ${group.minSelections} a ${group.maxSelections}`
                          : single ? '· opcional' : `· hasta ${group.maxSelections}`}
                      </span>
                    </legend>
                    <div className="sell-option-list">
                      {group.options.map((option) => {
                        const checked = choosing.optionIds.includes(option.id);
                        const full = !single && !checked && pickedInGroup >= group.maxSelections;
                        return (
                          <label key={option.id} className={`sell-option ${checked ? 'active' : ''} ${!option.isAvailable || full ? 'disabled' : ''}`}>
                            <input
                              type={single ? 'radio' : 'checkbox'}
                              className="form-check-input me-2"
                              name={`option-${group.id}`}
                              checked={checked}
                              disabled={!option.isAvailable || full}
                              onChange={() => toggleOption(group.id, option.id)}
                              onClick={() => {
                                // Tapping the chosen option of an optional single choice clears it.
                                if (single && checked && group.minSelections === 0) toggleOption(group.id, option.id);
                              }}
                            />
                            <span className="flex-grow-1">{option.name}{!option.isAvailable && ' (agotado)'}</span>
                            {option.priceDelta !== 0 && (
                              <span className="ta-money small">{option.priceDelta > 0 ? '+' : '−'}{formatMoney(Math.abs(option.priceDelta))}</span>
                            )}
                          </label>
                        );
                      })}
                    </div>
                  </fieldset>
                );
              })}
              <label className="form-label" htmlFor="sell-choice-quantity">
                {allowsFractions(choosing.product.unit) ? `Cantidad (${getUnitShortLabel(choosing.product.unit)})` : 'Cantidad'}
              </label>
              <input
                id="sell-choice-quantity"
                className={`form-control ta-money ${choosing.quantityText && chosenQuantity === null ? 'is-invalid' : ''}`}
                style={{ maxWidth: '8rem' }}
                inputMode={allowsFractions(choosing.product.unit) ? 'decimal' : 'numeric'}
                value={choosing.quantityText}
                onChange={(event) => setChoosing({ ...choosing, quantityText: event.target.value })}
              />
              {choiceError && <div className="text-danger small mt-2" role="status">{choiceError}</div>}
            </Modal.Body>
            <Modal.Footer>
              <button type="button" className="btn btn-light" onClick={() => setChoosing(null)}>Cancelar</button>
              <button type="submit" className="btn btn-primary" disabled={Boolean(choiceError) || chosenQuantity === null}>
                Agregar · {formatMoney(lineAmount(
                  getPriceWithOptions(choosing.product.priceAmount, choosing.product.optionGroups, choosing.optionIds),
                  chosenQuantity ?? 0,
                ))}
              </button>
            </Modal.Footer>
          </form>
        )}
      </Modal>

      <Modal show={!!measure} onHide={() => setMeasure(null)} centered>
        {measure && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              confirmMeasure();
            }}
          >
            <Modal.Header closeButton>
              <Modal.Title as="h2" className="h5">{measure.product.name}</Modal.Title>
            </Modal.Header>
            <Modal.Body>
              <label className="form-label" htmlFor="sell-measure">
                ¿Cuánto? <span className="text-muted">({formatMoney(measure.product.priceAmount)}/{getUnitShortLabel(measure.product.unit)})</span>
              </label>
              <div className="input-group input-group-lg">
                <input
                  id="sell-measure"
                  className={`form-control ta-money ${measure.text && (measuredQuantity === null || measureTooLarge) ? 'is-invalid' : ''}`}
                  inputMode="decimal"
                  autoComplete="off"
                  autoFocus
                  placeholder="0.000"
                  value={measure.text}
                  onChange={(event) => setMeasure({ ...measure, text: event.target.value })}
                  aria-describedby="sell-measure-help"
                />
                <span className="input-group-text">{getUnitShortLabel(measure.product.unit)}</span>
              </div>
              <div id="sell-measure-help" className={measure.text && (measuredQuantity === null || measureTooLarge) ? 'invalid-feedback d-block' : 'form-text'}>
                {measureTooLarge
                  ? `Máximo ${MAX_QUANTITY_PER_LINE.toLocaleString('es-MX')} por producto.`
                  : measure.text && measuredQuantity === null
                    ? 'Escribe una cantidad mayor a cero, con hasta 3 decimales.'
                    : measuredQuantity !== null
                      ? `Importe: ${formatMoney(lineAmount(measure.product.priceAmount, measuredQuantity))}`
                      : 'Hasta 3 decimales.'}
              </div>
              <div className="sell-measure-picks mt-3" role="group" aria-label="Cantidades rápidas">
                {getQuickQuantities(measure.product.unit).map((quantity) => (
                  <button
                    key={quantity}
                    type="button"
                    className="btn btn-light btn-touch"
                    onClick={() => setMeasure({ ...measure, text: String(quantity) })}
                  >
                    {formatQuantity(quantity, measure.product.unit)}
                  </button>
                ))}
              </div>
            </Modal.Body>
            <Modal.Footer>
              <button type="button" className="btn btn-light" onClick={() => setMeasure(null)}>Cancelar</button>
              <button type="submit" className="btn btn-primary" disabled={measuredQuantity === null || measureTooLarge}>
                {measure.replacing ? 'Cambiar cantidad' : 'Agregar'}
              </button>
            </Modal.Footer>
          </form>
        )}
      </Modal>

      <Modal show={chargeOpen} onHide={() => !submitting && setChargeOpen(false)} centered size="lg" fullscreen="sm-down">
        <Modal.Header closeButton={!submitting}>
          <Modal.Title as="h2" className="h5">Cobrar</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {chargeOpen && (
            <ChargePanel
              amountDue={totals.total}
              submitting={submitting}
              error={saleError}
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

      <Modal show={!!completedSale} onHide={startNewSale} centered fullscreen="sm-down">
        <Modal.Header closeButton>
          <Modal.Title as="h2" className="h5">
            <i className="ri-checkbox-circle-fill text-success me-1" aria-hidden="true"></i>
            {completedSale?.unpaid
              ? `Pedido ${completedSale.order.orderNumber} guardado`
              : `Venta ${completedSale?.order.orderNumber} cobrada`}
          </Modal.Title>
        </Modal.Header>
        <Modal.Body>
          {completedSale && (
            <>
              {completedSale.change > 0 && (
                <div className="charge-change has-change mb-3" aria-live="polite">
                  <span>Cambio</span>
                  <strong className="ta-money">{formatMoney(completedSale.change)}</strong>
                </div>
              )}
              <SaleTicket
                order={completedSale.order}
                businessName={businessName}
                change={completedSale.change}
                cashReceived={completedSale.cashReceived}
              />
            </>
          )}
        </Modal.Body>
        <Modal.Footer className="flex-wrap">
          <button type="button" className="btn btn-light btn-touch" onClick={printTicket}>
            <i className="ri-printer-line me-1" aria-hidden="true"></i>Imprimir
          </button>
          {chatOrigin && access.chat ? (
            <>
              <button type="button" className="btn btn-success btn-touch" onClick={() => void sendSummaryToChat()} disabled={sendingToChat}>
                <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>{sendingToChat ? 'Enviando…' : 'Enviar resumen al chat'}
              </button>
              <Link
                className="btn btn-light btn-touch"
                to={chatOrigin.conversationId ? `/whatsapp/chat?conversation=${chatOrigin.conversationId}` : '/whatsapp/chat'}
              >
                Volver al chat
              </Link>
            </>
          ) : (
            <a
              className="btn btn-light btn-touch"
              href={buildWhatsAppTicketUrl(ticketText, completedSale?.order.customerPhone)}
              target="_blank"
              rel="noopener noreferrer"
            >
              <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Enviar por WhatsApp
            </a>
          )}
          <button type="button" className="btn btn-primary btn-touch flex-grow-1" onClick={startNewSale} autoFocus>
            Nueva venta
          </button>
        </Modal.Footer>
      </Modal>
    </div>
  );
}
