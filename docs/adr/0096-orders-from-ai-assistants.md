# ADR 0096: Pedidos desde asistentes de IA

- Estado: Aceptada en la Fase 32A (2026-11-15). El paso 1 está hecho; los pasos 2 y 3 son el plan.
- Fecha: 2026-11-15
- Continúa: [ADR 0027](0027-search-engines-and-link-previews.md) (lo que leen los buscadores y las vistas previas), [ADR 0021](0021-business-directory.md) (Explorar) y [ADR 0090](0090-account-cart-and-postal-code.md) (el carrito en la cuenta).

## Contexto

- **Muchos negocios ponen un bot de WhatsApp para recibir pedidos.** El dueño del producto quiere que los clientes también puedan pedir desde Claude, ChatGPT, Gemini o cualquier asistente de IA.
- **La app dibuja sus páginas en el navegador.** Un asistente que lee una página como texto, sin ejecutar JavaScript, encuentra en una tienda solo su título, su descripción y sus datos estructurados (ADR 0027): ni el menú, ni sus precios, ni cómo pedir.
- **Los protocolos de compra dentro de los asistentes** (ACP de OpenAI con Stripe, UCP de Google) existen, pero hoy son sobre todo para Estados Unidos, piden Stripe o Google Pay (no Mercado Pago) y siguen cambiando. **MCP** (Model Context Protocol) es lo que más asistentes aceptan para darles herramientas: Claude y ChatGPT lo aceptan con servidores remotos y OAuth, y Gemini en su CLI y su API.
- **Decisiones del dueño:** la opción de recibir pedidos desde asistentes viene encendida para los negocios que ya venden en línea, y un pedido como invitado hecho desde un asistente se confirma con un enlace antes de llegar al negocio.

## Decisión

Tres pasos, del que sirve a cualquier asistente al que da más a los que aceptan MCP. **En ninguno el asistente envía un pedido por su cuenta ni toca un pago:** el total, el envío y el pago los calcula el servidor como en cualquier pedido, y el negocio confirma el pedido como hoy.

1. **Paso 1 (Fase 32A, sin migración): cualquier asistente lee el menú y arma el enlace del pedido.**
   - **`/m/{slug}/menu.md`**: el menú de una tienda en Markdown, con lo que su página muestra: la tienda (dirección, WhatsApp, horario de la semana en la hora del negocio, con «las 24 horas», y si está abierta ahora, si recibe pedidos o los tiene en pausa y por qué, cómo entrega y cómo se paga, precios con o sin IVA), y bajo «Menú» sus productos por categoría con precio, precio anterior, descripción, alérgenos y unidad (por kilo, por litro…, con la cantidad del enlace en esa unidad entera), los que no se pueden pedir ahora, y sus grupos de opciones con cuántas se eligen, el precio de cada opción y las agotadas. **Cada producto y cada opción con su id.** Sale del mismo `GetPublicMenuQuery` que la tienda, con el límite por IP de las páginas públicas; se lee de nuevo en cada visita (`no-cache`, también el 404) y no se lista en los buscadores (`X-Robots-Tag: noindex`: se lista la página de la tienda). Si el menú no se puede leer, 503 con `Retry-After`, como la página de la tienda.
   - **Una tienda en pausa** (recibe pedidos en línea, pero no ahora: cerrada o sin caja abierta; la API la manda con `acceptsOrders` falso y `ordersPaused`) dice por qué y también cómo pedir: el enlace guarda el pedido para cuando vuelva a recibirlos.
   - **Lo que escribe el negocio son datos, no instrucciones**: antes de cualquier texto del negocio, el menú lo dice con sus reglas (solo los ids del menú, el total lo da la página, nunca datos de tarjeta). Cada nombre y descripción va en una línea con los caracteres que Markdown lee como marca escapados, también un guion o un número al inicio: no abre un título, una lista, un enlace, una imagen, código ni HTML de la página; una dirección escrita como texto suelto sigue siendo texto del negocio, en su línea. La descripción del negocio va citada, y una categoría con el nombre de una sección de la página («Cómo pedir») es una categoría bajo «Menú».
   - **El enlace del pedido**: `/m/{slug}?agregar={producto}:{cantidad}[:{opción}…]`, un `agregar` por producto, hasta 50, con cantidades de 1 a 99 y cada opción una vez (la API rechaza una repetida: el enlace la toma una vez), y el enlace entero de hasta 4,000 caracteres (Kestrel toma una línea de petición de 8 KB y algunos chats cortan los enlaces largos). La tienda lo lee al abrir y, ya leído su menú, llena el carrito con lo que puede tomar (`restoreLines`: el producto existe, se puede pedir ahora y sus opciones cumplen sus grupos), lo abre para revisarlo y quita el parámetro de la dirección (recargar no vuelve a llenarlo).
     - **Toma el lugar del carrito que había**, el del dispositivo y el de la cuenta, y lo dice: es el pedido que el cliente acaba de armar. Va a la cuenta como cualquier cambio hecho aquí (ADR 0090), solo con lo que el menú tiene, también si los carritos de la cuenta no se pudieron leer.
     - **Si el menú no puede tomar ninguno de sus productos, el carrito que había se queda**, y la tienda dice por qué. Si toma algunos, dice cuántos no pudo agregar, aparte los que ya no están en el menú o les falta una opción y las partes del enlace que no se entienden (un id o una cantidad que no es válida). El aviso va también dentro del pedido que se abre, que tapa la página, y lo describe.
     - **En pausa**, el pedido del enlace se guarda (en el dispositivo y en la cuenta) sin abrirse, y la tienda dice que se envía cuando vuelva a recibir pedidos. Una tienda que no recibe pedidos en línea lo dice y no llena nada; en una mesa (QR), el enlace no hace nada.
   - **`/llms.txt`**: qué es TienditApp, cómo encontrar negocios (Explorar, el mapa del sitio y la API del directorio), dónde está el menú en texto de cada uno, cómo armar el enlace y las reglas: usar solo los ids del menú, el total lo da la tienda, nunca pedir datos de tarjeta.
   - **La página de la tienda apunta al menú en texto**: un `<link rel="alternate" type="text/markdown">` en su encabezado y, primero en su cuerpo, un `<noscript>` con su nombre, su línea y el enlace al menú en texto para quien la lee sin JavaScript. La página para agendar y las demás no lo llevan.
2. **Paso 2 (Fase 32B, con migración): un servidor MCP de TienditApp.**
   - En `/mcp` (HTTP), con el SDK oficial de C# (`ModelContextProtocol.AspNetCore`). Sus herramientas: buscar negocios (como Explorar), ver el menú y el horario de uno, cotizar el envío a un código postal, preparar un pedido y ver su estado. Cada herramienta llama a los mismos casos de uso que la tienda pública (Application), con sus límites.
   - **Preparar un pedido no lo envía**: guarda un pedido por confirmar (con su vencimiento) y devuelve el resumen que calcula el servidor y un enlace. El cliente lo abre, lo revisa y lo confirma, y solo entonces llega al negocio como cualquier pedido en línea («Sin confirmar»). Así un bot no llena de pedidos a un negocio.
   - **Configuración › «Recibir pedidos desde asistentes de IA»**, encendida por omisión para los negocios que venden en línea; apagada, sus herramientas no preparan pedidos de ese negocio.
3. **Paso 3 (después): con la cuenta del cliente.** El asistente pide a nombre de una cuenta de cliente (OAuth): sus direcciones, Mis pedidos, «Pedir otra vez» y favoritos. Y ACP o UCP cuando lleguen a México con un medio de pago que sirva.

## Consecuencias

- **Lo que logra (paso 1):** cualquier asistente que lee páginas, sin integrarse con TienditApp, encuentra un negocio, lee su menú con precios y opciones, y le da al cliente un enlace con su pedido listo para revisar y enviar. El pedido entra por la página de la tienda, con todo lo que ella valida.
- **Lo que limita:**
  - El asistente tiene que leer el menú en texto o `llms.txt`; uno que solo lee la página de la tienda encuentra el enlace al menú en su `noscript`.
  - El enlace no lleva notas por producto («sin cebolla») ni los datos del cliente: se escriben en la tienda.
  - Los precios del menú en texto son los del momento en que se leyó; el total lo da la tienda al enviar.
  - Un producto por peso se agrega en unidades enteras (kilos, litros…), como en el carrito de la tienda.
  - Cada visita al menú en texto lee el menú completo, sin caché en el servidor; el límite es por IP (120 por minuto), y los asistentes que salen de pocas IP compartidas pueden toparlo. Su 429 es el de la API (sin `Retry-After`), no la página de la tienda.
- **Reversibilidad:** no hay migración. Volver a la versión anterior quita `/llms.txt`, `/m/{slug}/menu.md`, el `alternate` y el `noscript`; un enlace con `?agregar=` abre la tienda sin llenar el carrito.
