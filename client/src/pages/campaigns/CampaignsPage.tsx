import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability } from '../../types';
import { hasCapability } from '../../utils/tenantCapabilities';
import CampaignsTab from './CampaignsTab';
import CreditRemindersTab from './CreditRemindersTab';
import OptOutsTab from './OptOutsTab';
import RemindersTab from './RemindersTab';
import TemplatesTab from './TemplatesTab';

type Tab = 'campaigns' | 'templates' | 'reminders' | 'creditReminders' | 'optOuts';

/**
 * Campaigns by WhatsApp: sent in the background, with opt-outs respected and the history of each message. `?tab=` opens
 * a tab, as the reminders of the fiado do to point at their templates and what they sent (Fase 26C).
 */
export default function CampaignsPage() {
  const { capabilities } = useTenant();
  const [params] = useSearchParams();
  const tabs: Array<{ key: Tab; label: string }> = [
    { key: 'campaigns', label: 'Campañas' },
    { key: 'templates', label: 'Plantillas' },
    ...(hasCapability(capabilities, TenantCapability.Appointments)
      ? [{ key: 'reminders' as const, label: 'Recordatorios de citas' }]
      : []),
    { key: 'creditReminders', label: 'Recordatorios del fiado' },
    { key: 'optOuts', label: 'Bajas' },
  ];
  const [tab, setTab] = useState<Tab>(() => tabs.find((item) => item.key === params.get('tab'))?.key ?? 'campaigns');

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
      {tab === 'creditReminders' && <CreditRemindersTab />}
      {tab === 'optOuts' && <OptOutsTab />}
    </>
  );
}
