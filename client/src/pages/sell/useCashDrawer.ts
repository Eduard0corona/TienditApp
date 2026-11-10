import { useEffect, useState } from 'react';
import { cashApi } from '../../api/cashApi';

/** The branch of the cashier's own drawer. */
export interface DrawerBranch {
  id: string;
  name: string;
}

/**
 * The drawer that would take the cashier's payments at the branch this device sells at. Only people who manage the cash
 * drawer can see it; for them a closed drawer is worth a warning. A drawer of their own decides where the sale is made.
 */
export function useCashDrawer(canSeeCash: boolean, tenantId: string, workingBranchId: string | undefined) {
  const [cashClosed, setCashClosed] = useState(false);
  const [drawerBranch, setDrawerBranch] = useState<DrawerBranch | null>(null);

  useEffect(() => {
    if (!canSeeCash || !tenantId) return;
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
  }, [canSeeCash, tenantId, workingBranchId]);

  /** The cashier opened a drawer from Vender: the sale is made at its branch when it is their own. */
  const opened = (session: { isMine: boolean; branchId?: string | null; branchName?: string | null }) => {
    setCashClosed(false);
    if (session.isMine && session.branchId) setDrawerBranch({ id: session.branchId, name: session.branchName ?? '' });
  };

  return { cashClosed, drawerBranch, opened };
}
