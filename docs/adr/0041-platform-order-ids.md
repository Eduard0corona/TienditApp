# ADR 0041: El ID largo del pedido de cada plataforma

- Estado: Aceptada
- Fecha: 2026-10-22

## Contexto

Un pedido de Rappi, Uber Eats o DiDi Food se registra en Vender con el folio corto que muestra la app de la plataforma ([ADR 0013](0013-delivery-platform-orders.md)). Ese folio se repite: Uber Eats usa 5 caracteres y DiDi un número de recolección del día. Por eso, un folio repetido es un aviso confirmable y no un índice único.

Los estados de cuenta de las plataformas no siempre traen ese folio. Muchos nombran el pedido por su ID largo:

- en Uber Eats, un UUID;
- en DiDi, un número de unos 19 dígitos;
- en Rappi, el número de orden completo.

La importación del estado de cuenta ([ADR 0040](0040-platform-statement-import.md)) compara por folio. Con un archivo que solo trae el ID largo, no encuentra los pedidos, y el ADR 0040 lo dejó como límite. El roadmap de Plataformas de reparto pide «el ID largo del pedido», y el ADR 0015 lo tenía entre sus siguientes pasos.

Tomé las decisiones de la Fase 22I con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Un valor del dominio, opcional.**
   - `PlatformOrderId` (Domain.Orders.Platforms) es el ID largo, normalizado como el folio: sin espacios alrededor ni dentro, sin un `#` inicial y en mayúsculas.
   - Admite letras de la A a la Z, dígitos y guiones, de 6 a 64 caracteres, y lee hasta 100 antes de normalizar.
   - El mínimo de 6 rechaza los 5 caracteres de Uber Eats escritos en el campo equivocado.
   - `PlatformOrderDetail.ExternalOrderId` lo guarda (nvarchar(64) NULL). Ningún importe depende de él: el detalle sigue sin cambiar salvo este dato de referencia.
2. **Se captura en Vender o después.**
   - `CreateOrderCommand.ExternalOrderId` es opcional y solo va en pedidos de plataforma; en otro pedido es un 400.
   - Vender muestra «ID largo del pedido (opcional)» junto al folio, con dónde encontrarlo en cada app. Lo guarda en el borrador del dispositivo y avisa si lo escrito no puede ser uno.
   - `PUT /api/v1/orders/{id}/platform-order-id` (`SetPlatformOrderIdCommand`) lo escribe, corrige o quita (vacío) en cualquier estado del pedido, porque los estados de cuenta llegan después de entregarlo. Lo usa quien toma los pedidos (`ManageOrders`), como al capturar.
   - Un pedido que no es de plataforma responde 400. El de otro negocio, 404.
   - En Pedidos, el detalle muestra el ID largo con «Agregar» o «Corregir».
3. **Un ID largo repetido es un aviso, como el folio.**
   - Al capturar o corregir, otro pedido de la misma plataforma del negocio con el mismo ID largo que todavía cuenta (ni cancelado ni devuelto completo) responde 409 `possible_duplicate_order`. Cuenta de cualquier fecha, porque los IDs largos no se repiten.
   - La persona confirma y se manda otra vez, con la misma llave de idempotencia al capturar.
   - No es un índice único: un pedido capturado dos veces se corrige cancelando uno, como con el folio. El índice `(TenantId, Channel, ExternalOrderId)` es no único y filtrado a los que lo tienen.
   - El mismo ID en otro negocio no avisa.
4. **Queda en la bitácora.** `Order.ChangePlatformOrderId` levanta `OrderPlatformIdChangedEvent` con el anterior y el nuevo. La auditoría `OrderPlatformIdChanged` lo escribe por el outbox. Escribir el mismo ID otra vez no cambia nada ni deja entrada. `OrderCreated` incluye el ID largo con el que se capturó.
5. **Se busca y se ve.**
   - Pedidos y Reportes › pedidos de plataformas encuentran el pedido por una parte del ID largo. El término se normaliza como se guarda, así que no depende de la intercalación de la base.
   - El ID se ve en el detalle del pedido, en la lista de Pedidos y de Reportes, y bajo el folio en la tabla de la liquidación, cuya búsqueda también lo usa.
   - La cocina y el ticket siguen con el folio corto: es el que dice el repartidor.
6. **El estado de cuenta compara por folio o por ID largo.**
   - Cada renglón se normaliza como folio, o como ID largo cuando es demasiado largo para folio, y se buscan los pedidos cuyo folio o ID largo coincide.
   - Los renglones que nombran el mismo pedido por su folio y por su ID largo se suman en uno, bajo el primero: el pedido no cuenta dos veces.
   - De un ID largo que la liquidación no puede tomar se leen sus pedidos completos, que son uno o los pocos capturados dos veces. El historial de un folio repetido sigue sin leerse completo.
   - El renglón admite hasta 100 caracteres, y la columna se llama «Folio o ID del pedido».
7. **Migración `AddPlatformOrderIds`, compatible con la versión anterior.**
   - La migración agrega la columna (NULL), `CK_PlatformOrderDetails_ExternalOrderId` (vacío, o de 6 caracteres o más) y el índice filtrado.
   - La versión anterior no conoce la columna: sus pedidos nuevos la dejan vacía y sus cambios no la tocan.
   - `rollback.sql` se detiene con 50032 si ya hay IDs largos guardados, porque borrarlos perdería lo que el negocio escribió. Desde ese momento solo se avanza.
   - `apply.sql` va a producción antes del merge.
8. **Fuera de esta fase:**
   - leer el ID largo de las APIs de las plataformas;
   - llenar los IDs largos desde un estado de cuenta;
   - el CFDI de retenciones;
   - mostrar el ID largo en la cocina o el ticket.

## Consecuencias

- **Lo que logra:**
  - Un estado de cuenta que nombra los pedidos por su ID largo encuentra los pedidos. Uno que los nombra por folio funciona igual que antes.
  - Un pedido capturado dos veces se detecta por su ID largo aunque pasen días.
  - La persona encuentra en Pedidos el pedido de un renglón del estado de cuenta pegando su ID.
- **Lo que limita:**
  - El ID largo es opcional. Los pedidos sin él siguen encontrándose solo por folio, y nada lo llena solo.
  - Un ID escrito distinto (con otro carácter, o sin un guion) es otro ID: se corrige en el detalle.
  - Un mismo texto puede ser folio de un pedido e ID largo de otro. Entonces el renglón tiene varios pedidos y la persona elige, como con un folio repetido.
- **Compatibilidad durante el despliegue:** la versión anterior (22H) funciona sobre la base migrada, y `/health/schema` sigue sano. Los IDs largos que se escriban mientras tanto se quedan en la base, y vuelven a verse con esta versión.
- **Reversibilidad:** volver a la versión anterior no necesita `rollback.sql`. Antes de que haya IDs largos, `rollback.sql` quita la columna, la restricción y el índice. Después, solo se avanza.
