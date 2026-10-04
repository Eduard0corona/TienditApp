import { useCallback, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { branchesApi, type Branch } from '../api/branchesApi';
import { useTenant } from './useTenant';

export const activeBranchesKey = ['branches', 'active'] as const;

/** Active branches of the business, the default one first. */
export function useActiveBranches() {
  return useQuery({
    queryKey: activeBranchesKey,
    queryFn: () => branchesApi.getAll(true),
    staleTime: 5 * 60 * 1000,
  });
}

function storageKey(tenantId: string | undefined) {
  return tenantId ? `ta.branch.${tenantId}` : null;
}

function readStored(key: string | null): string | null {
  if (!key) return null;
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

/**
 * The branch this device works at: sales are recorded there and drawers open there. It is remembered per device
 * and business; a branch that is no longer active falls back to the default one.
 */
export function useWorkingBranch() {
  const { tenant } = useTenant();
  const key = storageKey(tenant?.id);
  const branchesQuery = useActiveBranches();
  // Remembered with the business it belongs to: after switching business the other one's choice is read.
  const [stored, setStored] = useState(() => ({ key, id: readStored(key) }));
  const storedId = stored.key === key ? stored.id : readStored(key);
  const branches = useMemo<Branch[]>(() => branchesQuery.data ?? [], [branchesQuery.data]);
  const branch = branches.find((item) => item.id === storedId)
    ?? branches.find((item) => item.isDefault)
    ?? branches[0];

  const setBranch = useCallback((branchId: string) => {
    setStored({ key, id: branchId });
    if (!key) return;
    try {
      window.localStorage.setItem(key, branchId);
    } catch {
      // Private mode or blocked storage: the choice lasts until the page is reloaded.
    }
  }, [key]);

  return { branches, branch, setBranch, hasSeveral: branches.length > 1, isLoading: branchesQuery.isLoading };
}
