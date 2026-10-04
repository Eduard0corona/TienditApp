import { QueryClient } from '@tanstack/react-query';

/**
 * Server state cache. Screens migrated to TanStack Query read through hooks in api/queries.ts; mutations
 * invalidate the keys they change. Errors are not retried more than once: most are permissions or validation.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
  },
});
