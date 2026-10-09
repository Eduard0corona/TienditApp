import { useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability } from '../../types';
import { hasCapability } from '../../utils/tenantCapabilities';
import CampaignsTab from './CampaignsTab';
import OptOutsTab from './OptOutsTab';
import RemindersTab from './RemindersTab';
import TemplatesTab from './TemplatesTab';

type Tab = 'campaigns' | 'templates' | 'reminders' | 'optOuts';

/** Campaigns by WhatsApp: sent in the background, with opt-outs respected and the history of each message. */
export default function CampaignsPage() {
  const { capabilities } = useTenant();
  const [tab, setTab] = useState<Tab>('campaigns');
  const tabs: Array<{ key: Tab; label: string }> = [
    { key: 'campaigns', label: 'Campañas' },
    { key: 'templates', label: 'Plantillas' },
    ...(hasCapability(capabilities, TenantCapability.Appointments)
      ? [{ key: 'reminders' as const, label: 'Recordatorios de citas' }]
      : []),
    { key: 'optOuts', label: 'Bajas' },
  ];

  return (
    <>
      <PageTitle title="Campañas" />
      <ul className="nav nav-tabs mb-3 flex-nowrap overflow-auto" role="tablist">
        {tabs.map((item) => (
          <li key={item.key} className="nav-item" role="presentation">
            <button
              type="button"
              role="tab"
              aria-selected={tab === item.key}
              className={`nav-link text-nowrap ${tab === item.key ? 'active' : ''}`}
              onClick={() => setTab(item.key)}
            >
              {item.label}
            </button>
          </li>
        ))}
      </ul>

      {tab === 'campaigns' && <CampaignsTab onCreateTemplate={() => setTab('templates')} />}
      {tab === 'templates' && <TemplatesTab />}
      {tab === 'reminders' && <RemindersTab />}
      {tab === 'optOuts' && <OptOutsTab />}
    </>
  );
}
