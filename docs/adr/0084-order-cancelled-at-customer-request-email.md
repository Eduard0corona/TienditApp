# ADR 0084: El correo del pedido que el negocio cancela porque su cliente lo pidió

- Estado: Aceptada en la Fase 31B (2026-11-10).
- Fecha: 2026-11-10
- Continúa: [ADR 0082](0082-order-cancellation-email-reminder-lead-and-online-orders-index.md), «Fuera de esta fase»: el correo de un pedido que el cliente cancela por teléfono o por WhatsApp (lo cancela el negocio).

## Contexto

- **El cliente cancela solo mientras el negocio no acepta.** Desde la Fase 29R el cliente cancela su pedido en línea desde su enlace o su cuenta, pero solo mientras sigue por confirmar, sin pagos y sin mesa ([ADR 0072](0072-quote-answers-live-and-customer-order-cancellation.md)). Ya aceptado, el enlace le dice «para cancelarlo, llámale»: lo cancela alguien del equipo en Pedidos.
- **Ese es el pedido que más importa avisar.** Desde la Fase 30B, cuando el cliente cancela, le llega un correo a quien recibe «Pedidos cancelados» ([ADR 0082](0082-order-cancellation-email-reminder-lead-and-online-orders-index.md)). Pero si llama cuando ya lo aceptaron, quizá alguien ya lo está preparando, y nadie más del equipo se entera hasta abrir Pedidos.
- **Nada decía que lo pidió el cliente.** Cancelar en Pedidos solo pide un motivo libre («Ej. el cliente ya no lo quiso»). El evento de la cancelación distinguía al cliente que cancela desde su enlace (`ByCustomer`), no al negocio que cancela a su nombre.

El dueño del producto pidió avanzar este punto (el 17) sin esperar su respuesta a cada decisión. Tomé las de la Fase 31B con esa autorización. No lleva migración.

## Decisión

1. **Quien cancela dice si lo pidió el cliente.**
   - En Pedidos, al cancelar un pedido del menú en línea, en cualquier estado, después del motivo se pregunta «¿Lo pidió el cliente?»: «Sí, lo pidió» o «No». Cerrar la pregunta es «No».
   - No se pregunta en los demás canales (los del mostrador o de WhatsApp los toma el negocio, y los de las plataformas los cancela su app) ni en los pedidos con el QR de una mesa, que atiende quien sirve la mesa.
   - La API lo recibe en `PUT /orders/{id}/cancel` como `atCustomerRequest` (falso si no viene), y el evento de la cancelación lo lleva (`OrderCancelledEvent.AtCustomerRequest`). Un evento guardado antes se lee como «no lo pidió».
   - Queda en la actividad del negocio, con el motivo.
2. **El correo.**
   - Les llega a quienes reciben «Pedidos cancelados»: la misma preferencia, las mismas reglas (pueden confirmar pedidos, están activos y con el correo confirmado; los dueños mientras no eligen).
   - **No le llega a quien lo canceló**: ya lo sabe. Tampoco, desde ahora, a quien cancela su propio pedido como cliente.
   - Dice «Un cliente pidió cancelar su pedido»: quién, el folio, el total, que el equipo lo canceló, el motivo tal como lo escribió (en una línea), si escribió uno, y que ya no hay que prepararlo ni entregarlo. Nunca quién del equipo lo canceló, el teléfono ni la dirección del cliente, ni lo que pidió; el motivo dice lo que el equipo haya escrito.
   - Solo de los pedidos del menú en línea, pero de cualquiera: también uno ya aceptado, listo o en camino, que el cliente ya no podía cancelar desde su enlace (el de la 30B solo es de los que siguen por confirmar). Si la API lo recibe para un pedido de una mesa, también sale.
   - Al mandarlo se comprueba otra vez que siga cancelado, hace un día o menos, y que la persona lo siga recibiendo.
   - Los textos de la preferencia en Mi perfil y en Equipo, el pie del correo y el correo de la 30A cuando alguien más la cambia lo dicen.
3. **Sin migración.** El dato viaja en el evento del outbox, que se guarda como JSON, y en la actividad del negocio. El pedido no lo guarda.

## Consecuencias

- **Lo que logra:** quien está en la cocina o en el taller se entera de que ya no tiene que preparar un pedido que el cliente canceló por teléfono, aunque ya lo hubieran aceptado.
- **Lo que limita:**
  - **Lo afirma quien cancela, sin prueba.** Un «Sí» por error solo manda un correo de más; un «No» por error, uno de menos.
  - **El pedido no lo guarda:** ni su detalle ni el enlace del cliente dicen que lo pidió. Mostrarlo pide una columna (migración) o forzar el sentido de las notas del historial.
  - Cancelar cambiando el estado a «Cancelado» o anulando con reembolso no pregunta: solo «Cancelar pedido».
  - Quien ya recibe «Pedidos cancelados» recibe más correos. Quien no los quiere los apaga en Mi perfil.
  - Mientras se publica, el worker de la versión anterior ignora el dato nuevo y ese correo no sale.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - usar la preferencia de «Pedidos cancelados» en vez de una nueva, que pediría migración;
  - preguntarlo después del motivo, y solo en los pedidos en línea;
  - no decir en el correo quién del equipo lo canceló.
