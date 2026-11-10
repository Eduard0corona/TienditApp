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

  // The latest callbacks, without loading the list again each time the screen renders, and the filters, search and page
  // in sight: a reload asked for by something that started before they changed (turning a supply off) reads the ones in
  // sight, not the ones of when it started.
  const fetchRef = useRef(fetch);
  const onErrorRef = useRef(onError);
  const queryRef = useRef<ServerListQuery<TFilters>>({ filters, search, pageNumber: page, pageSize });
  useEffect(() => {
    fetchRef.current = fetch;
    onErrorRef.current = onError;
    queryRef.current = { filters, search, pageNumber: page, pageSize };
  });

  // The last page asked for: one of filters changed since that arrives later does not replace it.
  const latestRead = useRef(0);

  const reload = useCallback(async () => {
    const read = ++latestRead.current;
    setLoading(true);
    try {
      const next = await fetchRef.current(queryRef.current);
      if (read === latestRead.current) setData(next);
    } catch (err: unknown) {
      if (read === latestRead.current) onErrorRef.current(getApiErrorMessage(err, errorMessage));
    } finally {
      if (read === latestRead.current) setLoading(false);
    }
  }, [errorMessage]);

  // Each change of the filters, search or page reads the list again.
  useEffect(() => {
    if (enabled) void reload();
  }, [enabled, reload, filters, page, pageSize, search]);

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
