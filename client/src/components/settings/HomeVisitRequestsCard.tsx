import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { appointmentsApi, type HomeVisitRequestsSettings } from '../../api/appointmentsApi';
import { getApiErrorMessage } from '../../api/errors';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { TenantCapability, type TenantProfileDto } from '../../types';
import { visitPriceText } from '../../utils/businessCategories';
import { formatMoney } from '../../utils/money';
import { hasCapability } from '../../utils/tenantCapabilities';

/**
 * Publishing or turning off the menu, or giving the business its hours, changes whether it receives visits: asked again
 * with each. Its zones live in another screen, which this one is never open at the same time as.
 */
const settingsKey = (tenant: TenantProfileDto | null | undefined) =>
  ['home-visit-requests', tenant?.onlineMenuMode, (tenant?.openingHours?.length ?? 0) > 0];

/** How the visit is charged, in the owner's words, in the order the radios show them. */
const POLICIES: { value: HomeVisitChargePolicy; label: string; help: string }[] = [
  { value: HomeVisitChargePolicy.Charged, label: 'Se cobra siempre', help: 'El cliente paga la visita aunque no contrate el trabajo.' },
  {
    value: HomeVisitChargePolicy.CreditedIfHired,
    label: 'Se descuenta si contrata',
    help: 'El cliente paga la visita y, si contrata el trabajo, se la descuentas de la cotización.',
  },
  { value: HomeVisitChargePolicy.Free, label: 'Sin costo', help: 'Revisas y cotizas sin cobrar la visita.' },
];

/** Whether customers see the visits now, and when they do not, what is missing (the server's words). */
function VisitsNotice({ settings }: { settings: HomeVisitRequestsSettings }) {
  if (settings.receivesVisits) {
    return (
      <div className="alert alert-success py-2 small">
        <i className="ri-checkbox-circle-line me-1" aria-hidden="true"></i>
        Tus clientes ya ven tus visitas a domicilio en tu tienda y en Explorar.{' '}
        <Link to={`/m/${settings.slug}`}>Ver tu tienda</Link>
      </div>
    );
  }
  if (settings.missing.length > 0) {
    return (
      <div className="alert alert-warning py-2 small">
        {settings.enabled ? 'Tus clientes todavía no ven tus visitas a domicilio. Falta:' : 'Para recibir visitas a domicilio, falta también:'}
        <ul className="mb-0 mt-1 ps-3">
          {settings.missing.map((item) => <li key={item}>{item}</li>)}
        </ul>
      </div>
    );
  }
  return <div className="alert alert-secondary py-2 small">Las visitas a domicilio están apagadas: tu tienda no las muestra.</div>;
}

function HomeVisitRequestsForm({ settings }: { settings: HomeVisitRequestsSettings }) {
  const { tenant } = useTenant();
  const toast = useToast();
  const queryClient = useQueryClient();
  const ids = useId();
  const [enabled, setEnabled] = useState(settings.enabled);
  const [serviceId, setServiceId] = useState(settings.serviceId ?? '');
  const [policy, setPolicy] = useState<HomeVisitChargePolicy>(settings.chargePolicy);
  const [saving, setSaving] = useState(false);
  const dirty = enabled !== settings.enabled || serviceId !== (settings.serviceId ?? '') || policy !== settings.chargePolicy;
  // The service picked before that cannot be booked anymore (unavailable, or its category off): kept as it is until the
  // owner picks another, and it adds nothing to the visit meanwhile.
  const stale = !!settings.serviceId && !settings.service;
  // The list brings the first services by name; the one picked always shows, even past them.
  const options = settings.service && !settings.services.some((service) => service.id === settings.service?.id)
    ? [settings.service, ...settings.services]
    : settings.services;
  const price = visitPriceText(settings.price, settings.chargePolicy, settings.pricesIncludeTax);
  const taxNote = settings.pricesIncludeTax ? '' : ' más IVA';

  const save = async () => {
    setSaving(true);
    try {
      const saved = await appointmentsApi.updateHomeVisitRequests({ enabled, serviceId: serviceId || null, chargePolicy: policy });
      queryClient.setQueryData(settingsKey(tenant), saved);
      // Receiving visits is something its card in Explorar shows: «Cómo te ven» is asked again.
      void queryClient.invalidateQueries({ queryKey: ['directory-profile'] });
      toast.success(saved.receivesVisits ? 'Guardado: tus clientes ya ven tus visitas a domicilio.' : 'Guardado.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudieron guardar las visitas a domicilio.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <VisitsNotice settings={settings} />

      <div className="form-check form-switch mb-3">
        <input
          id={`${ids}-enabled`}
          className="form-check-input"
          type="checkbox"
          role="switch"
          checked={enabled}
          onChange={(event) => setEnabled(event.target.checked)}
        />
        <label className="form-check-label" htmlFor={`${ids}-enabled`}>Recibir visitas a domicilio para cotizar</label>
        <div className="form-text">
          Tu tienda dice a qué códigos postales vas, cuánto cuesta la visita y tus precios de referencia. Por ahora el cliente
          te pide la visita por WhatsApp.
        </div>
      </div>

      <div className="mb-3">
        <label className="form-label" htmlFor={`${ids}-service`}>Servicio de la visita</label>
        <select
          id={`${ids}-service`}
          className="form-select"
          style={{ maxWidth: '26rem' }}
          value={serviceId}
          onChange={(event) => setServiceId(event.target.value)}
          aria-describedby={`${ids}-service-help`}
        >
          <option value="">Sin servicio: solo lo que cobra la zona</option>
          {stale && <option value={settings.serviceId ?? ''}>El servicio que elegiste ya no se puede agendar</option>}
          {options.map((service) => (
            <option key={service.id} value={service.id}>{service.name} · {formatMoney(service.price)}{taxNote}</option>
          ))}
        </select>
        <div id={`${ids}-service-help`} className="form-text">
          {stale
            ? 'No suma nada a la visita hasta que lo vuelvas a activar o elijas otro.'
            : 'Su precio más el costo de la zona es lo que cuesta la visita; tus demás servicios salen como precios de referencia.'}
        </div>
      </div>

      <fieldset className="mb-3">
        <legend className="form-label fs-6 mb-1">Cobro de la visita</legend>
        {POLICIES.map((option) => (
          <div className="form-check" key={option.value}>
            <input
              id={`${ids}-policy-${option.value}`}
              className="form-check-input"
              type="radio"
              name={`${ids}-policy`}
              checked={policy === option.value}
              onChange={() => setPolicy(option.value)}
              aria-describedby={`${ids}-policy-${option.value}-help`}
            />
            <label className="form-check-label" htmlFor={`${ids}-policy-${option.value}`}>{option.label}</label>
            <div id={`${ids}-policy-${option.value}-help`} className="form-text mt-0">{option.help}</div>
          </div>
        ))}
      </fieldset>

      {price && (
        <p className="small mb-3">
          <i className="ri-price-tag-3-line me-1" aria-hidden="true"></i>
          {dirty ? 'Hoy tus clientes leen' : 'Tus clientes leen'}: <strong>{price}</strong>
          {settings.price?.isStarting && ' (depende de la zona)'}.{dirty && ' Guarda para ver cómo queda.'}
        </p>
      )}

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
              setEnabled(settings.enabled);
              setServiceId(settings.serviceId ?? '');
              setPolicy(settings.chargePolicy);
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
 * «Visitas a domicilio» (Fase 29A, ADR 0053): whether a business that goes to its customers' homes to quote shows its
 * visits in its store and in Explorar, which of its services is the visit and how it is charged, what a visit costs and
 * what is missing for it (owners and admins of a business with the agenda). Where it goes and what going costs are its
 * delivery zones.
 */
export default function HomeVisitRequestsCard() {
  const { tenant, capabilities } = useTenant();
  const titleId = useId();
  const withAgenda = hasCapability(capabilities, TenantCapability.Appointments);
  const settings = useQuery({
    queryKey: settingsKey(tenant),
    queryFn: ({ signal }) => appointmentsApi.getHomeVisitRequests(signal),
    enabled: !!tenant && withAgenda,
  });
  if (!tenant || !withAgenda) return null;

  return (
    <section className="card" aria-labelledby={titleId}>
      <div className="card-body">
        <h4 id={titleId} className="header-title mt-0 mb-1">Visitas a domicilio</h4>
        <p className="text-muted small mb-3">
          Para oficios que van a casa del cliente a revisar y cotizar: plomería, electricidad, fumigación, cerrajería y más.
          A dónde vas y cuánto cuesta ir salen de tus <Link to="/settings/delivery-zones">zonas de entrega</Link>.
        </p>
        {settings.isLoading && <div className="spinner-border spinner-border-sm text-primary" role="status"><span className="visually-hidden">Cargando…</span></div>}
        {settings.isError && (
          <div className="alert alert-danger py-2 small">{getApiErrorMessage(settings.error, 'No se pudieron cargar las visitas a domicilio.')}</div>
        )}
        {settings.data && (
          <HomeVisitRequestsForm
            key={`${settings.data.enabled}|${settings.data.serviceId ?? ''}|${settings.data.chargePolicy}|${settings.data.receivesVisits}|${settings.data.missing.join('|')}`}
            settings={settings.data}
          />
        )}
      </div>
    </section>
  );
}
