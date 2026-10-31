import { useEffect, useId, useRef, useState } from 'react';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import type { Schemas } from '../../api/typedClient';
import { isFreeVisit, visitPriceText } from '../../utils/businessCategories';
import { formatMoney } from '../../utils/money';
import { businessWhatsAppUrl } from '../../utils/whatsApp';

type PublicHomeVisits = Schemas['PublicHomeVisitsDto'];

/** Postal codes shown before «Ver todos»: a zone of a whole city has hundreds. */
const FIRST_POSTAL_CODES = 18;

/** What the visit policy means for the customer, in one line. */
const POLICY_NOTE: Record<HomeVisitChargePolicy, string> = {
  [HomeVisitChargePolicy.Charged]: 'Pagas la visita aunque no contrates el trabajo.',
  [HomeVisitChargePolicy.Free]: 'Revisamos y te decimos cuánto cuesta, sin costo.',
  [HomeVisitChargePolicy.CreditedIfHired]: 'Si contratas el trabajo, lo que pagaste de la visita se descuenta.',
};

interface HomeVisitsSectionProps {
  visits: PublicHomeVisits;
  businessName: string;
  contactPhone?: string | null;
  pricesIncludeTax: boolean;
}

/**
 * «Visitas a domicilio» on the store of a business that receives visits to quote (Fase 29A, ADR 0053 point 9): what the
 * visit costs and how it is charged, the postal codes it goes to (never the name of a zone) and its services as reference
 * prices, never in a cart. «Solicitar visita» opens WhatsApp with the business until visits are asked for online.
 */
export default function HomeVisitsSection({ visits, businessName, contactPhone, pricesIncludeTax }: HomeVisitsSectionProps) {
  const titleId = useId();
  const codesId = useId();
  const [allCodes, setAllCodes] = useState(false);
  const codesRef = useRef<HTMLUListElement>(null);
  // «Ver todos» goes away once pressed: the keyboard lands on the list it opened, not at the top of the page.
  useEffect(() => {
    if (allCodes) codesRef.current?.focus();
  }, [allCodes]);
  const price = visitPriceText(visits.price, visits.chargePolicy, pricesIncludeTax);
  const request = businessWhatsAppUrl(
    contactPhone,
    `Hola ${businessName}, quiero pedir una visita a domicilio para cotizar. Mi código postal es `,
  );
  const codes = allCodes ? visits.postalCodes : visits.postalCodes.slice(0, FIRST_POSTAL_CODES);
  const hidden = visits.postalCodeCount - codes.length;
  const taxNote = pricesIncludeTax ? '' : ' más IVA';

  return (
    <section className="pm-visits" aria-labelledby={titleId}>
      <h2 id={titleId} className="pm-section-title">
        <i className="ri-home-gear-line me-2" aria-hidden="true"></i>Visitas a domicilio
      </h2>
      <p className="pm-visits-price mb-1">
        {visits.visitServiceName && <span className="pm-visits-service">{visits.visitServiceName}</span>}
        {price && <strong>{price}</strong>}
      </p>
      <p className="pm-visits-note">{POLICY_NOTE[isFreeVisit(visits.price, visits.chargePolicy) ? HomeVisitChargePolicy.Free : visits.chargePolicy]}</p>

      <h3 className="pm-visits-subtitle" id={codesId}>Vamos a estos códigos postales</h3>
      <ul ref={codesRef} className="pm-visits-codes" aria-labelledby={codesId} tabIndex={-1}>
        {codes.map((code) => <li key={code} className="ta-money">{code}</li>)}
      </ul>
      {hidden > 0 && (
        allCodes || visits.postalCodes.length <= codes.length ? (
          <p className="pm-visits-note">Y {hidden.toLocaleString('es-MX')} más: pregúntanos por el tuyo.</p>
        ) : (
          <button type="button" className="btn btn-link btn-sm px-0" onClick={() => setAllCodes(true)}>
            Ver todos los códigos postales
          </button>
        )
      )}

      {visits.services.length > 0 && (
        <>
          <h3 className="pm-visits-subtitle">Precios de referencia</h3>
          <ul className="pm-visits-services">
            {visits.services.map((service, index) => (
              <li key={`${index}-${service.name}`}>
                <span>{service.name}</span>
                <span className="ta-money">desde {formatMoney(service.price)}{taxNote}</span>
              </li>
            ))}
          </ul>
          <p className="pm-visits-note">El precio final te lo decimos después de la visita.</p>
        </>
      )}

      {request && (
        <a className="btn btn-primary pm-visits-request" href={request} target="_blank" rel="noopener noreferrer">
          <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>Solicitar visita
        </a>
      )}
    </section>
  );
}
