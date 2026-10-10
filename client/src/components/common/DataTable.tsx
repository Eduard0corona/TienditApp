import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import './DataTable.css';

export interface DataTableColumn {
  header: string;
  className?: string;
  width?: number | string;
}

export interface ServerSideConfig {
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  /**
   * Searches on the server, over every page (called 300 ms after typing stops). Without it the box only
   * filters the rows already loaded.
   */
  onSearchChange?: (search: string) => void;
}

// Waiting for a pause in typing keeps one request per search instead of one per key.
const SERVER_SEARCH_DELAY_MS = 300;

interface DataTableProps<T> {
  data: T[];
  columns: DataTableColumn[];
  renderRow: (item: T) => React.ReactNode;
  keyExtractor: (item: T) => string;
  searchFilter?: (item: T, search: string) => boolean;
  serverSide?: ServerSideConfig;
  loading?: boolean;
  emptyMessage?: string;
  pageSizeOptions?: number[];
  searchPlaceholder?: string;
  /** Text already in the search box: a search that came in the link, or the one the page kept while another tab was open. */
  initialSearch?: string;
  /** The screen's own filters, in the same bar as the search box (each one a `DataTableFilter`). */
  filters?: ReactNode;
  /** The action of the list (Nuevo producto) or a control of the screen, at the end of that bar. */
  toolbarEnd?: ReactNode;
}

/** A filter of the list with its label above, so every control of the bar lines up. */
export function DataTableFilter({ label, htmlFor, children }: { label: string; htmlFor: string; children: ReactNode }) {
  return (
    <div className="datatable-filter">
      <label className="form-label" htmlFor={htmlFor}>{label}</label>
      {children}
    </div>
  );
}

const DEFAULT_PAGE_SIZES = [10, 25, 50, 100];

export default function DataTable<T>({
  data,
  columns,
  renderRow,
  keyExtractor,
  searchFilter,
  serverSide,
  loading = false,
  emptyMessage = 'Todavía no hay nada aquí.',
  pageSizeOptions = DEFAULT_PAGE_SIZES,
  searchPlaceholder = 'Buscar…',
  initialSearch = '',
  filters,
  toolbarEnd,
}: DataTableProps<T>) {
  const [search, setSearch] = useState(initialSearch);
  const [clientPage, setClientPage] = useState(1);
  const [clientPageSize, setClientPageSize] = useState(pageSizeOptions[0]);

  const isServerSide = !!serverSide;
  const onServerSearch = serverSide?.onSearchChange;
  const searchesOnServer = !!onServerSearch;
  const onServerSearchRef = useRef(onServerSearch);
  // The page already loaded the initial search: only later changes are sent.
  const lastServerSearch = useRef(initialSearch.trim());

  useEffect(() => {
    onServerSearchRef.current = onServerSearch;
  }, [onServerSearch]);

  useEffect(() => {
    if (!searchesOnServer) return;
    const term = search.trim();
    if (term === lastServerSearch.current) return;
    const timeoutId = window.setTimeout(() => {
      lastServerSearch.current = term;
      onServerSearchRef.current?.(term);
    }, SERVER_SEARCH_DELAY_MS);
    return () => window.clearTimeout(timeoutId);
  }, [search, searchesOnServer]);

  // Server search: the page already comes filtered. Otherwise filter the rows loaded.
  const filteredData = useMemo(() => {
    if (searchesOnServer || !search || !searchFilter) return data;
    return data.filter((item) => searchFilter(item, search.toLowerCase()));
  }, [data, search, searchFilter, searchesOnServer]);

  // Pagination values
  const page = isServerSide ? serverSide.page : clientPage;
  const pageSize = isServerSide ? serverSide.pageSize : clientPageSize;

  // Display data: server-side already paginated, client-side needs slicing
  const displayData = isServerSide
    ? filteredData
    : filteredData.slice((page - 1) * pageSize, page * pageSize);

  // Totals for info text and pagination
  const totalCount = isServerSide ? serverSide.totalCount : filteredData.length;
  const totalPages = Math.ceil(totalCount / pageSize) || 1;

  const startEntry = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const endEntry = Math.min(page * pageSize, totalCount);
  // The page size only matters once there is more than one page of the smallest size.
  const showsPageSize = totalCount > pageSizeOptions[0] || pageSize !== pageSizeOptions[0];

  const handlePageChange = (newPage: number) => {
    if (newPage < 1 || newPage > totalPages) return;
    if (isServerSide) {
      serverSide.onPageChange(newPage);
    } else {
      setClientPage(newPage);
    }
  };

  const handlePageSizeChange = (newSize: number) => {
    if (isServerSide) {
      serverSide.onPageSizeChange(newSize);
    } else {
      setClientPageSize(newSize);
      setClientPage(1);
    }
  };

  const handleSearchChange = (value: string) => {
    setSearch(value);
    if (!isServerSide) {
      setClientPage(1);
    }
  };

  // Page numbers with ellipsis for large page counts
  const getPageNumbers = (): (number | 'dots')[] => {
    if (totalPages <= 7) {
      return Array.from({ length: totalPages }, (_, i) => i + 1);
    }
    const pages: (number | 'dots')[] = [1];
    if (page > 3) pages.push('dots');
    for (let i = Math.max(2, page - 1); i <= Math.min(totalPages - 1, page + 1); i++) {
      pages.push(i);
    }
    if (page < totalPages - 2) pages.push('dots');
    pages.push(totalPages);
    return pages;
  };

  return (
    <>
      {/* One bar: the search, the screen's filters and, at the end, the action of the list. */}
      <div className="datatable-toolbar">
        <div className="datatable-search-area" role="search">
          <div className="datatable-search">
            <i className="ri-search-line" aria-hidden="true"></i>
            <input
              type="search"
              className="form-control form-control-sm"
              placeholder={searchPlaceholder}
              aria-label={searchPlaceholder}
              value={search}
              onChange={(e) => handleSearchChange(e.target.value)}
            />
          </div>
          {filters}
        </div>
        {toolbarEnd && <div className="datatable-toolbar-end">{toolbarEnd}</div>}
      </div>

      {/* Table with fixed/sticky header */}
      {loading ? (
        <div className="text-center py-4">
          <div className="spinner-border text-primary" role="status"></div>
        </div>
      ) : displayData.length === 0 ? (
        <div className="text-center py-4">
          <p className="text-muted mb-0">{search ? 'No se encontraron resultados.' : emptyMessage}</p>
        </div>
      ) : (
        <div className="table-responsive" style={{ maxHeight: '65vh', overflowY: 'auto' }}>
          <table className="datatable-table table table-striped table-centered nowrap w-100 mb-0">
            <thead className="table-light" style={{ position: 'sticky', top: 0, zIndex: 1 }}>
              <tr>
                {columns.map((col, idx) => (
                  <th
                    key={idx}
                    className={col.className}
                    style={col.width ? { width: col.width } : undefined}
                  >
                    {col.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {displayData.map((item) => (
                <tr key={keyExtractor(item)}>
                  {renderRow(item)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Bottom: what is shown, how many per page and the pages. */}
      {!loading && totalCount > 0 && (
        <div className="datatable-footer">
          <div className="datatable-count">
            <span className="text-muted">{startEntry}–{endEntry} de {totalCount}</span>
            {showsPageSize && (
              <label className="datatable-page-size">
                Por página
                <select
                  className="form-select form-select-sm"
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                >
                  {pageSizeOptions.map((size) => (
                    <option key={size} value={size}>{size}</option>
                  ))}
                </select>
              </label>
            )}
          </div>
          {totalPages > 1 && (
            <nav aria-label="Páginas">
              <ul className="pagination pagination-rounded mb-0">
                <li className={`page-item ${page <= 1 ? 'disabled' : ''}`}>
                  <button
                    type="button"
                    className="page-link"
                    onClick={() => handlePageChange(page - 1)}
                    disabled={page <= 1}
                    aria-label="Página anterior"
                  >
                    <i className="ri-arrow-left-s-line" aria-hidden="true"></i>
                  </button>
                </li>
                {getPageNumbers().map((p, idx) =>
                  p === 'dots' ? (
                    <li key={`dots-${idx}`} className="page-item disabled" aria-hidden="true">
                      <span className="page-link">&hellip;</span>
                    </li>
                  ) : (
                    <li key={p} className={`page-item ${p === page ? 'active' : ''}`}>
                      <button
                        type="button"
                        className="page-link"
                        onClick={() => handlePageChange(p)}
                        aria-current={p === page ? 'page' : undefined}
                      >
                        {p}
                      </button>
                    </li>
                  ),
                )}
                <li className={`page-item ${page >= totalPages ? 'disabled' : ''}`}>
                  <button
                    type="button"
                    className="page-link"
                    onClick={() => handlePageChange(page + 1)}
                    disabled={page >= totalPages}
                    aria-label="Página siguiente"
                  >
                    <i className="ri-arrow-right-s-line" aria-hidden="true"></i>
                  </button>
                </li>
              </ul>
            </nav>
          )}
        </div>
      )}
    </>
  );
}
