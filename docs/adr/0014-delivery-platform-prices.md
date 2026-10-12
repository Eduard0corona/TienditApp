# ADR 0014: Precio de cada producto en la app de cada plataforma

- Estado: Aceptada
- Fecha: 2026-10-12

## Contexto

Desde la Fase 22F ([ADR 0013](0013-delivery-platform-orders.md)), Vender registra los pedidos de Rappi, Uber Eats y DiDi Food con su folio, y cada pedido copia la comisión, las retenciones y el neto esperado. Pero los pedidos usaban los precios del menú, y las cocinas suben sus precios en las apps para cubrir la comisión. Así, el total del pedido, la comisión, las retenciones y el neto esperado salían menores que los reales. La liquidación semanal, la siguiente fase, no cuadraría con el estado de cuenta.

El total lo decide el servidor: la venta rechaza un `ExpectedTotal` distinto del que calcula. Por eso no había forma de capturar lo que cobró la app.

## Decisión

1. **Listas de precios explícitas por plataforma, nunca una fórmula al vender.**
   - Cada producto puede tener su precio en cada plataforma. Cada opción (variante o extra) puede tener lo que suma en cada plataforma.
   - Un producto o una opción sin precio propio en la app se cobra con el del menú, uno por uno.
   - «Calcular precios» (tu precio más un porcentaje) es una ayuda de captura en la pantalla: llena precios explícitos que la persona revisa antes de guardar. El servidor solo resuelve «el precio de la app o el del menú» y lo copia a la línea, como siempre.
2. **Las listas viven en el agregado `Product`.**
   - `ProductPlatformPrice` (en `Product.PlatformPrices`) y `ProductOptionPlatformPrice` (en `ProductOption.PlatformPrices`) son `ITenantScoped`, con el `TenantId` del producto.
   - `Product.PriceIn(channel)` y `ProductOption.PriceDeltaIn(channel)` usan la lista solo en un canal de plataforma. Mostrador, WhatsApp, el menú en línea, las mesas y el chat siguen con el precio del menú.
   - `Product.SetPlatformPrice(channel, price?)` y `Product.SetOptionPlatformPrice(optionId, channel, delta?)` aceptan solo canales de plataforma. Null vuelve al precio del menú.
   - **Límites:**
     - lo que suma una opción, los del menú: de −100,000 a 100,000 (negativo para una variante más barata), con dos decimales como máximo;
     - el precio de un producto, mayor a 0 y de hasta 1,000,000, con dos decimales como máximo. El tope y los decimales son propios de los precios en apps: el del menú solo pide mayor a 0.
   - `ResolveOptions(ids, channel)` copia a la línea lo que suma cada opción en ese canal, y `PriceWith(options, channel)` parte del precio del canal.
3. **Dos tablas, cada una con un solo camino de cascada hacia ella.**
   - `ProductPlatformPrices`:
     - su llave a `Products` va en cascada;
     - es única por `(ProductId, Channel)`;
     - sus CHECK piden `Channel IN (3, 4, 5)` y `Price > 0`.
   - `ProductOptionPlatformPrices`:
     - su llave a `ProductOptions` va en cascada;
     - es única por `(OptionId, Channel)`;
     - sus CHECK piden `Channel IN (3, 4, 5)` y `PriceDelta BETWEEN -100000 AND 100000`.
   - Las dos usan `decimal(18,2)` y un índice `(TenantId, Channel)`.
   - **Opciones:** el editor de opciones conserva el id de las opciones que se quedan, y con él su precio en las apps. Una opción que se quita se lleva sus precios por la cascada, y lo mismo un producto que se borra.
4. **La venta cobra el precio de la app.**
   - `CreateOrderCommand` pasa el canal a `OrderLineBuilder`. `OrderCatalog.LoadProductsAsync` incluye, filtrados por ese canal, los precios de la plataforma, y solo en un pedido de plataforma.
   - Cada línea copia el precio que cobró. Cambiar un precio después nunca reescribe un pedido, y la comisión y el neto esperado salen del total con los precios de la app.
   - `ExpectedTotal` sigue protegiendo: si el precio de la app cambió entre que Vender cargó el catálogo y registró el pedido, la venta se rechaza con el mensaje de siempre.
5. **Vender lee el catálogo con los precios de la app.**
   - `GET /products?priceChannel=<canal>` acepta solo canales de plataforma (400 si no).
   - **Qué devuelve:** el `priceAmount` de cada producto y el `priceDelta` de cada opción con la lista de esa plataforma, y los del menú donde no hay uno.
   - **El precio tachado:** `compareAtPrice` y `hasDiscount` son una oferta del menú, así que se quitan en los productos con precio de la app.
   - **Cómo se lee:** `ProductPlatformPriceReader.ApplyAsync` lee los precios de la página ya armada, como `ProductStockReader`.
   - **Al elegir el origen:** Vender recarga el catálogo al elegir una plataforma o al volver al mostrador, y recalcula el ticket con los precios nuevos.
   - **Mientras cargan:** los precios en pantalla no son todavía los del origen elegido, así que no deja cobrar ni registrar. El ticket dice «Cargando tus precios de Rappi…» y, con ellos, «Con tus precios de Rappi».
   - **Sin conexión:** el catálogo con precios de una plataforma no se guarda en el dispositivo, porque el modo sin conexión vende en el mostrador y un pedido de plataforma se registra con conexión ([ADR 0013](0013-delivery-platform-orders.md), punto 2).
6. **Catálogo › Precios en apps, para quien cambia los precios del menú.**
   - `GET /delivery-platforms/{canal}/prices` lista los productos con su precio en el menú y en la app, y sus opciones, en páginas.
     - Deja fuera los servicios, porque un pedido de plataforma lleva solo productos.
     - Ordena por categoría (su orden y su nombre) y por nombre.
   - `PUT /delivery-platforms/{canal}/prices` (204) guarda hasta 500 productos a la vez.
     - Cada producto listado toma su precio (null: el del menú).
     - Cada opción listada toma lo que suma (null: lo del menú). Las opciones que no se listan no cambian.
     - `price` y `priceDelta` son obligatorios (`[JsonRequired]`, y el contrato los marca así): un precio que nadie escribió nunca se quita por omisión. Un elemento null en la lista es una petición mal formada (400), no un error del servidor.
     - Un negocio sin el módulo de productos ni el de alimentos no guarda (400), igual que su lista sale vacía.
     - Es todo o nada: un producto ajeno o borrado, un servicio, una opción de otro producto o un precio fuera de los límites rechaza el guardado completo (400 `business_rule`). El validador (400 `validation_failed`) revisa el canal, de 1 a 500 productos, hasta 300 opciones por producto y que nada se repita.
     - Si el guardado choca en la base, la API responde 409 `concurrency_conflict` y no guarda nada: otra persona guardó al mismo tiempo el primer precio del mismo producto u opción (el índice único), o el producto, la opción o el precio se borró entre la lectura y el guardado. La pantalla vuelve a leer la lista y conserva lo escrito.
     - Dos cambios del mismo precio ya guardado no chocan: no hay token de concurrencia, y gana el último.
   - **Permiso:** las dos piden `ManageCatalog`, igual que los precios del menú. `GET /delivery-platforms/channels` ahora también responde con `ManageCatalog`, porque solo dice qué plataformas están activas, sin tasas.
   - **Bitácora:** no hay, igual que con los precios del menú. Cada línea copia el precio que cobró, así que la historia no cambia.
   - **La pantalla:**
     - Elige la plataforma: la primera activa, y las apagadas dicen «(apagada)».
     - Busca y filtra por categoría en el cliente.
     - Una caja vacía muestra como guía el precio del menú, que es el que cobra.
     - «Calcular precios» sube el precio del menú un porcentaje (más de 0 y hasta 200 %) y lo redondea hacia arriba al peso, a $5 o a $10.
       - Las opciones se redondean al peso, y una que no suma nada en el menú tampoco suma en la app.
       - Llena los productos de la categoría elegida, o todos, y solo las cajas vacías, salvo que se pida reemplazar también las que ya tienen precio.
     - Nada se guarda hasta «Guardar cambios». Cambiar de plataforma o descartar pide confirmar, y cerrar o recargar la pestaña lo pregunta el navegador.
     - La plataforma queda fija desde el primer cambio: si otra persona activa o apaga una plataforma mientras tanto, lo escrito no pasa a otra.
     - Lee hasta 500 productos (la página más grande de la API). Con más, avisa que los demás se cobran con el precio del menú.
   - **Plataformas de reparto:** el aviso de la configuración de cada plataforma dice que sus pedidos se cobran con estos precios. A quien puede abrir Catálogo le da un enlace a la pantalla; a los demás, el mismo texto sin enlace.
7. **Una migración que solo agrega, con la guarda 50009.**
   - `AddPlatformPrices` crea las dos tablas y no toca ninguna otra. Su `apply.sql` corre en producción antes del merge.
   - Su `Down` y su `rollback.sql` se detienen con el error 50009 si ya hay precios guardados: ninguna bitácora los conserva, y el rollback los borraría.

## Alternativas descartadas

- **Un porcentaje por plataforma aplicado al vender:** el precio cambiaría con cada ajuste de la tasa y no se podría poner un precio redondo ni un precio distinto en un producto. Además, la comisión se calcularía sobre un precio que nadie escribió. Con precios explícitos, el pedido cobra lo que la persona vio y guardó.
- **Capturar el total que cobró la app en cada pedido:** obligaría a escribir un total en cada pedido. También rompería la regla de que el servidor decide el total (`ExpectedTotal`), y el detalle por producto no cuadraría con el ticket.
- **Un precio por plataforma en el mismo formulario del producto:** son decenas de productos por plataforma. Una lista con todos, con el cálculo por porcentaje, es la tarea que hace la persona al subir sus precios en la app.
- **Una sola tabla para productos y opciones:** una llave a `Products` y otra a `ProductOptions` darían dos caminos de cascada hacia la misma tabla (borrar un producto borra sus opciones), que SQL Server no admite. También obligarían a un CHECK de «una de las dos».
- **Bitácora de cada cambio de precio:** los precios del menú no la tienen, y cada línea copia su precio. Si hace falta, se agrega para los dos a la vez.
- **Ocultar «Precios en apps» por módulo:** «Productos» tampoco se oculta. La lista sale vacía para un negocio sin el módulo de productos o de alimentos, aunque tenga productos; con él, lista también los productos apagados, con «No disponible».

## Consecuencias

- **Lo que logra:**
  - Un pedido de plataforma cobra lo que cobró la app, y su comisión, retenciones y neto esperado salen del total real. La liquidación semanal tendrá contra qué cuadrar (resuelto en la Fase 22G, [ADR 0015](0015-delivery-platform-settlements.md)).
  - Subir los precios de una plataforma es una tarea de minutos: calcular con un porcentaje, revisar y guardar.
- **Lo que limita:**
  - **Precios de la app:** deben coincidir con los de la app. TienditApp no los lee de la plataforma: si alguien cambia un precio en la app y no aquí, el pedido sale con el anterior.
  - **Fuera de alcance:** promociones de la plataforma, precios por sucursal, horarios por plataforma y un menú o disponibilidad distintos por plataforma.
  - **Lo escrito sin guardar:** cambiar de pantalla dentro de la app lo pierde sin preguntar. La app usa `BrowserRouter`, que no permite detener una navegación (`useBlocker` pide un router de datos); solo cerrar o recargar la pestaña lo pregunta.
  - **Catálogos grandes:** con más de 500 productos, la pantalla no muestra los demás y avisa que se cobran con el precio del menú.
  - **Pestañas abiertas con la versión anterior:** Vender todavía cobra con el precio del menú, y la venta se rechaza porque el total no es el que calcula el servidor, hasta que la pantalla se recarga. El aviso de versión nueva lo pide. Un negocio sin precios en las apps no nota nada.
- **Reversibilidad:**
  - **Volver a la versión anterior** (master con la Fase 22F) funciona sin `rollback.sql`: no conoce las tablas, y Vender y la venta vuelven juntos al precio del menú.
  - **Con precios guardados,** `rollback.sql` se detiene con el error 50009. Para quitar las tablas hay que exportar y borrar los precios primero, o corregir hacia adelante ([migraciones](../development/database-migrations.md)).

## Siguientes pasos

- **Liquidación semanal y conciliación con el estado de cuenta** de cada plataforma. Hecho: [ADR 0015](0015-delivery-platform-settlements.md).
- **Precios desde la plataforma** por su API o un integrador, cuando lleguen las integraciones.
