import { useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Link } from 'react-router-dom';
import BrandLogo from '../../components/common/BrandLogo';
import ThemeToggle from '../../components/layout/ThemeToggle';
import { useAuth } from '../../hooks/useAuth';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { BusinessTheme } from '../../types';
import { previewBusinessTheme } from '../../utils/theme';
import {
  AFTER,
  BEFORE,
  BENEFITS,
  BUSINESS_TYPES,
  FAQ,
  FEATURE_GROUPS,
  FLOW_STEPS,
  INDUSTRIES,
  LANDING_TITLE,
  NAV_LINKS,
  PILLARS,
  PLAN_INCLUDES,
  SIGN_UP_PATH,
  STEPS,
} from './landingContent';
import './BusinessLandingPage.css';

/**
 * TienditApp presented to whoever has a business (/negocios): what it does for each type of business, why join and how
 * to start. Every call to action opens the registration on the business account. The server writes the same title and
 * description for search engines (PublicPages.cs).
 */
export default function BusinessLandingPage() {
  const { isAuthenticated, isLoading } = useAuth();
  useDocumentTitle(LANDING_TITLE);

  // TienditApp's colors, not those of the last business opened on this device: before the first paint.
  useLayoutEffect(() => {
    previewBusinessTheme(BusinessTheme.Nopal);
    return () => previewBusinessTheme(undefined);
  }, []);

  return (
    <div className="lp-page">
      <a className="visually-hidden-focusable lp-skip" href="#contenido">Saltar al contenido</a>
      <header className="lp-header">
        <div className="lp-container lp-header-inner">
          <a href="#inicio" className="lp-brand" aria-label="TienditApp, ir al inicio de la página">
            <BrandLogo />
          </a>
          <nav className="lp-nav" aria-label="Secciones">
            {NAV_LINKS.map((link) => (
              <a key={link.href} href={link.href}>{link.label}</a>
            ))}
          </nav>
          <div className="lp-actions">
            <span className="lp-theme">
              <ThemeToggle />
            </span>
            {!isLoading && !isAuthenticated && (
              <>
                <Link to="/login" className="btn btn-sm btn-light">Entrar</Link>
                <Link to={SIGN_UP_PATH} className="btn btn-sm btn-primary">Empieza gratis</Link>
              </>
            )}
            {!isLoading && isAuthenticated && (
              <Link to="/" className="btn btn-sm btn-primary">Abrir TienditApp</Link>
            )}
          </div>
        </div>
      </header>

      <main id="contenido">
        <Hero />
        <Industries />
        <Mission />
        <Flow />
        <BusinessTypes />
        <Features />
        <Explore />
        <Benefits />
        <BeforeAfter />
        <Steps />
        <Plans />
        <Questions />
        <ClosingCall />
      </main>

      <footer className="lp-footer">
        <div className="lp-container lp-footer-inner">
          <BrandLogo />
          <ul className="lp-footer-links">
            <li><Link to="/explorar">Explorar negocios</Link></li>
            <li><Link to="/login">Entrar</Link></li>
            <li><Link to={SIGN_UP_PATH}>Crear cuenta de negocio</Link></li>
            <li><a href="#preguntas">Preguntas frecuentes</a></li>
          </ul>
          <p className="lp-footer-note">© {new Date().getFullYear()} TienditApp · Hecho para los negocios de México.</p>
        </div>
      </footer>
    </div>
  );
}

function SectionHead({ eyebrow, title, lead, id }: { eyebrow: string; title: string; lead?: string; id: string }) {
  return (
    <div className="lp-section-head">
      <p className="lp-eyebrow">{eyebrow}</p>
      <h2 id={id} className="lp-h2">{title}</h2>
      {lead && <p className="lp-lead">{lead}</p>}
    </div>
  );
}

function SignUpButton({ className = 'btn-primary' }: { className?: string }) {
  return (
    <Link className={`btn btn-lg ${className}`} to={SIGN_UP_PATH}>
      Crea tu cuenta gratis <i className="ri-arrow-right-line" aria-hidden="true"></i>
    </Link>
  );
}

function Hero() {
  return (
    <section className="lp-hero" id="inicio" aria-labelledby="lp-title">
      <div className="lp-container lp-hero-grid">
        <div>
          <p className="lp-eyebrow"><i className="ri-store-2-line" aria-hidden="true"></i>Para los negocios locales de México</p>
          <h1 id="lp-title" className="lp-title">
            Vende, cobra y factura <span className="lp-title-accent">desde una sola app</span>
          </h1>
          <p className="lp-hero-lead">
            TienditApp es el punto de venta, la tienda en línea y la administración de tu negocio: pedidos, cocina, citas,
            inventario, caja y facturación CFDI conectados entre sí, en el celular, la tablet o la computadora que ya tienes.
          </p>
          <div className="lp-cta-row">
            <SignUpButton />
            <a className="btn btn-lg btn-light" href="#como-funciona">Ver cómo funciona</a>
          </div>
          <ul className="lp-trust">
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i>Sin tarjeta de crédito</li>
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i>Sin instalar nada</li>
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i>Sin comisión por venta</li>
          </ul>
        </div>
        <HeroShowcase />
      </div>
    </section>
  );
}

/** The app drawn with the page's own colors: Vender on a laptop and the online menu on a phone. */
function HeroShowcase() {
  return (
    <div
      className="lp-showcase"
      role="img"
      aria-label="Vender de TienditApp con productos y el ticket de la venta, y el menú en línea del negocio en un teléfono"
    >
      <div className="lp-device" aria-hidden="true">
        <div className="lp-device-bar">
          <span className="lp-dot"></span>
          <span className="lp-dot"></span>
          <span className="lp-dot"></span>
          <span>Vender · Café La Esquina</span>
        </div>
        <div className="lp-sell">
          <div>
            <div className="lp-chips">
              <span className="lp-chip lp-chip-on">Bebidas</span>
              <span className="lp-chip">Desayunos</span>
              <span className="lp-chip">Pan</span>
              <span className="lp-chip">Para llevar</span>
            </div>
            <div className="lp-tiles">
              <span className="lp-tile"><i className="ri-cup-line"></i><b>Latte</b>$55.00</span>
              <span className="lp-tile"><i className="ri-cup-line"></i><b>Café de olla</b>$35.00</span>
              <span className="lp-tile"><i className="ri-drinks-2-line"></i><b>Horchata</b>$30.00</span>
              <span className="lp-tile"><i className="ri-cake-3-line"></i><b>Concha</b>$18.00</span>
              <span className="lp-tile"><i className="ri-restaurant-line"></i><b>Chilaquiles</b>$95.00</span>
              <span className="lp-tile"><i className="ri-cake-2-line"></i><b>Pay de queso</b>$48.00</span>
            </div>
          </div>
          <div className="lp-ticket">
            <span className="lp-ticket-title">Mesa 3</span>
            <span className="lp-ticket-row"><span>2 Latte grande</span><span>$130.00</span></span>
            <span className="lp-ticket-row"><span>1 Chilaquiles</span><span>$95.00</span></span>
            <span className="lp-ticket-row"><span>2 Concha</span><span>$36.00</span></span>
            <span className="lp-ticket-row lp-ticket-total"><span>Total</span><span>$261.00</span></span>
            <span className="lp-ticket-pay">Cobrar</span>
          </div>
        </div>
      </div>
      <div className="lp-phone" aria-hidden="true">
        <div className="lp-phone-cover"></div>
        <div className="lp-phone-body">
          <span className="lp-phone-name">Café La Esquina</span>
          <span className="lp-chip lp-chip-on">A domicilio</span>
          <span className="lp-phone-item"><span>Latte</span><span>$55</span></span>
          <span className="lp-phone-item"><span>Concha</span><span>$18</span></span>
          <span className="lp-phone-item"><span>Chilaquiles</span><span>$95</span></span>
          <span className="lp-phone-cta">Ver mi pedido · $168</span>
        </div>
      </div>
      <div className="lp-float lp-float-order" aria-hidden="true">
        <i className="ri-notification-3-line"></i>
        <span><b>Nuevo pedido en línea</b>A domicilio · $168.00</span>
      </div>
      <div className="lp-float lp-float-invoice" aria-hidden="true">
        <i className="ri-file-check-line"></i>
        <span><b>Factura timbrada</b>CFDI 4.0 enviado por correo</span>
      </div>
    </div>
  );
}

function Industries() {
  return (
    <section className="lp-strip" aria-labelledby="lp-industries">
      <div className="lp-container">
        <h2 id="lp-industries" className="lp-strip-title">Para negocios como el tuyo</h2>
        <ul className="lp-strip-list">
          {INDUSTRIES.map((industry) => (
            <li key={industry.label}><i className={industry.icon} aria-hidden="true"></i>{industry.label}</li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function Mission() {
  return (
    <section className="lp-section" id="objetivo" aria-labelledby="lp-mission">
      <div className="lp-container lp-mission">
        <div>
          <p className="lp-eyebrow">Nuestro objetivo</p>
          <h2 id="lp-mission" className="lp-h2">Las herramientas de una gran cadena, para el negocio de tu colonia</h2>
          <p className="lp-mission-quote">
            Queremos que la fonda, la tiendita, la estética y el taller vendan, cobren y crezcan con la misma tecnología que
            los grandes, sin pagar ni complicarse como ellos.
          </p>
          <p className="lp-lead mt-3">
            Por eso juntamos en una sola app lo que antes eran cuadernos, calculadora, chats y varios sistemas que no se
            hablan. Tú atiendes a tus clientes; TienditApp lleva las cuentas.
          </p>
        </div>
        <ul className="lp-pillars">
          {PILLARS.map((pillar) => (
            <li key={pillar.title} className="lp-card">
              <div className="d-flex gap-3">
                <span className="lp-icon"><i className={pillar.icon} aria-hidden="true"></i></span>
                <div>
                  <h3 className="lp-h3">{pillar.title}</h3>
                  <p>{pillar.text}</p>
                </div>
              </div>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function Flow() {
  return (
    <section className="lp-section lp-section-alt" id="como-funciona" aria-labelledby="lp-flow">
      <div className="lp-container">
        <SectionHead
          id="lp-flow"
          eyebrow="Cómo funciona"
          title="Una venta, todo conectado"
          lead="Desde que entra un pedido hasta que lo ves en tus reportes, TienditApp hace el resto del camino por ti."
        />
        <ol className="lp-pipeline">
          {FLOW_STEPS.map((step) => (
            <li key={step.title} className="lp-pipeline-step">
              <span className="lp-icon"><i className={step.icon} aria-hidden="true"></i></span>
              <h3 className="lp-h3">{step.title}</h3>
              <p>{step.text}</p>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}

/** Comida, Comercio and Servicios as tabs: arrows, Home and End move between them, as in any tab list. */
function BusinessTypes() {
  const [selected, setSelected] = useState(0);
  const tabs = useRef<(HTMLButtonElement | null)[]>([]);
  const baseId = useId();
  const type = BUSINESS_TYPES[selected];
  const last = BUSINESS_TYPES.length - 1;

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const next = {
      ArrowRight: selected === last ? 0 : selected + 1,
      ArrowLeft: selected === 0 ? last : selected - 1,
      Home: 0,
      End: last,
    }[event.key];
    if (next === undefined) return;
    event.preventDefault();
    setSelected(next);
    tabs.current[next]?.focus();
  };

  return (
    <section className="lp-section" id="giros" aria-labelledby="lp-types">
      <div className="lp-container">
        <SectionHead
          id="lp-types"
          eyebrow="Para tu tipo de negocio"
          title="Hecho a la medida de tu giro"
          lead="Al registrarte eliges comida, comercio o servicios, y TienditApp enciende lo que tu negocio necesita. Los combinas cuando quieras."
        />
        <div className="lp-tablist" role="tablist" aria-label="Tipo de negocio">
          {BUSINESS_TYPES.map((item, index) => (
            <button
              key={item.id}
              ref={(element) => {
                tabs.current[index] = element;
              }}
              type="button"
              role="tab"
              id={`${baseId}-tab-${item.id}`}
              aria-selected={index === selected}
              aria-controls={`${baseId}-panel`}
              tabIndex={index === selected ? 0 : -1}
              className="lp-tab"
              onClick={() => setSelected(index)}
              onKeyDown={onKeyDown}
            >
              <i className={item.icon} aria-hidden="true"></i>
              {item.label}
            </button>
          ))}
        </div>
        <div
          className="lp-panel"
          role="tabpanel"
          id={`${baseId}-panel`}
          aria-labelledby={`${baseId}-tab-${type.id}`}
          tabIndex={0}
        >
          <div>
            <p className="lp-panel-for">{type.forWho}</p>
            <ul className="lp-checks">
              {type.features.map((feature) => (
                <li key={feature.title}>
                  <i className="ri-checkbox-circle-fill" aria-hidden="true"></i>
                  <span><b>{feature.title}:</b> {feature.text}</span>
                </li>
              ))}
            </ul>
          </div>
          <div className="lp-panel-side">
            <p className="lp-panel-side-title">{type.boardTitle}</p>
            <ul className="lp-flow" aria-label={`Ejemplo: ${type.boardTitle.toLowerCase()}`}>
              {type.board.map((row) => (
                <li key={row.text}>
                  <i className={row.icon} aria-hidden="true"></i>
                  <span>{row.text}</span>
                  <span className={`lp-flow-state lp-state-${row.tone}`}>{row.state}</span>
                </li>
              ))}
            </ul>
            <SignUpButton className="btn-primary mt-auto" />
          </div>
        </div>
      </div>
    </section>
  );
}

function Features() {
  return (
    <section className="lp-section lp-section-alt" id="funciones" aria-labelledby="lp-features">
      <div className="lp-container">
        <SectionHead
          id="lp-features"
          eyebrow="Funciones"
          title="Todo lo que tu negocio necesita, en un solo lugar"
          lead="Enciende solo lo que usas. Todo comparte los mismos productos, clientes, caja e inventario."
        />
        {FEATURE_GROUPS.map((group) => (
          <div key={group.id} className="lp-group">
            <div className="lp-group-head">
              <h3 className="lp-group-title">{group.title}</h3>
              <p className="lp-lead">{group.text}</p>
            </div>
            <ul className="lp-grid lp-grid-3">
              {group.items.map((item) => (
                <li key={item.title} className="lp-card">
                  <span className="lp-icon"><i className={item.icon} aria-hidden="true"></i></span>
                  <h4 className="lp-h3">{item.title}</h4>
                  <p>{item.text}</p>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </section>
  );
}

function Explore() {
  return (
    <section className="lp-section" id="explorar" aria-labelledby="lp-explore">
      <div className="lp-container lp-explore">
        <div>
          <p className="lp-eyebrow">Clientes nuevos</p>
          <h2 id="lp-explore" className="lp-h2">Te encuentran en Explorar</h2>
          <p className="lp-lead mb-4">
            Explorar es el directorio de TienditApp: ahí los clientes de tu ciudad buscan dónde comer, comprar o agendar, y
            llegan directo a tu menú. Apareces en cuanto publicas tu menú en línea.
          </p>
          <ul className="lp-checks">
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>Búsqueda por negocio, producto y categoría.</span></li>
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>«Cerca de mí», con la ubicación de tu sucursal.</span></li>
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>Tu cliente ve con su código postal si le entregas y cuánto cuesta el envío.</span></li>
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>Reseñas que respondes tú y favoritos para que te vuelvan a pedir.</span></li>
          </ul>
          <Link className="btn btn-light mt-4" to="/explorar">Ver Explorar</Link>
        </div>
        <div className="lp-explore-cards" role="img" aria-label="Tarjetas de negocios en Explorar, con su categoría, su ciudad y si reciben pedidos">
          <ExploreCard initial="C" name="Café La Esquina" detail="Comida · Puebla" state="Pide en línea" tone="ready" />
          <ExploreCard initial="A" name="Abarrotes Doña Lupe" detail="Abarrotes · Puebla" state="A domicilio" tone="confirmed" />
          <ExploreCard initial="B" name="Barbería Don Luis" detail="Belleza y bienestar · Cholula" state="Agenda tu cita" tone="preparing" />
        </div>
      </div>
    </section>
  );
}

function ExploreCard({ initial, name, detail, state, tone }: {
  initial: string;
  name: string;
  detail: string;
  state: string;
  tone: 'ready' | 'confirmed' | 'preparing';
}) {
  return (
    <div className="lp-store" aria-hidden="true">
      <span className="lp-store-initial">{initial}</span>
      <span className="lp-store-text"><b>{name}</b>{detail}</span>
      <span className={`lp-flow-state lp-state-${tone}`}>{state}</span>
    </div>
  );
}

function Benefits() {
  return (
    <section className="lp-section lp-section-alt" id="ventajas" aria-labelledby="lp-benefits">
      <div className="lp-container">
        <SectionHead
          id="lp-benefits"
          eyebrow="Ventajas"
          title="Lo que ganas al ser parte de TienditApp"
          lead="Más ventas, menos horas de captura y las cuentas claras, desde el primer día."
        />
        <ul className="lp-grid lp-grid-4">
          {BENEFITS.map((benefit) => (
            <li key={benefit.title} className="lp-card">
              <span className="lp-icon"><i className={benefit.icon} aria-hidden="true"></i></span>
              <h3 className="lp-h3">{benefit.title}</h3>
              <p>{benefit.text}</p>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function BeforeAfter() {
  return (
    <section className="lp-section" aria-labelledby="lp-compare">
      <div className="lp-container">
        <SectionHead id="lp-compare" eyebrow="Antes y después" title="Del cuaderno a tu celular" />
        <div className="lp-compare">
          <div className="lp-compare-col lp-compare-before">
            <h3 className="lp-h3"><i className="ri-book-2-line" aria-hidden="true"></i>Sin TienditApp</h3>
            <ul className="lp-checks">
              {BEFORE.map((line) => (
                <li key={line}><i className="ri-close-circle-line" aria-hidden="true"></i><span>{line}</span></li>
              ))}
            </ul>
          </div>
          <div className="lp-compare-col lp-compare-after">
            <h3 className="lp-h3"><i className="ri-smartphone-line" aria-hidden="true"></i>Con TienditApp</h3>
            <ul className="lp-checks">
              {AFTER.map((line) => (
                <li key={line}><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>{line}</span></li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </section>
  );
}

function Steps() {
  return (
    <section className="lp-section lp-section-alt" id="como-empezar" aria-labelledby="lp-steps">
      <div className="lp-container">
        <SectionHead
          id="lp-steps"
          eyebrow="Cómo empezar"
          title="Tu primera venta, hoy mismo"
          lead="Sin instalar nada y sin hablar con un vendedor: te registras y la guía de primeros pasos te lleva hasta tu primera venta."
        />
        <ol className="lp-steps">
          {STEPS.map((step) => (
            <li key={step.title} className="lp-step">
              <h3 className="lp-h3">{step.title}</h3>
              <p>{step.text}</p>
            </li>
          ))}
        </ol>
        <div className="lp-cta-row justify-content-center mt-4">
          <SignUpButton />
        </div>
      </div>
    </section>
  );
}

function Plans() {
  return (
    <section className="lp-section" id="planes" aria-labelledby="lp-plans">
      <div className="lp-container lp-plans">
        <div>
          <p className="lp-eyebrow">Planes</p>
          <h2 id="lp-plans" className="lp-h2">Pagas tu plan, no un porcentaje de cada venta</h2>
          <p className="lp-lead mb-4">
            Cada plan es mensual, en pesos, y dice qué módulos, cuántas sucursales y cuántas personas de tu equipo incluye.
            Cuando tu negocio crezca, cambias a uno más grande.
          </p>
          <ul className="lp-checks">
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>Crear tu cuenta es gratis y no pide tarjeta.</span></li>
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>Los pagos en línea de tus clientes llegan directo a tu Mercado Pago.</span></li>
            <li><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>Si un pago se atrasa, tienes 7 días de gracia antes de que se pause tu cuenta.</span></li>
          </ul>
        </div>
        <div className="lp-plan-card">
          <h3 className="lp-h3">Todos los planes incluyen</h3>
          <ul className="lp-checks mt-3">
            {PLAN_INCLUDES.map((line) => (
              <li key={line}><i className="ri-checkbox-circle-fill" aria-hidden="true"></i><span>{line}</span></li>
            ))}
          </ul>
          <Link className="btn btn-primary" to={SIGN_UP_PATH}>Crea tu cuenta gratis</Link>
        </div>
      </div>
    </section>
  );
}

function Questions() {
  return (
    <section className="lp-section lp-section-alt" id="preguntas" aria-labelledby="lp-faq">
      <div className="lp-container">
        <SectionHead id="lp-faq" eyebrow="Preguntas frecuentes" title="Lo que nos preguntan los negocios" />
        <div className="lp-faq">
          {FAQ.map((item) => (
            <details key={item.question}>
              <summary>
                {item.question}
                <i className="ri-arrow-down-s-line lp-faq-icon" aria-hidden="true"></i>
              </summary>
              <p>{item.answer}</p>
            </details>
          ))}
        </div>
      </div>
    </section>
  );
}

function ClosingCall() {
  return (
    <section className="lp-final" aria-labelledby="lp-final">
      <div className="lp-container">
        <h2 id="lp-final" className="lp-h2">Tu negocio, listo para vender en línea hoy</h2>
        <p className="lp-lead">Crea tu cuenta gratis, sigue los primeros pasos y haz tu primera venta con TienditApp.</p>
        <div className="lp-cta-row">
          <SignUpButton className="btn-light" />
          <Link className="btn btn-lg lp-btn-on-accent" to="/login">Ya tengo cuenta</Link>
        </div>
      </div>
    </section>
  );
}
