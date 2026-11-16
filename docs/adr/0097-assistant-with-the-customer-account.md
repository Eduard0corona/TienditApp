# ADR 0097: El asistente con la cuenta del cliente (OAuth para el servidor MCP)

- Estado: Aceptada en la Fase 32E (2026-11-16).
- Fecha: 2026-11-16
- Continúa: [ADR 0096](0096-orders-from-ai-assistants.md) (su paso 3), [ADR 0022](0022-shopping-with-an-account.md) (comprar con tu cuenta), [ADR 0028](0028-product-search-and-favorites.md) (favoritos) y [ADR 0090](0090-account-cart-and-postal-code.md) (el carrito en la cuenta).

## Contexto

- **El paso 3 de la ADR 0096** quedó como plan: el asistente pide a nombre de una cuenta de cliente, con sus direcciones, sus pedidos, «Pedir otra vez» y sus favoritos. Sin cuenta (pasos 1 y 2), el asistente no sabe dónde vive el cliente ni qué pidió antes, y el cliente escribe sus datos cada vez.
- **La especificación de autorización de MCP (2025-11-25)** pide, para un servidor protegido: OAuth 2.1 con PKCE `S256`; los metadatos del recurso protegido (RFC 9728), anunciados con `WWW-Authenticate` en el 401; los del servidor de autorización (RFC 8414); el parámetro `resource` (RFC 8707) y tokens emitidos solo para ese servidor; y para registrar al cliente, documentos de metadatos (CIMD, «debería») o registro dinámico (RFC 7591, «puede»).
- **Claude y ChatGPT** agregan un conector con OAuth: lo detectan del servidor, registran su cliente solos y abren el navegador para que la persona inicie sesión y dé permiso. **El servidor de hoy, `/mcp`, no pide cuenta**: si anunciara OAuth, el asistente pediría iniciar sesión a todos.
- **La app ya tiene** la cuenta de cliente (Identity, rol cliente), sus direcciones, Mis pedidos, los favoritos y el carrito por tienda, con sus casos de uso (`GetMyAddressesQuery`, `GetMyOrdersQuery`, `GetMyFavoritesQuery`) que leen al usuario de sus claims.

## Decisión

**El asistente sigue sin enviar pedidos.** Con la cuenta, sabe más del cliente y le prepara el pedido con eso; el cliente lo revisa y lo envía desde la página del negocio, como en el paso 2.

1. **Dos direcciones del mismo servidor.**
   - **`/mcp`**, sin cuenta, igual que hoy (ADR 0096).
   - **`/mcp/cuenta`**, con la cuenta del cliente: toda petición sin un token válido recibe **401** con `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource/mcp/cuenta", scope="cuenta"`. Tiene las herramientas de `/mcp` y las de la cuenta; `/mcp` no lista las de la cuenta.
   - El cliente agrega la que quiere: «sin cuenta» en `/mcp` o «con tu cuenta» en `/mcp/cuenta` (Claude detecta el inicio de sesión). `/asistentes` dice las dos.
2. **El servidor de autorización vive en la app** (el emisor es la dirección pública de la app, `AppUrl`):
   - `/.well-known/oauth-protected-resource/mcp/cuenta` (RFC 9728): el recurso `…/mcp/cuenta`, su servidor de autorización y el scope `cuenta`.
   - `/.well-known/oauth-authorization-server` (RFC 8414): `authorize`, `token`, `register` y `revoke`; `response_types` `code`; `grant_types` `authorization_code` y `refresh_token`; `code_challenge_methods` `S256`; autenticación del cliente `none` (clientes públicos); scope `cuenta`. Los demás `/.well-known` siguen en 404 (Fase 32B).
   - **`POST /oauth/register`** (registro dinámico, RFC 7591): solo clientes públicos; un nombre de hasta 100 caracteres y de 1 a 10 `redirect_uris`, cada uno `https` o `http` a `localhost`/`127.0.0.1`/`[::1]` (el de una app de escritorio), sin fragmento. Devuelve un `client_id` al azar. Con límite por IP. CIMD queda para después: obliga a pedir una URL que da el cliente y a cuidarse de SSRF.
   - **`GET /oauth/authorize`**: `response_type=code`, un `client_id` registrado, un `redirect_uri` idéntico a uno suyo, `code_challenge` con `S256`, `resource` igual a la dirección canónica de `/mcp/cuenta` y `scope` `cuenta` (o sin scope). Un `client_id` o `redirect_uri` que no sirve se dice en una página, sin redirigir; lo demás regresa al `redirect_uri` con el error de OAuth y el `state`. Si todo sirve, manda al navegador a la pantalla de permiso de la app (`/autorizar`), con los mismos parámetros.
   - **`POST /oauth/token`**: `authorization_code` (con `code_verifier`, el mismo `redirect_uri`, `client_id` y `resource`) y `refresh_token` (con rotación).
   - **`POST /oauth/revoke`** (RFC 7009): revoca un token de actualización y con él su conexión.
3. **El permiso, en la app.** `/autorizar` (fuera del marco del negocio) pide iniciar sesión con una cuenta de cliente si no la hay (una cuenta de un negocio no se conecta: su equipo tiene permisos que un asistente no debe tener) y muestra **el nombre del asistente, el sitio al que regresa** (el host del `redirect_uri`, con un aviso si es `localhost`) y lo que podrá hacer: ver tus direcciones, tus pedidos y tus favoritos, y prepararte pedidos que tú envías. «Permitir» llama a la API con la sesión del cliente, que **vuelve a validar todo** y da la dirección de regreso con el `code` y el `state`; «No permitir» regresa con `access_denied`.
4. **Tokens opacos**, 43 caracteres al azar como los del seguimiento, guardados solo como su SHA-256 y nunca en los registros:
   - el **código**: 5 minutos, un uso, atado al cliente, al `redirect_uri`, al `code_challenge`, al recurso, al scope y a la cuenta;
   - el **de acceso**: 1 hora;
   - el **de actualización**: 30 días; cada uso lo cambia por otro, y **usar uno ya cambiado revoca la conexión** (alguien lo copió).
   - Cada petición a `/mcp/cuenta` busca el token de acceso y revisa que no haya vencido, que su conexión siga y que la cuenta siga activa. Con eso arma la identidad de un cliente (su id, su correo y su rol), la que leen los casos de uso de la cuenta.
5. **Una conexión por asistente y cuenta** (`AssistantConnection`): el cliente OAuth, la cuenta, el scope, cuándo se dio y cuándo se usó por última vez. **Mi cuenta › Asistentes conectados** las lista con «Desconectar», que revoca sus tokens al momento. Dar permiso otra vez al mismo asistente reemplaza la conexión.
6. **Las herramientas de la cuenta** (solo en `/mcp/cuenta`), con los casos de uso de la cuenta:
   - `mis_direcciones`: sus direcciones guardadas, con su código postal;
   - `mis_pedidos`: sus pedidos recientes, con el negocio, el folio, cuándo, el total y cómo va;
   - `mis_favoritos`: sus negocios favoritos y si reciben pedidos ahora;
   - `pedir_otra_vez`: prepara, como `preparar_pedido`, el pedido por confirmar con los productos de uno suyo anterior que el menú puede tomar hoy, y dice lo que ya no;
   - `preparar_pedido` y las demás de `/mcp`, igual. El enlace abre la tienda; con la sesión del cliente, la tienda ya escoge su dirección del código postal (Fase 32B).
7. **Migración `AddAssistantConnections`**: las tablas de los clientes OAuth registrados, las conexiones y los tokens (código, acceso y actualización). No son de un negocio: son de la cuenta del cliente, como sus favoritos. Una limpieza diaria borra los tokens vencidos hace 7 días y los clientes registrados que nunca se conectaron en 7 días.

## Consecuencias

- **Lo que logra:** el asistente sabe dónde entregar, qué pidió el cliente antes y en qué negocios confía, y le prepara el pedido con eso; el cliente sigue enviándolo él.
- **Lo que limita:**
  - Un asistente que no hace OAuth usa `/mcp` sin cuenta.
  - El asistente ve las direcciones y los pedidos del cliente mientras la conexión siga: el permiso lo dice y Mi cuenta la quita.
  - Sin CIMD, Claude usa el registro dinámico («Register automatically»); su identidad publicada llega con CIMD.
  - El token de acceso que ya se dio sirve hasta que se revisa en la siguiente petición: desconectar corta la siguiente.
- **Seguridad:** PKCE obligatorio, `redirect_uri` exacto, `resource` atado al token, tokens solo como hash, rotación con detección de reúso, límites por IP en `/oauth/*`, solo cuentas de cliente, y la pantalla de permiso muestra adónde regresa el código.
- **Reversibilidad:** la migración agrega tablas; su rollback se detiene sin cambiar nada mientras haya una conexión.
