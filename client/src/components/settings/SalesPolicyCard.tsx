import { useState } from 'react';
import { tenantApi } from '../../api/tenantApi';
import { getApiErrorMessage } from '../../api/errors';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OrderNotificationPolicy, type TenantSalesPolicyDto } from '../../types';
import SalesPolicyFields from './SalesPolicyFields';

/** "Impuestos y ventas" in Mi negocio: the IVA policy applies to sales made from now on. */
export default function SalesPolicyCard() {
  const { tenant, refreshTenant } = useTenant();
  const { confirm } = useConfirm();
  const toast = useToast();
  const saved: TenantSalesPolicyDto = {
    pricesIncludeTax: tenant?.pricesIncludeTax ?? false,
    defaultTaxRate: tenant?.defaultTaxRate ?? 16,
    autoCloseCounterSales: tenant?.autoCloseCounterSales ?? true,
    requireOpenCashSession: tenant?.requireOpenCashSession ?? false,
    orderNotificationPolicy: tenant?.orderNotificationPolicy ?? OrderNotificationPolicy.RemoteAndServices,
  };
  const [draft, setDraft] = useState<TenantSalesPolicyDto | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const value = draft ?? saved;
  const changed = value.pricesIncludeTax !== saved.pricesIncludeTax
    || value.defaultTaxRate !== saved.defaultTaxRate
    || value.autoCloseCounterSales !== saved.autoCloseCounterSales
    || value.requireOpenCashSession !== saved.requireOpenCashSession
    || value.orderNotificationPolicy !== saved.orderNotificationPolicy;

  const save = async () => {
    const taxChanged = value.pricesIncludeTax !== saved.pricesIncludeTax || value.defaultTaxRate !== saved.defaultTaxRate;
    if (taxChanged) {
      const accepted = await confirm({
        title: 'Cambiar cómo se calcula el IVA',
        message: 'Las ventas nuevas se cobrarán con la nueva regla. Las ventas anteriores conservan su total y su desglose.',
        confirmLabel: 'Cambiar',
      });
      if (!accepted) return;
    }
    setSaving(true);
    setError('');
    try {
      await tenantApi.updateSalesPolicy(value);
      await refreshTenant();
      setDraft(null);
      toast.success('Política de IVA y ventas guardada.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo guardar la política de IVA.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-1">Impuestos y ventas</h4>
        <p className="text-muted small mb-3">Se aplica a las ventas nuevas. Las ventas anteriores no cambian.</p>
        <SalesPolicyFields value={value} onChange={setDraft} disabled={saving} idPrefix="settings-policy" />
        {error && <div className="alert alert-danger mt-3 mb-0">{error}</div>}
        <div className="d-flex justify-content-end gap-2 mt-3">
          {changed && (
            <button type="button" className="btn btn-light" onClick={() => setDraft(null)} disabled={saving}>
              Descartar
            </button>
          )}
          <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={!changed || saving}>
            {saving ? 'Guardando…' : 'Guardar'}
          </button>
        </div>
      </div>
    </div>
  );
}
