import { useId, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { useHomeVisitRequests } from '../../api/homeVisitRequests';
import { publicDirectoryApi, type DirectoryProfile } from '../../api/publicDirectoryApi';
import { DirectoryVisibility, type BusinessCategory } from '../../api/generated/enums';
import DirectoryBusinessCard from '../explore/DirectoryBusinessCard';
import HomeVisitAddressNotice from './HomeVisitAddressNotice';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode } from '../../types';
import { BUSINESS_CATEGORIES, categoryInfo } from '../../utils/businessCategories';

// The same limit as the API (Tenant.MaxPublicDescriptionLength).
const MAX_DESCRIPTION = 160;

/** Whether Explorar lists the business, and the one thing to do when it does not. */
function VisibilityNotice({ visibility, category }: { visibility: DirectoryVisibility; category: BusinessCategory }) {
  switch (visibility) {
    case DirectoryVisibility.Shown:
      return (
        <div className="alert alert-success py-2 small">
          <i className="ri-checkbox-circle-line me-1" aria-hidden="true"></i>
          Apareces en Explorar.{' '}
          <Link to={`/explorar/${categoryInfo(category)?.slug ?? ''}`}>Ver cómo te encuentran</Link>
        </div>
      );
    case DirectoryVisibility.HiddenByOwner:
      return <div className="alert alert-secondary py-2 small">No apareces en Explorar: elegiste no aparecer.</div>;
    case DirectoryVisibility.MenuOff:
      return (
        <div className="alert alert-warning py-2 small">
          No apareces en Explorar: publica tu menú en línea (Solo menú o Menú con pedidos) para aparecer.
        </div>
      );
    case DirectoryVisibility.NoProducts:
      return (
        <div className="alert alert-warning py-2 small">
          No apareces en Explorar: tu menú todavía no muestra productos.{' '}
          <Link to="/products">Agrega o activa productos</Link>
        </div>
      );
    default:
      return <div className="alert alert-secondary py-2 small">Tu negocio no está activo, así que no aparece en Explorar.</div>;
  }
}

function DirectoryForm({ profile, onNoticeDismissed }: { profile: DirectoryProfile; onNoticeDismissed: () => void }) {
  const { tenant } = useTenant();
  // A business that receives visits to quote (Fase 29A) shows them on its card, as Explorar does.
  const visits = useHomeVisitRequests().data;
  const receivesVisits = visits?.receivesVisits ?? false;
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = useId();
  const [category, setCategory] = useState<BusinessCategory>(profile.category);
  const [description, setDescription] = useState(profile.description ?? '');
  const [listed, setListed] = useState(profile.listed);
  const [saving, setSaving] = useState(false);
  if (!tenant) return null;

  const dirty = category !== profile.category
    || description.trim() !== (profile.description ?? '')
    || listed !== profile.listed;
  const suggested = categoryInfo(profile.suggestedCategory);

  const save = async () => {
    setSaving(true);
    try {
      const saved = await publicDirectoryApi.updateProfile({ category, description: description.trim() || null, listed });
      queryClient.setQueryData(['directory-profile', tenant.onlineMenuMode, tenant.industry ?? null], saved);
      toast.success(saved.visibility === DirectoryVisibility.Shown
        ? 'Guardado: Explorar lo muestra en unos segundos.'
        : 'Guardado.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar cómo te ven en Explorar.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <VisibilityNotice visibility={profile.visibility} category={profile.category} />
      {profile.homeVisitAddressNotice && <HomeVisitAddressNotice onDismissed={onNoticeDismissed} />}

      <div className="form-check form-switch mb-3">
        <input
          id={`${ids}-listed`}
          className="form-check-input"
          type="checkbox"
          role="switch"
          checked={listed}
          onChange={(event) => setListed(event.target.checked)}
        />
        <label className="form-check-label" htmlFor={`${ids}-listed`}>Aparecer en Explorar</label>
        <div className="form-text">Mientras tu menú esté publicado. Quien tenga tu enlace o tu QR lo sigue viendo igual.</div>
      </div>

      <div className="row g-3">
        <div className="col-md-5">
          <label className="form-label" htmlFor={`${ids}-category`}>Categoría</label>
          <select
            id={`${ids}-category`}
            className="form-select"
            value={category}
            onChange={(event) => setCategory(Number(event.target.value) as BusinessCategory)}
          >
            {BUSINESS_CATEGORIES.map((item) => (
              <option key={item.value} value={item.value}>{item.label}</option>
            ))}
          </select>
          {suggested && suggested.value !== category && (
            <div className="form-text">Por tu giro te sugerimos {suggested.label}.</div>
          )}
        </div>
        <div className="col-md-7">
          <label className="form-label" htmlFor={`${ids}-description`}>Descripción corta</label>
          <input
            id={`${ids}-description`}
            className="form-control"
            maxLength={MAX_DESCRIPTION}
            placeholder="Ej. Tacos al pastor y gringas desde 1998"
            value={description}
            onChange={(event) => setDescription(event.target.value.replace(/[\r\n\t]+/g, ' '))}
            aria-describedby={`${ids}-description-help`}
          />
          <div id={`${ids}-description-help`} className="form-text">
            Lo que vendes, en un renglón. También sale con tu categoría bajo tu nombre en tu menú. {description.trim().length}/{MAX_DESCRIPTION}
          </div>
        </div>
      </div>

      <div className="small fw-semibold mt-3 mb-1">Así se ve tu tarjeta</div>
      <div className="mb-3" style={{ maxWidth: '26rem' }}>
        <DirectoryBusinessCard
          preview
          business={{
            name: tenant.name,
            slug: tenant.slug,
            logoUrl: tenant.logoImages?.small ?? tenant.logoUrl ?? null,
            // Its cover across the top, as Explorar shows it (Fase 24F).
            coverImageUrl: tenant.coverImages?.medium ?? null,
            category,
            description: description.trim() || null,
            theme: tenant.theme ?? 0,
            city: null,
            deliveryOnly: false,
            deliveryAvailable: false,
            pricesIncludeTax: receivesVisits ? visits?.pricesIncludeTax ?? true : true,
            booksOnline: false,
            receivesVisits,
            visitChargePolicy: receivesVisits ? visits?.chargePolicy : undefined,
            visitPrice: receivesVisits ? visits?.price : undefined,
            acceptsOrders: tenant.onlineMenuMode === OnlineMenuMode.Orders,
            ordersPaused: false,
            statusLabel: null,
            statusDetail: null,
            closed: false,
          }}
        />
      </div>

      <div className="d-flex gap-2">
        <button type="button" className="btn btn-primary" disabled={!dirty || saving} onClick={() => void save()}>
          {saving ? 'Guardando…' : 'Guardar'}
        </button>
        {dirty && (
          <button
            type="button"
            className="btn btn-light"
            disabled={saving}
            onClick={() => {
              setCategory(profile.category);
              setDescription(profile.description ?? '');
              setListed(profile.listed);
            }}
          >
            Descartar
          </button>
        )}
      </div>
    </>
  );
}

/**
 * «Cómo te ven en TienditApp»: the card of the business in Explorar, where customers find businesses by category. Its
 * category, a short description and whether it appears; and when it does not, why (owners and admins).
 */
export default function DirectoryCard() {
  const { tenant } = useTenant();
  const titleId = useId();
  const titleRef = useRef<HTMLHeadingElement>(null);
  // Publishing or turning off the menu changes whether it appears, and a new giro may move the category (while the
  // owner has not picked another): asked again with each, so saving never sends back a category already replaced.
  const profile = useQuery({
    queryKey: ['directory-profile', tenant?.onlineMenuMode, tenant?.industry ?? null],
    queryFn: () => publicDirectoryApi.getProfile(),
    enabled: !!tenant,
    // Its notice of the address also changes in Sucursales, and its products in the catalog: read again on each visit.
    staleTime: 0,
  });
  if (!tenant) return null;

  return (
    <section className="card" aria-labelledby={titleId}>
      <div className="card-body">
        <h4 id={titleId} ref={titleRef} tabIndex={-1} className="header-title mt-0 mb-1">Cómo te ven en TienditApp</h4>
        <p className="text-muted small mb-3">
          En Explorar los clientes encuentran negocios por categoría, abren tu menú y piden cuando lo recibes en línea.
        </p>
        {profile.isLoading && <div className="spinner-border spinner-border-sm text-primary" role="status"><span className="visually-hidden">Cargando…</span></div>}
        {profile.isError && (
          <div className="alert alert-danger py-2 small">{getApiErrorMessage(profile.error, 'No se pudo cargar cómo te ven en Explorar.')}</div>
        )}
        {profile.data && (
          <DirectoryForm
            key={`${profile.data.category}|${profile.data.description ?? ''}|${profile.data.listed}|${profile.data.visibility}`}
            profile={profile.data}
            onNoticeDismissed={() => titleRef.current?.focus()}
          />
        )}
      </div>
    </section>
  );
}
