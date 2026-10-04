import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../api/typedClient';
import { getApiErrorMessage } from '../../api/errors';
import { tablesApi } from '../../api/tablesApi';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, TenantCapability } from '../../types';
import { buildMenuUrl, createQrPngDataUrl, createQrSvgDataUrl } from '../../utils/qr';
import { hasCapability } from '../../utils/tenantCapabilities';

const MODES: Array<{ value: OnlineMenuMode; label: string; help: string }> = [
  { value: OnlineMenuMode.Off, label: 'Apagado', help: 'Nadie fuera del negocio ve tu catálogo.' },
  { value: OnlineMenuMode.MenuOnly, label: 'Solo menú', help: 'Quien tenga el enlace o el QR ve productos y precios.' },
  { value: OnlineMenuMode.Orders, label: 'Menú con pedidos', help: 'Además piden en línea: llegan a Pedidos como "En línea" y tú los confirmas.' },
];

function downloadQr(url: string, fileName: string) {
  const link = document.createElement('a');
  link.href = createQrPngDataUrl(url);
  link.download = fileName;
  link.click();
}

/**
 * The public menu of the business: who can see it, its link and QR (also one per table), and a button to
 * share it by WhatsApp. Owners and admins only.
 */
export default function OnlineMenuCard() {
  const { tenant, capabilities, refreshTenant } = useTenant();
  const toast = useToast();
  const [saving, setSaving] = useState(false);
  const isFood = hasCapability(capabilities, TenantCapability.Food);
  const tables = useQuery({
    queryKey: ['tables', 'room'],
    queryFn: () => tablesApi.getDiningRoom(),
    enabled: isFood,
  });
  if (!tenant) return null;

  const mode = (tenant.onlineMenuMode ?? OnlineMenuMode.Off) as OnlineMenuMode;
  const menuUrl = buildMenuUrl(tenant.slug);
  const shareText = `Mira nuestro menú y haz tu pedido aquí: ${menuUrl}`;

  const changeMode = async (next: OnlineMenuMode) => {
    setSaving(true);
    try {
      await apiRequest('put', '/api/v1/auth/my-tenant/online-menu', { body: { mode: next } });
      await refreshTenant();
      toast.success(next === OnlineMenuMode.Off ? 'Menú en línea apagado.' : 'Menú en línea actualizado.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar el menú en línea.'));
    } finally {
      setSaving(false);
    }
  };

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(menuUrl);
      toast.success('Enlace copiado.');
    } catch {
      toast.info(menuUrl);
    }
  };

  return (
    <div className="card">
      <div className="card-body">
        <h4 className="header-title mt-0 mb-1">Menú en línea</h4>
        <p className="text-muted small mb-3">Tu catálogo en un enlace y un QR, para mesas, redes y WhatsApp.</p>

        <div className="d-flex flex-wrap gap-2 mb-2" role="radiogroup" aria-label="Menú en línea">
          {MODES.map((item) => (
            <button
              key={item.value}
              type="button"
              role="radio"
              aria-checked={mode === item.value}
              className={`btn btn-sm ${mode === item.value ? 'btn-primary' : 'btn-light'}`}
              disabled={saving}
              onClick={() => void changeMode(item.value)}
            >
              {item.label}
            </button>
          ))}
        </div>
        <p className="small mb-3">{MODES.find((item) => item.value === mode)?.help}</p>

        {mode !== OnlineMenuMode.Off && (
          <div className="row g-3 align-items-center">
            <div className="col-sm-auto text-center">
              <img src={createQrSvgDataUrl(menuUrl)} alt={`QR del menú de ${tenant.name}`} width={160} height={160} className="border rounded bg-white" />
            </div>
            <div className="col">
              <label className="form-label small mb-1" htmlFor="online-menu-url">Enlace</label>
              <div className="input-group mb-2">
                <input id="online-menu-url" className="form-control" readOnly value={menuUrl} onFocus={(event) => event.target.select()} />
                <button type="button" className="btn btn-light" onClick={() => void copy()}>Copiar</button>
              </div>
              <div className="d-flex flex-wrap gap-2">
                <a className="btn btn-sm btn-light" href={menuUrl} target="_blank" rel="noopener noreferrer">
                  <i className="ri-external-link-line me-1" aria-hidden="true"></i>Abrir
                </a>
                <button type="button" className="btn btn-sm btn-light" onClick={() => downloadQr(menuUrl, `menu-${tenant.slug}.png`)}>
                  <i className="ri-qr-code-line me-1" aria-hidden="true"></i>Descargar QR
                </button>
                <a className="btn btn-sm btn-success" href={`https://wa.me/?text=${encodeURIComponent(shareText)}`} target="_blank" rel="noopener noreferrer">
                  <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Compartir por WhatsApp
                </a>
              </div>
            </div>
            {isFood && mode === OnlineMenuMode.Orders && (tables.data?.tables.length ?? 0) > 0 && (
              <div className="col-12">
                <div className="small fw-semibold mb-1">QR por mesa</div>
                <p className="small text-muted mb-2">El pedido llega con el nombre de la mesa.</p>
                <div className="d-flex flex-wrap gap-2">
                  {tables.data!.tables.map((table) => (
                    <button
                      key={table.id}
                      type="button"
                      className="btn btn-sm btn-light"
                      onClick={() => downloadQr(buildMenuUrl(tenant.slug, table.name), `menu-${tenant.slug}-${table.name}.png`)}
                    >
                      <i className="ri-download-2-line me-1" aria-hidden="true"></i>{table.name}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
