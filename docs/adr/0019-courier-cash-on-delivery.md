# ADR 0019: Salida a entregar, cobro contra entrega y liquidación del repartidor

- Estado: Aceptada
- Fecha: 2026-10-13
- Continúa: [ADR 0018](0018-home-delivery-orders.md), punto 7 (Paquetenvia se anota a mano, por folio).

## Contexto

Desde la Fase 22C el pedido sabe a dónde va, cobra su envío y guarda el folio del viaje de Paquetenvia ([ADR 0018](0018-home-delivery-orders.md)). Pero el pedido se quedaba ahí:

- nadie sabía cuándo salió ni si el repartidor cobraba algo;
- el cliente no se enteraba de que su pedido iba en camino;
- el efectivo que Paquetenvia cobra al cliente llegaba días después, menos su viaje y su comisión, y se anotaba en una libreta.

Paquetenvia cobra en efectivo al cliente y lo entrega al negocio: es el caso más común de los negocios sin local. No tiene una API para otros sistemas (pide una sesión de usuario en una organización), así que todo sigue siendo manual.

## Decisión

1. **La salida a entregar es un hecho de la entrega, no un estado nuevo del pedido.** `Order.Dispatch` guarda en `OrderDelivery`:
   - el viaje (repartidor, folio y enlace, con las reglas del ADR 0018);
   - cuándo salió y quién lo mandó (`DispatchedAt`, `DispatchedBy`);
   - si el repartidor cobra en efectivo y cuánto (`CashToCollect`), y con cuánto paga el cliente (`PaysWith`).

   No agrega un estado a la máquina de estados: la cocina, los reportes y los avisos de cambio de estado siguen igual, y volver a la versión anterior no encuentra estados que no conoce.
   - Sale un pedido confirmado (no pendiente) y hasta que se entrega; no uno cancelado.
   - Mientras va en camino se corrige: el folio, el enlace, si cobra y con cuánto paga. La primera vez queda como la hora en que salió.
   - Guardar lo mismo otra vez no registra nada. La bitácora `OrderDispatched` guarda repartidor, folio, si hay enlace (nunca el enlace ni la dirección), lo que cobra, con cuánto paga y si fue corrección.
   - Lo manda quien toma los pedidos (`ManageOrders`): `PUT /orders/{id}/delivery/dispatch`. «Cobra en efectivo» es obligatorio en la petición: callarlo no puede significar que no cobra nada.
2. **El repartidor cobra el saldo del pedido al salir.** Si cobra en efectivo, `CashToCollect` es el saldo en ese momento; si el pedido ya está pagado, no cobra nada y no puede marcarse que cobra. Si no cobra, «paga con» se borra.
   - «Paga con» es el billete con que paga el cliente: pesos y centavos, de $0.01 a $100,000, y cubre lo que cobra el repartidor (si no: «Con $200.00 no alcanza: hay que cobrar $275.00.»). Lo de más es el cambio que lleva el repartidor.
   - También se pregunta al tomar el pedido: «Paga con (opcional)» en Vender y «¿Con cuánto pagas? (opcional)» en el menú en línea. Ahí debe cubrir el total con su envío.
3. **El cliente recibe «En camino» por WhatsApp** la primera vez que el pedido sale, con la plantilla `order_status_update` que ya tienen los avisos de estado (sin plantilla nueva en Meta). Si hay enlace: «En camino, síguelo aquí: https://…». Sigue la política de avisos del negocio, como los demás avisos del pedido. No se manda si el pedido se entregó o canceló antes de que saliera el aviso, ni por una corrección.
4. **Lo que debe el repartidor es el saldo de los pedidos que entregó cobrando** (`Order.AwaitsCourierCash`): un pedido con lo que cobra el repartidor, entregado, con saldo y sin liquidación. Se debe el saldo, no lo que salió a cobrar: si el cliente pagó una parte por transferencia, el repartidor debe el resto. Lo que va en camino se muestra aparte y no se debe todavía; también ahí, el pedido, el ticket y Caja muestran lo que todavía cobra (lo que salió a cobrar o, si se pagó una parte, el saldo), y un pedido que se pagó en camino ya no cuenta.
5. **La liquidación cobra los pedidos en la caja de quien recibe el efectivo** (`CourierSettlement`, inmutable). En Caja › Repartidores:
   - Se eligen los pedidos que entrega (hasta 200, los más antiguos primero), lo que entregó y cómo (efectivo o transferencia), y los ajustes: el viaje, la comisión por cobrar y un faltante se descuentan; «Otro» descuenta o suma y dice qué fue (hasta 50 ajustes).
   - Lo entregado debe ser los pedidos más los ajustes, al centavo; si no, no se guarda y se dice cuánto falta.
   - Cada pedido se paga con su saldo (`OrderPaymentRegistrar`) en la caja propia y abierta de quien recibe: nunca en la de un compañero. Sin caja propia: «Abre tu caja para recibir lo que entrega el repartidor».
   - Cada ajuste entra a esa caja como movimiento con el mismo método: lo que se descuenta como gasto y lo que se suma como depósito. Así el corte de caja cuadra con lo que el repartidor dejó.
   - Un pedido se liquida una vez (índice único por pedido). La pantalla manda también cuánto debían los pedidos que vio (`ordersTotal`): si alguien lo cobró, lo liquidó o pagó una parte mientras tanto, 409 `conflict` y se lee otra vez la lista, en lugar de cobrar otra cantidad; si se guardó un cobro al mismo tiempo: 409 `concurrency_conflict`.
   - Una liquidación no se anula: un error se corrige con un reembolso del pedido o un movimiento de caja, que dejan su propio historial.
   - Liquida quien cobra pedidos y maneja la caja (`ChargeOrders` y `ManageCash`); lo que deben los repartidores y su historial los lee quien maneja la caja (`ManageCash`). La bitácora `CourierSettlementRegistered` guarda montos, pedidos y ajustes, sin direcciones.
   - La pestaña Repartidores aparece cuando un repartidor cobró algo o hay liquidaciones: un negocio que no entrega no la ve.
6. **Pedidos se refresca en vivo** cuando un pedido sale o cambia su viaje: el hub manda `OrderUpdated` con el id del pedido, nada más.

## Consecuencias

- **Lo que logra:**
  - El equipo sabe qué pedidos van en camino, qué cobra el repartidor y cuánto cambio lleva.
  - El cliente se entera de que su pedido salió y puede seguirlo.
  - El efectivo que trae el repartidor entra a caja con cada pedido pagado, y su viaje y su comisión quedan como gastos del corte.
- **Lo que limita:**
  - Paquetenvia se sigue anotando a mano: sin API no sabemos cuándo entregó ni cuánto cobró de verdad. El equipo marca entregado.
  - Hoy solo hay un repartidor (Paquetenvia); otro (uno propio, Lalamove) se agrega al catálogo de repartidores cuando lo pidan.
  - No hay saldo a favor del repartidor: lo que entrega de más se registra como «Otro» que suma en esa liquidación.
  - La liquidación no se anula ni se edita.
- **Decisión por omisión:** se tomó sin una preferencia expresa del dueño del producto, con su autorización para decidir:
  - cobrar los pedidos al liquidar, no cuando el repartidor los entrega al cliente, porque el negocio todavía no tiene el efectivo;
  - descontar el viaje y la comisión como gastos de caja, no restarlos de cada pedido, porque el pedido vale lo que pagó el cliente.
- **Reversibilidad:**
  - La migración `AddCourierSettlements` agrega cuatro columnas que aceptan nulos en `OrderDeliveries` y tres tablas vacías.
  - Volver a la versión anterior de la app no necesita la base: no conoce las columnas ni las tablas. Los pagos de una liquidación son pagos normales de cada pedido. Pero no muestra la salida, lo que debe el repartidor ni sus liquidaciones: es preferible corregir hacia adelante.
  - El `rollback.sql` se detiene con `THROW 50014` mientras haya pedidos que salieron, con «paga con» o liquidaciones: se perdería lo que cobra el repartidor y lo que entregó. Sin ellos, quita lo que agregó, y correrlo otra vez no hace nada.
