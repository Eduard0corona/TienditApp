import { useEffect, useMemo, useRef, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useNavigate } from 'react-router-dom';
import { ordersApi } from '../../api/ordersApi';
import { customersApi } from '../../api/customersApi';
import { productsApi } from '../../api/productsApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { formatMoney } from '../../utils/money';
import { getOrderStatusLabel } from '../../utils/labels';
import { getAllowedPages } from './navigation';

interface GlobalSearchProps {
  show: boolean;
  onClose: () => void;
}

interface SearchResult {
  key: string;
  group: string;
  icon: string;
  title: string;
  detail?: string;
  path: string;
}

const SEARCH_DELAY_MS = 250;
const RESULTS_PER_GROUP = 5;

function normalize(value: string): string {
  return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

/**
 * Ctrl K: pages, orders (folio, customer, phone), customers and products in one box. Every list is
 * searched on the server and only for what the person can open.
 */
export default function GlobalSearch({ show, onClose }: GlobalSearchProps) {
  const navigate = useNavigate();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const [term, setTerm] = useState('');
  const [remoteResults, setRemoteResults] = useState<SearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const latestRequest = useRef(0);

  const pageResults = useMemo<SearchResult[]>(() => {
    const query = normalize(term.trim());
    return getAllowedPages(access, capabilities)
      .filter((item) => !query || normalize(`${item.label} ${item.keywords ?? ''}`).includes(query))
      .slice(0, query ? 6 : 8)
      .map((item) => ({ key: `page-${item.path}`, group: 'Ir a', icon: item.icon, title: item.label, path: item.path }));
  }, [access, capabilities, term]);

  const canSearchOrders = access.orders;
  const canSearchCustomers = access.customers;
  const canSearchProducts = access.catalog;

  useEffect(() => {
    const query = term.trim();
    if (query.length < 2) return;
    const requestId = ++latestRequest.current;
    const timeoutId = window.setTimeout(async () => {
      setSearching(true);
      const [orders, customers, products] = await Promise.all([
        canSearchOrders
          ? ordersApi.getAll({ search: query, pageSize: RESULTS_PER_GROUP }).then((r) => r.data.items).catch(() => [])
          : Promise.resolve([]),
        canSearchCustomers
          ? customersApi.getAll({ search: query, pageSize: RESULTS_PER_GROUP }).then((r) => r.data.items).catch(() => [])
          : Promise.resolve([]),
        canSearchProducts
          ? productsApi.getAll({ search: query, pageSize: RESULTS_PER_GROUP }).then((r) => r.data.items).catch(() => [])
          : Promise.resolve([]),
      ]);
      // An older, slower search must not replace the results of the current one.
      if (requestId !== latestRequest.current) return;
      setRemoteResults([
        ...orders.map((order) => ({
          key: `order-${order.id}`,
          group: 'Pedidos',
          icon: 'ri-file-list-3-line',
          title: `${order.orderNumber} · ${order.customerName}`,
          detail: `${getOrderStatusLabel(order.status)} · ${formatMoney(order.total)}`,
          path: `/orders?search=${encodeURIComponent(order.orderNumber)}`,
        })),
        ...customers.map((customer) => ({
          key: `customer-${customer.id}`,
          group: 'Clientes',
          icon: 'ri-user-3-line',
          title: customer.isPublicGeneral ? 'Público en general' : customer.name,
          detail: customer.phone ?? customer.email ?? undefined,
          path: `/customers?search=${encodeURIComponent(customer.phone || customer.name)}`,
        })),
        ...products.map((product) => ({
          key: `product-${product.id}`,
          group: 'Productos',
          icon: 'ri-shopping-bag-line',
          title: product.name,
          detail: formatMoney(product.priceAmount),
          path: `/products/${product.id}/edit`,
        })),
      ]);
      setSearching(false);
    }, SEARCH_DELAY_MS);
    return () => window.clearTimeout(timeoutId);
  }, [canSearchCustomers, canSearchOrders, canSearchProducts, term]);

  const showRemote = term.trim().length >= 2;
  const results = useMemo(
    () => [...pageResults, ...(showRemote ? remoteResults : [])],
    [pageResults, remoteResults, showRemote],
  );
  const selectedIndex = Math.min(activeIndex, Math.max(0, results.length - 1));

  const open = (result: SearchResult) => {
    onClose();
    setTerm('');
    setRemoteResults([]);
    navigate(result.path);
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((selectedIndex + 1) % Math.max(1, results.length));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((selectedIndex - 1 + results.length) % Math.max(1, results.length));
    } else if (event.key === 'Enter' && results[selectedIndex]) {
      event.preventDefault();
      open(results[selectedIndex]);
    }
  };

  return (
    <Modal show={show} onHide={onClose} size="lg" className="global-search" aria-labelledby="global-search-label">
      <Modal.Body className="p-0">
        <div className="global-search-box">
          <i className="ri-search-line" aria-hidden="true"></i>
          <label id="global-search-label" htmlFor="global-search-input" className="visually-hidden">
            Buscar pedidos, clientes, productos o pantallas
          </label>
          <input
            id="global-search-input"
            className="form-control form-control-lg border-0 shadow-none"
            placeholder="Buscar pedido, cliente, producto o pantalla…"
            autoFocus
            autoComplete="off"
            role="combobox"
            aria-expanded={results.length > 0}
            aria-controls="global-search-results"
            aria-activedescendant={results[selectedIndex] ? `global-search-${results[selectedIndex].key}` : undefined}
            value={term}
            onChange={(event) => {
              setTerm(event.target.value);
              setActiveIndex(0);
            }}
            onKeyDown={handleKeyDown}
          />
          <kbd className="d-none d-md-inline">Esc</kbd>
        </div>

        <ul id="global-search-results" className="global-search-results list-unstyled mb-0" role="listbox">
          {results.map((result, index) => {
            const header = index === 0 || results[index - 1].group !== result.group ? result.group : null;
            return (
              <li key={result.key} role="presentation">
                {header && <div className="global-search-group">{header}</div>}
                <button
                  type="button"
                  id={`global-search-${result.key}`}
                  role="option"
                  aria-selected={index === selectedIndex}
                  className={`global-search-result ${index === selectedIndex ? 'active' : ''}`}
                  onMouseEnter={() => setActiveIndex(index)}
                  onClick={() => open(result)}
                >
                  <i className={result.icon} aria-hidden="true"></i>
                  <span className="flex-grow-1 text-truncate">{result.title}</span>
                  {result.detail && <span className="text-muted small text-truncate">{result.detail}</span>}
                </button>
              </li>
            );
          })}
          {showRemote && searching && <li className="global-search-empty">Buscando…</li>}
          {showRemote && !searching && results.length === pageResults.length && (
            <li className="global-search-empty">Sin pedidos, clientes ni productos para “{term.trim()}”.</li>
          )}
        </ul>
      </Modal.Body>
    </Modal>
  );
}
