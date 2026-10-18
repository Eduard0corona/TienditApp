import { useId, useRef, useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { tenantApi } from '../../api/tenantApi';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import PhotoOrInitial from '../common/PhotoOrInitial';
import './MenuCoverCard.css';

/**
 * The cover of the online menu (Fase 24E, ADR 0026): a wide photo across its top, as the delivery apps open a store,
 * with a preview of how it looks with the logo over it. Without one the menu shows the colors of the business. Owners
 * and admins, like the logo.
 */
export default function MenuCoverCard() {
  const { tenant, refreshTenant } = useTenant();
  const toast = useToast();
  const titleId = useId();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  if (!tenant) return null;
  const cover = tenant.coverImages?.large ?? null;

  const upload = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;
    setBusy(true);
    try {
      await tenantApi.uploadCover(file);
      await refreshTenant();
      toast.success('Portada actualizada: ya se ve en tu menú.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo subir la portada.'));
    } finally {
      setBusy(false);
      if (input.current) input.current.value = '';
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      await tenantApi.deleteCover();
      await refreshTenant();
      toast.success('Portada quitada: tu menú lleva los colores de tu negocio.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo quitar la portada.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card" aria-labelledby={titleId}>
      <div className="card-body">
        <h4 id={titleId} className="header-title mt-0 mb-1">Portada del menú</h4>
        <p className="text-muted small mb-3">
          Una foto ancha arriba de tu menú en línea, como en las apps de entrega: tu local, tus platillos o tu equipo. Sin
          portada, tu menú lleva los colores de tu negocio.
        </p>
        <div className="mc-preview" aria-hidden="true">
          <div className={`mc-cover ${cover ? 'has-photo' : ''}`}>{cover && <img src={cover} alt="" />}</div>
          <PhotoOrInitial src={tenant.logoUrl} name={tenant.name} className="mc-logo" />
          <span className="mc-name">{tenant.name}</span>
        </div>
        <input
          ref={input}
          type="file"
          className="d-none"
          accept="image/jpeg,image/png,image/webp"
          aria-label="Foto de portada"
          onChange={(event) => void upload(event)}
        />
        <div className="d-flex flex-wrap gap-2 mt-3">
          <button type="button" className="btn btn-light btn-sm" onClick={() => input.current?.click()} disabled={busy}>
            <i className="ri-image-add-line me-1" aria-hidden="true"></i>{cover ? 'Cambiar portada' : 'Subir portada'}
          </button>
          {cover && (
            <button type="button" className="btn btn-light btn-sm" onClick={() => void remove()} disabled={busy}>
              <i className="ri-delete-bin-line me-1" aria-hidden="true"></i>Quitar portada
            </button>
          )}
        </div>
        <p className="small text-muted mt-2 mb-0">JPG, PNG o WebP de hasta 5 MB. Se ve mejor horizontal, unas tres veces más ancha que alta.</p>
      </div>
    </section>
  );
}
