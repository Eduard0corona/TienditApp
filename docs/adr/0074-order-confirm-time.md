# ADR 0074: Cuánto tarda en confirmar los pedidos en línea

- Estado: Aceptada en la Fase 29T (2026-11-07).
- Fecha: 2026-11-07
- Continúa: [ADR 0064](0064-response-time-in-explore.md), [ADR 0065](0065-response-time-outside-explore.md) y [ADR 0073](0073-quote-time-and-explore-order.md), «Fuera de esta fase»: los pedidos en línea de los restaurantes.

## Contexto

Desde la Fase 29J, Explorar dice en cuánto suele responder un negocio sus solicitudes en línea: citas y visitas ([ADR 0064](0064-response-time-in-explore.md)). Desde la 29S dice también en cuánto suele mandar un oficio la cotización ([ADR 0073](0073-quote-time-and-explore-order.md)). De los restaurantes, las tiendas y los demás negocios que toman pedidos en su menú en línea no dice nada. Su cliente espera a que el negocio confirme el pedido, y esa espera es de minutos, no de horas.

El pedido ya guarda en su historial cada cambio de estado con su hora (`OrderStatusHistory`), también el primero: cuando el negocio lo acepta o lo rechaza, o cuando su cliente lo cancela (Fase 29R, [ADR 0072](0072-quote-answers-live-and-customer-order-cancellation.md)). El historial tiene índice por pedido y hora. No hace falta guardar nada nuevo.

El dueño del producto pidió avanzar este punto (el 8), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29T con esa autorización.

## Decisión

1. **Qué cuenta:**
   - Los pedidos que sus clientes hicieron en su menú en línea en los últimos 90 días. No cuentan los que se pidieron con el QR de una mesa, porque los atiende quien la sirve, ni los que tomó el equipo (en mostrador, de WhatsApp o de una plataforma).
   - **La espera** va desde que llegó el pedido hasta que el negocio lo sacó de «Por confirmar» por primera vez: lo aceptó o lo rechazó. Un negocio que acepta los pedidos solo los responde al llegar.
   - **Los más lentos** son los que el negocio respondió a la media hora o después, los que llevan media hora sin respuesta y los que su cliente canceló después de esperar media hora sin respuesta.
   - **No cuentan todavía** los que esperan desde hace menos de media hora. Nunca cuentan los que su cliente canceló antes.
   - **Cada cliente cuenta una vez**, por su último pedido, como las solicitudes ([ADR 0067](0067-requests-that-count.md)). El teléfono y quién respondió solo se comparan en la base, nunca se leen.
2. **Qué dice:** desde 3 pedidos, el tramo más corto que reúne al menos la mitad. «Menos de» es estricto:
   - «Suele confirmar los pedidos en menos de 5 minutos»;
   - «… de 15 minutos»;
   - «… de media hora».
   - Con media hora o más, o con menos de 3 pedidos, no dice nada.
3. **Dónde:**
   - En la tarjeta de Explorar de un negocio que toma pedidos en línea, aunque estén en pausa (cerrado o sin caja abierta). La tarjeta de un oficio con sus pedidos en pausa habla de sus visitas, no de pedidos, como ya lo hacía ([ADR 0053](0053-home-visit-quotes.md)).
   - En su menú, salvo en una mesa.
   - Para su dueño, en «Cómo te ven en TienditApp». Ahí también lee si tarda media hora o más, o que todavía no se cuenta.
4. **De dónde:** de la foto de Explorar, como el tiempo de respuesta.
   - Una sola lectura entre negocios, filtrada por los negocios listados, une cada pedido con su historial dentro del mismo negocio.
   - Si no se puede leer, nadie lo dice hasta la siguiente foto, y al dueño no se le dice que todavía no se cuenta.
   - Cada tiempo depende de su propia lectura. Si la foto no puede leer las solicitudes, el menú y el dueño siguen diciendo el de los pedidos, y al revés. Antes, una lectura fallida de las solicitudes callaba todo lo que la foto dice fuera de Explorar.
   - La regla de la mitad es la misma para responder, cotizar y confirmar (`TypicalWait`).
5. **Fuera de esta fase:**
   - Ordenar Explorar por el tiempo de confirmar. Un restaurante sigue en medio de su grupo, como antes ([ADR 0073](0073-quote-time-and-explore-order.md)). Llegó en la Fase 29W ([ADR 0077](0077-explore-order-with-every-time.md)).
   - Avisarle al negocio cuando un pedido lleva tiempo sin confirmar: llegó en la Fase 29X ([ADR 0078](0078-online-order-waiting-notice.md)).
   - Medir cuánto tarda en preparar o entregar el pedido. Lo de tenerlo listo llegó en la Fase 29Y ([ADR 0079](0079-order-ready-time.md)); la entrega sigue fuera.

## Consecuencias

- **Lo que logra:**
  - El cliente sabe, antes de pedir, si el negocio suele confirmar pronto.
  - El negocio tiene una razón para confirmar sus pedidos en línea, o para aceptarlos solo.
  - Aparece desde el despliegue: el historial ya guarda los pedidos de los últimos 90 días.
- **Lo que limita:**
  - **Aceptar solo cuenta como inmediato:** un negocio que acepta los pedidos solo dice «menos de 5 minutos». Es lo que vive su cliente: el pedido entra a preparación al llegar.
  - **Se puede inflar** con pedidos falsos desde varios teléfonos, como el tiempo de respuesta ([ADR 0064](0064-response-time-in-explore.md)). Es una señal para el cliente, no una garantía.
  - **Costo de la foto:** cada foto (cada 30 segundos, si alguien la pide) lee los pedidos de 90 días de los negocios listados.
    - Los busca por negocio y fecha (`IX_Orders_TenantId_CreatedAt`) y revisa en cada uno el canal y la mesa, que no están en ese índice: también recorre las ventas de mostrador.
    - Busca el primer cambio de cada pedido en el historial, por pedido y hora, y el último pedido de cada cliente una sola vez, agrupado. *Desde la Fase 30E une el historial una vez y, por cliente, dice cuándo llegó el último pedido de cada tramo ([ADR 0082](0082-order-cancellation-email-reminder-lead-and-online-orders-index.md), punto 3).*
    - Medido en SQL Server, con una sola cocina: unos 20 ms sin pedidos, unos 250 ms con 25,000 pedidos (2,500 en línea) y unos 600 ms con 100,000 (10,000 en línea). Crece con los pedidos de todos los negocios listados.
    - Con varios negocios de ese tamaño conviene un índice filtrado de los pedidos en línea (por negocio, teléfono y fecha) o guardar en el pedido cuándo se confirmó. Los dos piden migración.
  - **El menú puede armar la foto:** el menú de un negocio que toma pedidos lee ahora la foto de Explorar, como las páginas de citas y de visitas (Fase 29K, [ADR 0065](0065-response-time-outside-explore.md)). Si la foto tiene más de 10 minutos, la petición que la encuentra la arma de nuevo; las demás no esperan.
  - **Contrato:** la tarjeta de Explorar, el menú y «Cómo te ven» suman `orderConfirmTime`, y «Cómo te ven», `countsOrders`. Los clientes anteriores los ignoran.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - qué cuenta (sin las mesas y con la aceptación automática);
  - los tramos (5, 15 y 30 minutos);
  - que no cambie el orden;
  - los textos.
- **Reversibilidad:** sin migración. Volver al código anterior quita el tiempo de confirmar.
