import { useState, useMemo } from 'react';
import './DataTable.css';

export interface DataTableColumn {
  header: string;
  className?: string;
  width?: number | string;
}

interface ServerSideConfig {
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

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
  emptyMessage = 'No hay registros.',
  pageSizeOptions = DEFAULT_PAGE_SIZES,
}: DataTableProps<T>) {
  const [search, setSearch] = useState('');
  const [clientPage, setClientPage] = useState(1);
  const [clientPageSize, setClientPageSize] = useState(pageSizeOptions[0]);

  const isServerSide = !!serverSide;

  // Filter data by search (always client-side)
  const filteredData = useMemo(() => {
    if (!search || !searchFilter) return data;
    return data.filter((item) => searchFilter(item, search.toLowerCase()));
  }, [data, search, searchFilter]);

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
      {/* Top controls: entries selector + search */}
      <div className="datatable-toolbar d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3">
        <div className="d-flex align-items-center gap-2 flex-wrap">
          <span>Mostrar</span>
          <select
            className="form-select form-select-sm"
            style={{ width: 'auto' }}
            value={pageSize}
            onChange={(e) => handlePageSizeChange(Number(e.target.value))}
          >
            {pageSizeOptions.map((size) => (
              <option key={size} value={size}>{size}</option>
            ))}
          </select>
          <span>registros</span>
        </div>
        <div className="datatable-search-wrapper">
          <input
            type="search"
            className="form-control form-control-sm w-100"
            placeholder="Buscar..."
            value={search}
            onChange={(e) => handleSearchChange(e.target.value)}
          />
        </div>
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
          <table className="table table-striped table-centered nowrap w-100 mb-0">
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

      {/* Bottom: info text + pagination */}
      {!loading && totalCount > 0 && (
        <div className="datatable-footer d-flex flex-wrap justify-content-between align-items-center gap-2 mt-3">
          <span className="text-muted font-13">
            Mostrando {startEntry} a {endEntry} de {totalCount} registros
          </span>
          {totalPages > 1 && (
            <nav>
              <ul className="pagination pagination-rounded mb-0">
                <li className={`page-item ${page <= 1 ? 'disabled' : ''}`}>
                  <button
                    className="page-link"
                    onClick={() => handlePageChange(page - 1)}
                    disabled={page <= 1}
                  >
                    <i className="ri-arrow-left-s-line"></i>
                  </button>
                </li>
                {getPageNumbers().map((p, idx) =>
                  p === 'dots' ? (
                    <li key={`dots-${idx}`} className="page-item disabled">
                      <span className="page-link">&hellip;</span>
                    </li>
                  ) : (
                    <li key={p} className={`page-item ${p === page ? 'active' : ''}`}>
                      <button className="page-link" onClick={() => handlePageChange(p)}>
                        {p}
                      </button>
                    </li>
                  ),
                )}
                <li className={`page-item ${page >= totalPages ? 'disabled' : ''}`}>
                  <button
                    className="page-link"
                    onClick={() => handlePageChange(page + 1)}
                    disabled={page >= totalPages}
                  >
                    <i className="ri-arrow-right-s-line"></i>
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
