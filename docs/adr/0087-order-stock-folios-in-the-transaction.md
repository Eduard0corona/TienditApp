# ADR 0087: Los folios de inventario de una orden dentro de su transacción

- Estado: Aceptada en la Fase 31G (2026-11-10).
- Fecha: 2026-11-10
- Continúa: la Fase 4G (los comandos que crean un documento corren en una transacción y devuelven su folio si se rechazan) y la Fase 14 (cada ronda de una cuenta, también).

## Contexto

- **Lo que mueve una orden también toma folio.** Al confirmar, preparar o entregar una orden se descuentan sus insumos; al cancelarla, reembolsarla o anularla regresan. Cada uno de esos movimientos toma un folio `INV` del día.
- **Esos comandos no corrían en una transacción.** Fuera de una, el folio se confirma al tomarlo: si el comando fallaba después (un faltante bajo la regla «Bloquear», un error de la base, una regla que lo rechaza), el número se perdía y quedaba un hueco en los folios del negocio. Eran el cambio de estado (también el de la cocina y el de las piezas del taller), las cancelaciones (también las del cliente), el reembolso, la anulación y el cierre de una cuenta abierta antes de la Fase 14.
- **Algunos reintentaban su guardado dentro del handler** ante `DbUpdateConcurrencyException` (dos empleados moviendo la misma orden, el cliente cancelando mientras el negocio acepta). Cada intento tomaba un folio nuevo, y el del intento fallido también se perdía. Meterlos en una transacción tal cual no servía: con MARS, que traen las cadenas de conexión de la app, SQL Server no usa savepoints, y un reintento dentro de la misma transacción repetiría lo que el guardado fallido alcanzó a escribir.
- **La deuda técnica lo tenía pendiente** (punto 11 de la lista).

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31G con esa autorización. No lleva migración.

## Decisión

1. **Todos esos comandos corren en una transacción** (`ITransactionalRequest`): `CancelOrderCommand`, `RefundOrderCommand`, `VoidOrderCommand` y `CloseTabCommand`, además de los que reintentan (abajo). Lo que escriben, el folio incluido, se confirma con su respuesta exitosa y se deshace con una fallida o una excepción. `ArchitectureTests` exige ahora la transacción a todo handler que recibe `IOrderInventoryService`, como ya lo hacía con `IFolioGenerator` e `InventoryLedger`.
2. **El reintento ante un conflicto de concurrencia es de la transacción completa** (`IRetryOnConcurrencyConflict`).
   - Lo usan `UpdateOrderStatusCommand`, `MoveKitchenOrderCommand`, `UpdateServiceOrderPieceStatusCommand`, `CancelPublicOrderCommand` y `CancelMyOrderCommand`. Sus handlers ya no tienen bucle.
   - Cuando su guardado encuentra una fila cambiada, `TransactionBehavior` deshace la transacción (folio, movimientos, outbox y eventos incluidos), vacía el `ChangeTracker` y vuelve a correr el handler, hasta tres veces. Es lo mismo que ya hacía ante un fallo transitorio.
   - Si al tercer intento la fila sigue cambiando, responde el mensaje del comando, el mismo de antes («La orden fue actualizada por otro proceso…»), o deja pasar el conflicto (409) en las cancelaciones del cliente, como antes.
   - Dentro de la transacción de otro comando no reintenta: solo quien abrió la transacción puede empezarla de nuevo.
   - **No se reintentan la anulación, la cancelación ni el reembolso**, que tampoco reintentaban: responden 409, como antes, y quien los pidió ve la venta como la dejó la otra persona antes de volver a intentarlo.
3. **Una devolución que no regresa nada no toma folio.** El reembolso calcula qué insumos vuelven antes de pedir el número (como ya lo hacía la cancelación de un platillo de una cuenta): una receta que cambió desde la venta puede no regresar nada.
4. **Lo que se lee antes del consumo, antes.** El cambio a «Lista» lee la política de ventas y las capacidades del negocio antes de tomar el folio, para no tener el contador detenido mientras tanto.
5. **Dos personas que mueven la misma orden a la vez la mueven una vez.** Las dos revisan que su movimiento no exista (la referencia de la orden, de la pieza o de la reversa) y no lo encuentran. Justo antes de tomar el folio, cada una espera el contador `INV` del día (el candado que el folio toma enseguida, en el mismo punto que antes, hasta el final de su transacción) y vuelve a revisar.
   - La segunda espera a que la primera confirme, encuentra su movimiento y no lo registra otra vez; su orden cambió, así que su guardado encuentra el conflicto y, si el comando reintenta, corre de nuevo sobre lo que dejó la primera. Antes respondía 500 por el índice único de la referencia.
   - Con `READ_COMMITTED_SNAPSHOT` (Azure SQL Database) una lectura no espera lo que otra transacción no ha confirmado: sin el candado, la segunda revisión no vería el movimiento de la primera.
   - El candado se toma donde lo tomaba el folio: ninguna solicitud lo espera más tiempo ni en otro orden. Las ventas pasan por el mismo consumo; para ellas la segunda revisión es una consulta más que nunca encuentra nada.
   - No lo cubre todo: con la regla «Bloquear», la segunda puede responder el faltante que dejó la primera si lee las existencias después de que la primera confirma; y alrededor de la medianoche UTC dos que esperan los contadores de días distintos no se ordenan entre sí.

Ningún código HTTP ni mensaje cambia.

## Consecuencias

- **Lo que logra:** un fallo después de tomar el folio lo devuelve, también el primero del día (con la fila de su contador). Un conflicto entre dos personas que mueven la misma orden deja un solo movimiento con un solo folio, también si las dos la confirman a la vez. Lo que avisan las pantallas (SignalR) sale una vez, después del commit.
- **Lo que limita:**
  - **El contador `INV` del día se detiene más tiempo:** desde que uno de estos comandos toma su folio hasta que confirma (milisegundos). Las ventas del mismo negocio lo esperan, como ya esperaban a otras ventas.
  - **Una venta que leyó las existencias antes de esperar el contador** puede responder 409 si uno de estos comandos movió esos insumos mientras tanto (la nota de la deuda técnica sobre transferencias). Ahora son más participantes en esa carrera. Lo mismo con la caja: un reembolso o una anulación en efectivo mueve la caja abierta (`CashSession.Revision`), y una venta en efectivo de esa caja que la leyó antes de esperar el contador responde 409. Esperar el contador antes de leer cerraría las dos; toca las ventas y queda para otra fase.
  - **Si se pierde la respuesta del commit** (un fallo transitorio justo al confirmar), la estrategia de reintentos (`EnableRetryOnFailure`, sin `verifySucceeded`) vuelve a correr el comando completo sin revisar si ya se confirmó, como en los demás comandos con transacción (Fase 4G). Para el reembolso es nuevo: antes, el reintento de su guardado chocaba con la llave primaria de lo que ya había escrito; ahora un reembolso parcial que sí quedó podría registrarse otra vez si el saldo lo permite. Evitarlo pide una llave de idempotencia del reembolso.
  - **Los trabajos del outbox** (WhatsApp, correos, bitácora y el reembolso de Mercado Pago) de estos comandos salen con el siguiente sondeo del despachador (hasta 5 s), porque dentro de una transacción el despachador no se despierta al guardar. Despertarlo al confirmar queda pendiente.
  - Los huecos que ya quedaron antes de esta fase siguen ahí.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - reintentar la transacción completa en lugar de responder 409 y que reintente la pantalla, para no cambiar lo que ve el cajero;
  - no desactivar MARS: no resuelve el hueco por sí solo y haría depender la corrección de una configuración;
  - no reintentar la anulación.

## Fuera de esta fase

- Esperar el contador antes de leer las existencias y la caja, en las ventas y en el consumo de una orden.
- Despertar al despachador del outbox cuando confirma la transacción.
- Una llave de idempotencia del reembolso (el commit perdido, arriba).
