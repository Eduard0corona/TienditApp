# ADR 0023: Seguimiento del pedido

- Estado: Aceptada
- Fecha: 2026-10-17

## Contexto

Con la Fase 24B ([ADR 0022](0022-shopping-with-an-account.md)) un cliente compra con su cuenta o como invitado, y el pedido llega a Pedidos del negocio. Pero después de enviarlo el cliente no sabía cómo iba:

- Mis pedidos mostraba el estado del negocio («Confirmada», «Lista»), no lo que el cliente espera leer, y no decía cuándo pasó cada cosa;
- «En camino» (Fase 22D) solo llegaba por WhatsApp, y no es un estado del pedido;
- un invitado no tenía nada después del recibo: si cerraba la página, solo le quedaba escribirle al negocio;
- Mis pedidos mezclaba los pedidos de hoy con los de hace meses y no se actualizaba solo.

La Fase 24C, «Seguimiento», del informe «TienditApp para clientes», resuelve eso.

## Decisión

1. **Pasos del cliente, no estados del negocio** (`OrderTracking` en Application).
   - Cada tipo de pedido tiene sus pasos, en el orden en que lo mueve su máquina de estados:
     - una cocina (`FoodOrderStateMachine`): Recibido, Confirmado, En preparación, Listo, [En camino], Entregado;
     - una tienda (`RetailOrderStateMachine`): Recibido, Confirmado, [En camino], Entregado;
     - un servicio (`ServiceOrderStateMachine`): Recibido, Aceptado, En proceso, Terminado.
   - «En camino» solo es paso de un pedido a domicilio (`OrderDelivery`). Su hora es la salida con el repartidor (`DispatchedAt`), y es el paso actual mientras el pedido salió y no se entregó ni se canceló.
   - Varios estados son un paso:
     - Confirmed y Accepted son Confirmado (o Aceptado);
     - InPreparation e InProgress son En preparación (o En proceso);
     - Delivered y Completed son Entregado (o Terminado).
   - La hora de cada paso es la primera vez que el pedido llegó a él según su historial (`OrderStatusHistory`). Recibido es la hora del pedido.
   - Un paso que el negocio se saltó (por ejemplo, de Pendiente a Entregado) queda hecho, sin hora.
   - Un pedido entregado o terminado tiene todos sus pasos hechos: ya no falta nada.
   - Un pedido cancelado muestra los pasos hasta donde llegó (el estado anterior a cancelarse, o En camino si ya había salido) y después «Cancelado» con su hora.
   - Los textos los da el servidor (`OrderTrackingStepDto.Label`), y el paso actual también (`Step`, `StepLabel`): el cliente los muestra tal cual.
2. **Detalle del pedido de la cuenta** (`GET /api/v1/me/orders/{id}`, `GetMyOrderQuery`, `[RequiresAuthentication]`).
   - Solo un pedido de la cuenta de la sesión (`CustomerUserId`). Cualquier otro, de otra cuenta, de un invitado o inexistente, responde 404 igual.
   - Lee los pedidos de la cuenta en todos los negocios, como su lista: el filtro del negocio se cambia por el de la cuenta (un `IgnoreQueryFilters` revisado más en `TenantFilterBypassAllowlistTests`).
   - Lleva los pasos, lo que se pidió con sus opciones y notas, el envío o el cargo por la visita a domicilio (Fase 22E), el total, lo pagado, el motivo si se canceló (el mismo que el cliente recibió por WhatsApp), la dirección de entrega, el enlace de la paquetería y el teléfono del negocio.
3. **Enlace de seguimiento para invitados.**
   - Cada pedido del menú en línea recibe al crearse un enlace imposible de adivinar (`Order.TrackingToken`): 32 bytes aleatorios en Base64Url, 43 caracteres.
     - Se emite una sola vez y solo con la forma de un enlace (`Order.IssueTrackingToken`).
     - La columna lleva una CHECK de longitud y un índice único filtrado que no es por negocio: un enlace abre un solo pedido de toda la plataforma.
   - El recibo lo devuelve (`PublicOrderReceiptDto.TrackingToken`), también al reenviarse el pedido con su llave. La tienda ofrece «Seguir tu pedido» y, a un invitado, «Copiar enlace de seguimiento»: la página no lo guarda, así que el invitado lo conserva si lo abre, lo copia o lo guarda.
   - **El enlace nunca va en la URL que llega al servidor.** La telemetría de las peticiones (Azure Monitor) guarda rutas y consultas, así que:
     - la app lo abre como `/seguimiento#enlace`, y el fragmento no sale del navegador;
     - la página lo manda en el cuerpo de `POST /api/v1/public/orders/tracking` (`GetPublicOrderTrackingQuery`, anónimo, límite `public-menu`).
   - El servidor busca el enlace antes de tener un negocio en contexto: solo lee su negocio y su fecha (un `IgnoreQueryFilters` revisado más), entra a ese negocio y lee el pedido con su filtro.
     - La columna compara sin mayúsculas, como la base. El servidor exige además el enlace exacto.
   - **Vale 30 días desde el pedido** (`OrderTracking.Days`), solo mientras el negocio está activo.
     - Un enlace desconocido, cambiado, vencido o de un negocio suspendido responde el mismo 404: «No encontramos ese pedido. El enlace pudo haber vencido.».
   - **No lleva nada del cliente:** ni su nombre, ni su teléfono, ni su dirección, ni las notas que escribió en los productos. Dice si va a domicilio, sin decir a dónde. Sí lleva el enlace de la paquetería que registró el negocio, y la página de la paquetería puede mostrar el destino.
   - Los pedidos anteriores a la Fase 24C, y los que no vienen del menú en línea, no tienen enlace. La migración solo agrega la columna.
4. **Mis pedidos en curso y anteriores** (`GetMyOrdersQuery.Scope`: `InProgress`, `Past` o `All`, que es el valor por defecto y la lista de antes).
   - **En curso:** pedidos abiertos (no entregados, terminados ni cancelados) de los últimos 30 días. Un pedido que el negocio dejó abierto más tiempo pasa a Anteriores.
   - Cada pedido dice su paso actual (`MyOrderDto.Step` y `StepLabel`).
5. **Se actualiza sola.**
   - El detalle, el seguimiento y la sección En curso se piden otra vez cada 20 segundos (`TRACKING_REFRESH_MS`), mientras el pedido está en curso (`InProgress`).
   - Un 404 (el enlace venció o el pedido ya no es de la cuenta) deja de preguntar: la página dice el mensaje y conserva lo último que vio. Sin conexión sigue preguntando.
   - Cuando un pedido sale de En curso, Anteriores se vuelve a pedir.
   - No hay SignalR para el cliente: los hubs son del equipo de cada negocio.

## Consecuencias

- **Lo que logra:**
  - El cliente ve, con o sin cuenta, en qué paso va su pedido y a qué hora pasó cada uno, «En camino» incluido.
  - Un invitado sigue su pedido con un enlace que solo él tiene.
  - Mis pedidos separa lo que está en curso de lo anterior.
- **Lo que limita:**
  - Hasta 20 segundos de retraso. El cliente no recibe avisos en el navegador; los de WhatsApp siguen igual.
  - Quien tenga el enlace ve el pedido durante 30 días: el enlace es el secreto. Se guarda tal cual (no como hash), y no se puede revocar ni volver a emitir.
  - Un invitado que cierra el recibo sin abrir, copiar ni guardar el enlace lo pierde: no llega por WhatsApp.
  - El motivo de una cancelación lo escribe el negocio, y el enlace lo muestra como lo mostró WhatsApp.
  - Las horas de los pasos salen del historial: se guarda la hora de cada cambio, no por qué el negocio se saltó un paso.
  - Los pedidos de antes de la 24C, los del mostrador y los de WhatsApp no tienen enlace. Los de una cuenta se siguen en Mis pedidos.
- **Reversibilidad:**
  - Volver a la versión anterior de la app no necesita la base: no lee la columna, y sus pedidos se guardan sin enlace.
  - `rollback.sql` de `20261017100104_AddOrderTrackingToken` quita la columna, la CHECK y el índice. Los enlaces ya enviados dejan de abrir y, si se vuelve a aplicar, esos pedidos quedan sin enlace. Se puede ejecutar otra vez.
