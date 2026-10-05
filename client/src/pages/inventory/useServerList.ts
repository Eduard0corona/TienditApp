import { useCallback, useEffect, useRef, useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import type { ServerSideConfig } from '../../components/common/DataTable';
import type { PaginatedList } from '../../types';

export interface ServerListQuery<TFilters> {
  filters: TFilters;
  search: string;
  pageNumber: number;
  pageSize: number;
}

interface ServerListOptions<TItem, TFilters> {
  fetch: (query: ServerListQuery<TFilters>) => Promise<PaginatedList<TItem>>;
  filters: TFilters;
  pageSize?: number;
  /** Off: nothing loads until it turns on (the list of a tab that is not open). */
  enabled?: boolean;
  errorMessage: string;
  onError: (message: string) => void;
}

export interface ServerList<TItem, TFilters> {
  data: PaginatedList<TItem>;
  loading: boolean;
  filters: TFilters;
  search: string;
  /** The pages, page size and search box of the DataTable that shows it. */
  table: ServerSideConfig;
  changeFilters: (patch: Partial<TFilters>) => void;
  reload: () => Promise<void>;
}

const EMPTY_PAGE = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

/**
 * A list searched, filtered and paged on the server. Its filters, search and page last as long as the screen, so
 * going to another tab and back keeps them; changing any of them goes back to the first page.
 */
export function useServerList<TItem, TFilters extends object>({
  fetch,
  filters: initialFilters,
  pageSize: initialPageSize = 10,
  enabled = true,
  errorMessage,
  onError,
}: ServerListOptions<TItem, TFilters>): ServerList<TItem, TFilters> {
  const [filters, setFilters] = useState(initialFilters);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [data, setData] = useState<PaginatedList<TItem>>(EMPTY_PAGE);
  const [loading, setLoading] = useState(enabled);

  // The latest callbacks, without loading the list again each time the screen renders.
  const fetchRef = useRef(fetch);
  const onErrorRef = useRef(onError);
  useEffect(() => {
    fetchRef.current = fetch;
    onErrorRef.current = onError;
  });

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setData(await fetchRef.current({ filters, search, pageNumber: page, pageSize }));
    } catch (err: unknown) {
      onErrorRef.current(getApiErrorMessage(err, errorMessage));
    } finally {
      setLoading(false);
    }
  }, [errorMessage, filters, page, pageSize, search]);

  useEffect(() => {
    if (enabled) reload();
  }, [enabled, reload]);

  const changeFilters = useCallback((patch: Partial<TFilters>) => {
    setFilters((current) => ({ ...current, ...patch }));
    setPage(1);
  }, []);

  return {
    data,
    loading,
    filters,
    search,
    table: {
      totalCount: data.totalCount,
      page,
      pageSize,
      onPageChange: setPage,
      onPageSizeChange: (size) => {
        setPageSize(size);
        setPage(1);
      },
      onSearchChange: (term) => {
        setSearch(term);
        setPage(1);
      },
    },
    changeFilters,
    reload,
  };
}
