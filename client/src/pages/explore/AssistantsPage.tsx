import { Link } from 'react-router-dom';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { useToast } from '../../hooks/useToast';
import { mcpAccountServerUrl, mcpServerUrl } from '../../utils/mcpServer';
import './AssistantsPage.css';

const EXAMPLES = [
  'Busca en TienditApp quién vende tacos al pastor y entrega en el 72000.',
  'Muéstrame el menú de Café Luna y prepárame dos lattes grandes.',
  '¿Ya llegó el pedido que me preparaste?',
  'Pídeme otra vez lo de la semana pasada a mi casa.',
];

/**
 * How a customer orders from their AI assistant (Fase 32D, ADR 0096): TienditApp's MCP server, how to add it to Claude and
 * to other assistants, what to do with one that takes no connectors, and that nothing reaches a business until the
 * customer sends it from its page. With the customer's account (Fase 32E, ADR 0097), the same server at /mcp/cuenta also
 * knows their addresses, orders and favorites, once they allow it.
 */
export default function AssistantsPage() {
  useDocumentTitle('Pide con tu asistente de IA · TienditApp');
  const toast = useToast();
  const url = mcpServerUrl(window.location.origin);
  const accountUrl = mcpAccountServerUrl(window.location.origin);
  const command = `claude mcp add --transport http tienditapp ${url}`;
  const accountCommand = `claude mcp add --transport http tienditapp-cuenta ${accountUrl}`;

  const copy = async (text: string, said: string) => {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(said);
    } catch {
      toast.info(text);
    }
  };

  return (
    <div className="as-page">
      <header className="as-hero">
        <h1 className="as-title">Pide con tu asistente de IA</h1>
        <p className="as-lead">
          Pídele a Claude, ChatGPT u otro asistente que busque en los negocios de TienditApp, te muestre su menú y te prepare
          el pedido. Tú lo revisas y lo envías desde la página del negocio: nada le llega al negocio hasta que lo envías.
        </p>
      </header>

      <section className="card" aria-labelledby="as-address">
        <div className="card-body">
          <h2 id="as-address" className="h5 mt-0">La dirección para tu asistente</h2>
          <p className="text-muted small">Agrega una como conector (servidor MCP).</p>
          <label className="form-label small mb-1" htmlFor="as-url">Dirección sin cuenta</label>
          <div className="input-group">
            <input id="as-url" className="form-control" value={url} readOnly aria-describedby="as-url-help" />
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => void copy(url, 'Dirección copiada.')}
              aria-label="Copiar la dirección sin cuenta"
            >
              Copiar
            </button>
          </div>
          <p id="as-url-help" className="small text-muted mt-1">Busca negocios y te prepara pedidos. No pide cuenta ni contraseña.</p>
          <label className="form-label small mb-1" htmlFor="as-account-url">Dirección con tu cuenta</label>
          <div className="input-group">
            <input id="as-account-url" className="form-control" value={accountUrl} readOnly aria-describedby="as-account-url-help" />
            <button
              type="button"
              className="btn btn-light"
              onClick={() => void copy(accountUrl, 'Dirección copiada.')}
              aria-label="Copiar la dirección con tu cuenta"
            >
              Copiar
            </button>
          </div>
          <p id="as-account-url-help" className="small text-muted mt-1 mb-0">
            Además conoce tus direcciones, tus pedidos y tus favoritos: tu asistente te pide iniciar sesión en TienditApp y dar permiso.
          </p>
        </div>
      </section>

      <div className="as-grid">
        <section className="card" aria-labelledby="as-claude">
          <div className="card-body">
            <h2 id="as-claude" className="h5 mt-0">En Claude</h2>
            <p className="text-muted small">En la web, la app de escritorio o el teléfono.</p>
            <ol className="as-steps">
              <li>Abre <strong>Customize › Connectors</strong> (los nombres pueden cambiar con el idioma).</li>
              <li>Elige <strong>Add › Add custom connector</strong>.</li>
              <li>
                Escribe «TienditApp» y una dirección. Sin cuenta, en la autenticación elige <strong>No sign-in</strong>; con tu
                cuenta, Claude te manda a TienditApp a iniciar sesión y dar permiso.
              </li>
              <li>En una conversación, préndelo desde <strong>+ › Connectors</strong>.</li>
            </ol>
            <p className="small mb-1">En Claude Code:</p>
            <div className="input-group input-group-sm mb-2">
              <input className="form-control as-code" value={command} readOnly aria-label="Comando para Claude Code" />
              <button
                type="button"
                className="btn btn-light"
                onClick={() => void copy(command, 'Comando copiado.')}
                aria-label="Copiar el comando sin cuenta"
              >
                Copiar
              </button>
            </div>
            <div className="input-group input-group-sm">
              <input className="form-control as-code" value={accountCommand} readOnly aria-label="Comando para Claude Code con tu cuenta" />
              <button
                type="button"
                className="btn btn-light"
                onClick={() => void copy(accountCommand, 'Comando copiado.')}
                aria-label="Copiar el comando con tu cuenta"
              >
                Copiar
              </button>
            </div>
            <p className="small text-muted mt-1 mb-0">Con tu cuenta, entra después con <code>/mcp</code> en Claude Code.</p>
          </div>
        </section>

        <section className="card" aria-labelledby="as-others">
          <div className="card-body">
            <h2 id="as-others" className="h5 mt-0">En ChatGPT y otros asistentes</h2>
            <p className="small">
              Si tu asistente acepta conectores o servidores MCP, agrégalo con la dirección sin cuenta y sin autenticación, o con
              la de tu cuenta si acepta iniciar sesión (OAuth). ChatGPT lo permite en su modo desarrollador y Gemini como app
              personalizada, según tu plan y tu país.
            </p>
            <p className="small text-muted mb-0">
              Los menús de los asistentes cambian seguido: si no encuentras la opción, busca «conector» o «MCP» en su ayuda.
            </p>
          </div>
        </section>

        <section className="card" aria-labelledby="as-without">
          <div className="card-body">
            <h2 id="as-without" className="h5 mt-0">¿Tu asistente no acepta conectores?</h2>
            <p className="small mb-0">
              Dale la página del negocio y pídele que arme tu pedido: lee su menú en texto y te da un enlace con el pedido
              listo para revisar y enviar. Encuentra los negocios en <Link to="/explorar">Explorar</Link>.
            </p>
          </div>
        </section>

        <section className="card" aria-labelledby="as-account">
          <div className="card-body">
            <h2 id="as-account" className="h5 mt-0">Con tu cuenta</h2>
            <p className="small">
              Tu asistente usa las direcciones que guardaste, ve tus pedidos y en qué negocios confías, y te prepara otra vez lo
              que pediste antes. Antes de conectarlo, TienditApp te muestra quién lo pide y a qué sitio regresa, y tú decides.
            </p>
            <p className="small mb-0">
              Solo con una cuenta de cliente. Lo desconectas cuando quieras en <Link to="/cuenta">Mi cuenta</Link>.
            </p>
          </div>
        </section>

        <section className="card" aria-labelledby="as-examples">
          <div className="card-body">
            <h2 id="as-examples" className="h5 mt-0">Qué pedirle</h2>
            <ul className="as-examples">
              {EXAMPLES.map((example) => <li key={example}>«{example}»</li>)}
            </ul>
          </div>
        </section>
      </div>

      <section className="as-note" aria-labelledby="as-safe">
        <h2 id="as-safe" className="h6">Lo que hace tu asistente y lo que no</h2>
        <ul className="small mb-0">
          <li>Busca negocios, lee su menú y te prepara el pedido con los precios de la página.</li>
          <li>No envía el pedido: lo revisas y lo envías tú en la página del negocio.</li>
          <li>Sin cuenta no ve tus datos; con tu cuenta ve tus direcciones, tus pedidos y tus favoritos mientras lo tengas conectado.</li>
          <li>Nunca le des datos de tarjeta. El pago se elige en la página al enviar el pedido.</li>
          <li>El enlace del pedido vence en un día.</li>
        </ul>
      </section>
    </div>
  );
}
