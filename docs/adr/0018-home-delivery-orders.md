# ADR 0018: Pedido a domicilio con el envío cobrado y Paquetenvia por folio

- Estado: Aceptada
- Fecha: 2026-10-13
- Enmienda: [ADR 0017](0017-delivery-zones.md), punto 3 (el envío sigue la política de IVA del pedido).

## Contexto

Desde la Fase 22B el negocio sabe a qué códigos postales entrega, cuánto cobra y desde qué monto toma un pedido ([ADR 0017](0017-delivery-zones.md)), y las respuestas automáticas lo contestan. Pero el pedido no lo sabía: el menú en línea no ofrecía «A domicilio», Vender no guardaba a dónde iba un pedido tomado por WhatsApp o por teléfono, el envío se cobraba aparte (o se olvidaba) y el ticket no decía la dirección. Los negocios piden el viaje a un repartidor externo; el que más usan, además de Rappi, Uber Eats y DiDi, es Paquetenvia, que no es un canal de venta sino un servicio de mensajería ([ADR 0013](0013-delivery-platform-orders.md)): hoy el negocio pide el viaje en su app y anota el folio en un papel.

## Decisión

1. **El pedido sabe a dónde va** (`OrderDelivery`, 1:1 con el pedido, `ITenantScoped`, como el detalle de un pedido de plataforma). Guarda:
   - el código postal (cinco dígitos), la dirección (hasta 300 caracteres) y las referencias (hasta 500, opcionales);
   - el nombre de la zona que llegó, copiado al venderse;
   - el envío cobrado (0 si fue gratis) y la tasa de IVA con que se cobró;
   - el viaje del repartidor, cuando el negocio lo anota: repartidor (hoy solo Paquetenvia), folio, enlace para seguirlo, cuándo y quién.

   Se carga siempre con el pedido (`AutoInclude`): su envío es parte del total, y un cambio del pedido sin él lo recalcularía sin el envío.
2. **Solo un pedido nuevo de productos va a domicilio.** `Order.ArrangeDelivery` se llama al crear el pedido, después de sus productos y antes de cualquier pago o estado: pendiente, de productos, sin plataforma (su repartidor lo lleva) ni cuenta de mesa. Lo usan igual Vender (`CreateOrderCommand.Delivery`) y el menú en línea (`PlacePublicOrderCommand.Delivery`), con `HomeDeliveryPlacement`:
   - la zona activa del negocio que llega al código postal; si ninguna llega: «Por ahora no llegamos al 72999.»;
   - el pedido mínimo, comparado con lo que suman los productos como los paga el cliente (con IVA); si no lo alcanza: «El pedido mínimo para el 72000 es de $150.00.»;
   - el envío de la zona, o $0 desde su «envío gratis desde».

   Después, el pedido ya no toma productos ni se vuelve cuenta. Un pedido a domicilio pagado no se cierra solo: listo en cocina no es entregado; alguien lo marca entregado. No se registra sin conexión: el envío sale de las zonas.

   Un pedido a domicilio tomado en Vender no es una venta de mostrador para los avisos por WhatsApp (`OrderNotificationRules`): el cliente lo espera en casa, así que recibe los avisos de su pedido como los de un pedido del menú en línea.
3. **El envío es un concepto más del pedido, con la política de IVA del pedido.** Entra al desglose como un monto con la tasa por omisión del negocio (`TenantSalesSettings.DefaultTaxRate`), igual que un precio: con IVA incluido si los precios del negocio lo incluyen (lo de omisión desde la Fase 1), y con el IVA sumado si no. Así el negocio escribe el envío como escribe sus precios. **Enmienda el punto 3 del ADR 0017**, que decía que el envío era siempre con IVA incluido: lo sigue siendo para casi todos los negocios, y Configuración › Zonas de entrega dice cuál aplica. Con precios sin IVA, donde se dice el envío se dice «más IVA» (las respuestas automáticas, la cotización del menú en línea y Vender), y el carrito muestra el envío con su IVA para que sus renglones sumen el total. Antes del merge, la consulta 6 de `verify.sql` lista los negocios con zonas activas y precios sin IVA: son los que verán el cambio. El mínimo y el envío gratis se siguen comparando con lo que paga el cliente en productos. El envío queda fijo en el pedido: es lo que el cliente aceptó, aunque la zona cambie después.
4. **El borrador de CFDI lleva el envío como concepto propio:** «Envío a domicilio», clave 78102203 (servicios de envío) y unidad E48 (unidad de servicio), objeto de impuesto 02. Un aviso pide confirmarlo con el contador. Las sumas del CFDI cuadran con el total del pedido como con los demás conceptos.
5. **El menú en línea ofrece «A domicilio» cuando el negocio toma pedidos y tiene al menos una zona activa** (`PublicMenuDto.DeliveryAvailable`):
   - el cliente elige «A domicilio» o «Paso por él»; una cocina sin local con zonas solo ofrece domicilio, y el servidor exige la dirección (sin zonas, sus pedidos llegan como antes y el negocio arregla la entrega);
   - en una mesa (`?mesa=`) no se ofrece: el cliente ya está ahí, y una mesa con dirección se rechaza. Cualquiera puede escribir una mesa en el enlace, así que solo una mesa activa del negocio libra a una cocina sin local de exigir la dirección (`PublicMenuTables.IsSeatedAsync`, la misma regla que ya decidía si el cliente está sentado);
   - el código postal se cotiza con `GET /public/menu/{slug}/delivery-quote` (anónimo, con el límite del menú): si llega, cuánto cobra, el mínimo y desde cuánto es gratis, **nunca el nombre de la zona**, que puede ser interno. Contesta también con los pedidos pausados: el cliente puede ver si llegan mientras mira el menú;
   - el total del carrito suma el envío y se manda como `expectedTotal`: si el servidor calcula otro, rechaza el pedido como cuando cambia un precio.
6. **Vender ofrece «A domicilio» con las zonas activas del negocio** (que puede leer todo el equipo), no en cuentas de mesa, pedidos de plataforma ni servicios. Con el código postal muestra la zona, su envío y su mínimo; el envío entra al total y a la clave de la venta (otra dirección es otra venta). Sin mesa: la dirección dice a dónde va.
7. **Paquetenvia se anota a mano, por folio.** Quien toma los pedidos (`ManageOrders`) escribe o corrige en Pedidos el folio del viaje y, si lo hay, su enlace (`PUT /orders/{id}/delivery/shipment`):
   - el folio con las reglas del folio de plataformas (sin `#` ni espacios, en mayúsculas);
   - el enlace escrito con `https://` al principio (no `https:/` ni con barras invertidas, que el navegador entendería distinto), hasta 500 caracteres y sin usuario ni contraseña; el cliente solo lo ofrece si empieza con `https://`;
   - no en un pedido cancelado ni en uno que no va a domicilio;
   - el pedido cambia con su viaje, así que su versión detecta una cancelación guardada al mismo tiempo (409);
   - anotar el mismo viaje otra vez no registra nada.

   No hay integración con la API de Paquetenvia: el negocio pide el viaje en su app, como hoy.
8. **La dirección no sale de donde no hace falta.**
   - La bitácora del pedido creado dice que va a domicilio y cuánto cobró de envío, nunca la dirección; la del viaje guarda repartidor y folio, antes y después, y si hay enlace, nunca el enlace.
   - La cocina ve «A domicilio», sin la dirección.
   - Un código postal mal escrito se rechaza sin repetirlo en el error, que puede llegar a la bitácora de la API.
   - El ticket sí la lleva, con las referencias y el folio del viaje: va en la bolsa para el repartidor.

## Consecuencias

- **Lo que logra:**
  - El cliente pide a domicilio desde el menú y sabe antes de enviar cuánto cuesta el envío y si llegan.
  - El envío se cobra en el pedido, con su IVA, y entra a caja, reportes y factura como el resto.
  - El equipo ve a dónde va cada pedido y el folio de su viaje, y el ticket lleva todo lo que necesita el repartidor.
- **Lo que limita:**
  - La dirección no se valida contra un mapa: el código postal decide la zona y la dirección es lo que escribe el cliente.
  - No hay tiempo estimado de entrega, horario por zona, ni seguimiento del repartidor dentro de TienditApp.
  - Paquetenvia se anota a mano; otros repartidores (un repartidor propio, Lalamove) quedan para cuando los pidan.
  - El cliente del menú no guarda sus direcciones para la próxima vez.
  - Anotar el viaje no avisa en tiempo real: otra pantalla de Pedidos lo ve al refrescarse (como hoy otras correcciones del pedido).
  - La clave del SAT del envío es la más común para cargos de envío; cada negocio debe confirmarla con su contador.
- **Decisión por omisión:** se tomó sin una preferencia expresa del dueño del producto, con su autorización para decidir. Si se prefiere que el envío sea siempre con IVA incluido aunque los precios no lo incluyan, el cambio está en `Order.Breakdown` y en el cliente (`utils/homeDelivery.ts`); los pedidos ya guardados conservan su envío y su tasa.
- **Reversibilidad:**
  - La migración `AddOrderDeliveries` agrega una tabla vacía. Volver a la versión anterior de la app no necesita la base: no conoce la tabla y cobra cada pedido a domicilio por el total con que se guardó, envío incluido. Pero no muestra la dirección ni el envío en Pedidos ni en el ticket, y su menú y Vender dejan de ofrecer domicilio: es preferible corregir hacia adelante.
  - El `rollback.sql` se detiene con `THROW 50013` mientras haya pedidos a domicilio: se perderían su dirección y su envío. Sin ellos, quita la tabla, y correrlo otra vez no hace nada.
