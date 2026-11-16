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
   - **`/mcp`**, sin cuenta, igual que hoy (ADR 0096). El JWT de la app no se lee ahí: el servidor nunca toma la sesión de la app.
   - **`/mcp/cuenta`**, con la cuenta del cliente: toda petición sin un token válido recibe **401** con `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource/mcp/cuenta", scope="cuenta"` (y `error="invalid_token"` si mandó uno que no sirve). Solo lee su propio esquema de autenticación: ni el JWT de la app ni el de la cuenta del cliente lo abren. Tiene las herramientas de `/mcp` y las de la cuenta, y sus instrucciones dicen qué sabe del cliente; sin sesión, cada petición tiene sus propias opciones, así que `/mcp` ni lista ni corre las de la cuenta, aunque reciba el mismo token, y cada herramienta de la cuenta revisa además que la identidad venga de ese esquema.
   - El cliente agrega la que quiere: «sin cuenta» en `/mcp` o «con tu cuenta» en `/mcp/cuenta` (Claude detecta el inicio de sesión). `/asistentes` dice las dos.
2. **El servidor de autorización vive en la app.** El emisor y el recurso son la dirección a la que llegó la petición (https fuera de desarrollo), no `AppUrl`: un cliente MCP revisa que los metadatos nombren la dirección que usó, así sirven el dominio público, el de Azure y un slot de staging. Los enlaces a las tiendas siguen con `AppUrl`.
   - `/.well-known/oauth-protected-resource/mcp/cuenta` (RFC 9728): el recurso `…/mcp/cuenta`, su servidor de autorización y el scope `cuenta`.
   - `/.well-known/oauth-authorization-server` (RFC 8414): `authorize`, `token`, `register` y `revoke`; `response_types` `code`; `grant_types` `authorization_code` y `refresh_token`; `code_challenge_methods` `S256`; autenticación del cliente `none` (clientes públicos); scope `cuenta`. Los demás `/.well-known` siguen en 404 (Fase 32B).
   - **`POST /oauth/register`** (registro dinámico, RFC 7591): solo clientes públicos (`token_endpoint_auth_method` `none`, `grant_types` `authorization_code` y `refresh_token`, `response_types` `code`); un nombre (se guarda sin caracteres de control y hasta 100) y de 1 a 10 `redirect_uris`, cada uno `https` o `http` a `localhost`/`127.0.0.1`/`[::1]` (el de una app de escritorio), sin fragmento ni usuario antes del host (así el sitio que muestra la pantalla de permiso es al que va). Devuelve un `client_id` al azar. Límite propio: 60 por minuto por IP. CIMD queda para después: obliga a pedir una URL que da el cliente y a cuidarse de SSRF.
   - **`GET /oauth/authorize`**: `response_type=code`, un `client_id` registrado, un `redirect_uri` idéntico a uno suyo (en `http` a este dispositivo el puerto puede cambiar, como pide el RFC 8252), `code_challenge` con `S256`, `resource` igual a la dirección canónica de `/mcp/cuenta` (sin él, es esa) y `scope` `cuenta` (o sin scope; `offline_access` se acepta y no cambia nada). Un `client_id` o `redirect_uri` que no sirve lo dice la pantalla de permiso, sin regresar a ninguna parte; lo demás regresa al `redirect_uri` con el error de OAuth y el `state`. Si todo sirve, manda al navegador a la pantalla de permiso de la app (`/autorizar`), con los mismos parámetros.
   - **`POST /oauth/token`** (formulario): `authorization_code` (con `code_verifier`, el mismo `redirect_uri` y `client_id`; `resource`, si viene, el mismo) y `refresh_token` (con rotación). Sus respuestas no se guardan en caché (`no-store`). Un código o una conexión se canjean de uno en uno: un candado de SQL Server por código o por conexión, en la transacción de la petición.
   - **`POST /oauth/revoke`** (RFC 7009): un token de acceso o de actualización termina su conexión; responde 200 sea o no uno suyo.
   - Todos fuera del contrato de la API (`openapi.json`); los de lectura y canje con el límite del servidor MCP (300 por minuto por IP).
3. **El permiso, en la app.** `/autorizar` (fuera del marco del negocio) pide iniciar sesión si no la hay y regresa con la misma solicitud; con una cuenta de un negocio pide cambiar a una de cliente (su equipo tiene permisos que un asistente no debe tener; la API responde 403). Muestra **el nombre del asistente, el sitio al que regresa** (el host del `redirect_uri`, con un aviso si es una app de este dispositivo), la cuenta con la que se conecta y lo que podrá hacer: ver tus direcciones, tus pedidos y tus favoritos, y prepararte pedidos que tú envías; y lo que no: enviar pedidos, pagar ni cambiar la cuenta. Su API es `GET` y `POST /api/v1/auth/assistant-authorizations`: «Permitir» la llama con la sesión del cliente, que **vuelve a validar todo** y da la dirección de regreso con el `code` y el `state`; «No permitir» regresa con `access_denied`. La página solo sale a una dirección `https` o de este dispositivo.
4. **Tokens opacos**, 43 caracteres al azar como los del seguimiento, guardados solo como su SHA-256 y nunca en los registros:
   - el **código**: 5 minutos, un uso, atado al cliente, al `redirect_uri`, al `code_challenge`, al recurso, al scope y a la cuenta;
   - el **de acceso**: 1 hora;
   - el **de actualización**: 30 días; cada uso lo cambia por otro, y **usar uno ya cambiado revoca la conexión** (alguien lo copió), salvo en los 10 segundos después de cambiarlo: es la misma petición mandada dos veces, que no recibe nada y no termina la conexión, como las sesiones de la app.
   - un **código usado otra vez** termina la conexión que dio (RFC 6749, 4.1.2).
   - Cada petición a `/mcp/cuenta` busca el token de acceso y revisa que no haya vencido, que su conexión siga y que la cuenta siga activa. Con eso arma la identidad de un cliente (su id, su correo y su rol), la que leen los casos de uso de la cuenta.
5. **Una conexión por asistente y cuenta** (`AssistantConnection`, la base lo exige con un índice único filtrado): el cliente OAuth, la cuenta, el scope, cuándo se dio, cuándo se usó por última vez (se escribe cada 5 minutos como mucho) y cuándo se desconectó. **Mi cuenta › Asistentes conectados** (`GET` y `DELETE /api/v1/me/assistants`) las lista con el nombre, el sitio, desde cuándo y cuándo leyó la cuenta, y «Desconectar» detiene sus tokens en su siguiente petición. Dar permiso otra vez al mismo asistente reemplaza la conexión. Una cuenta tiene hasta 20 asistentes conectados.
6. **Las herramientas de la cuenta** (solo en `/mcp/cuenta`), con los casos de uso de la cuenta:
   - `mis_direcciones`: sus direcciones guardadas, con su código postal;
   - `mis_pedidos`: sus pedidos recientes, con el negocio, el folio, cuándo, el total y cómo va;
   - `mis_favoritos`: sus negocios favoritos y si reciben pedidos ahora;
   - `pedir_otra_vez`: prepara, como `preparar_pedido` (`PrepareAssistantReorderCommand` lo manda dentro de su transacción), el pedido por confirmar con los productos de uno suyo anterior que el menú puede tomar hoy, a los precios de hoy, y dice lo que ya no (también lo que se vende por peso);
   - `preparar_pedido` y las demás de `/mcp`, igual. El enlace abre la tienda; con la sesión del cliente, la tienda ya escoge su dirección del código postal (Fase 32B).
7. **Migración `AddAssistantConnections`**: cuatro tablas, `AssistantClients`, `AssistantAuthorizationCodes`, `AssistantConnections` y `AssistantTokens`, con sus llaves a la cuenta en cascada (se van con ella) y restricciones para lo que los agregados nunca escriben. No son de un negocio: son de la cuenta del cliente, como sus favoritos (sin `ITenantScoped` ni filtro de negocio). Una limpieza diaria borra los tokens y códigos vencidos hace 7 días, las conexiones desconectadas hace 30 y los clientes registrados hace 7 días que ninguna cuenta conectó. **Su rollback se detiene (error 50065) sin cambiar nada** mientras haya una conexión activa.

## Consecuencias

- **Lo que logra:** el asistente sabe dónde entregar, qué pidió el cliente antes y en qué negocios confía, y le prepara el pedido con eso; el cliente sigue enviándolo él.
- **Lo que limita:**
  - Un asistente que no hace OAuth usa `/mcp` sin cuenta.
  - El asistente ve las direcciones y los pedidos del cliente mientras la conexión siga: el permiso lo dice y Mi cuenta la quita.
  - Sin CIMD, Claude usa el registro dinámico («Register automatically»); su identidad publicada llega con CIMD.
  - El token de acceso que ya se dio sirve hasta que se revisa en la siguiente petición: desconectar corta la siguiente. Después de renovar, el de acceso anterior sirve hasta su hora.
  - `/mcp/cuenta` y `/oauth/*` no tienen CORS: un cliente MCP que corre en el navegador no puede usarlos; Claude y ChatGPT llaman desde sus servidores.
  - El registro es abierto: cualquiera registra un cliente, que no lee nada sin el permiso de una cuenta; la limpieza borra los que nadie conectó.
- **Seguridad:** PKCE obligatorio, `redirect_uri` exacto, `resource` atado al token, tokens solo como hash, rotación con detección de reúso, límites por IP en `/oauth/*`, solo cuentas de cliente, y la pantalla de permiso muestra adónde regresa el código.
- **Reversibilidad:** la migración agrega tablas; la versión anterior funciona sobre la base migrada (los asistentes conectados dejan de entrar y vuelven con esta versión). Su rollback se detiene sin cambiar nada mientras haya una conexión activa (consulta 2 de su `verify.sql`).
