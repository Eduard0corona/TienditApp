import { useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useWorkingBranch } from '../../hooks/useWorkingBranch';
import { getAccess } from '../../utils/access';
import DailyCut from './DailyCut';
import MyDrawer from './MyDrawer';
import ShiftHistory from './ShiftHistory';

type CashTab = 'drawer' | 'shifts' | 'cut';

const TABS: Array<{ key: CashTab; label: string }> = [
  { key: 'drawer', label: 'Mi caja' },
  { key: 'shifts', label: 'Turnos' },
  { key: 'cut', label: 'Corte del día' },
];

/** Caja: each cashier's drawer, the shift history with differences and the day's cut. */
export default function CashManagement() {
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const [tab, setTab] = useState<CashTab>('drawer');
  const [refreshKey, setRefreshKey] = useState(0);
  const { branches, branch, setBranch } = useWorkingBranch();

  return (
    <>
      <PageTitle title="Caja" />

      <ul className="nav nav-tabs mb-3" role="tablist">
        {TABS.map((item) => (
          <li key={item.key} className="nav-item" role="presentation">
            <button
              type="button"
              role="tab"
              aria-selected={tab === item.key}
              className={`nav-link ${tab === item.key ? 'active' : ''}`}
              onClick={() => setTab(item.key)}
            >
              {item.label}
            </button>
          </li>
        ))}
      </ul>

      {tab === 'drawer' && (
        <MyDrawer
          branches={branches}
          workingBranchId={branch?.id}
          onBranchChosen={setBranch}
          onChanged={() => setRefreshKey((key) => key + 1)}
        />
      )}
      {tab === 'shifts' && <ShiftHistory branches={branches} canCloseOthers={access.businessSettings} refreshKey={refreshKey} />}
      {tab === 'cut' && <DailyCut branches={branches} />}
    </>
  );
}
