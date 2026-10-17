# ADR 0022: Comprar con tu cuenta

- Estado: Aceptada
- Fecha: 2026-10-17

## Contexto

Con la Fase 24A ([ADR 0021](0021-business-directory.md)) un cliente descubre negocios en Explorar y compra en el menú de cada uno, con cuenta o sin ella. Pero la cuenta no ayudaba a comprar:

- el pedido se ligaba a la cuenta solo por el teléfono, y solo si la cuenta lo había confirmado por SMS;
- el nombre, el teléfono y la dirección se escribían en cada pedido;
- el carrito se perdía al recargar o al salir del menú;
- «Pedir otra vez» solo abría el menú vacío;
- Explorar no decía qué negocio llega al domicilio del cliente;
- un pedido enviado dos veces (una respuesta que se perdió, un doble toque) se registraba dos veces.

La Fase 24B, «Comprar con tu cuenta», del informe «TienditApp para clientes», resuelve eso. El seguimiento del pedido es la 24C.

## Decisión

1. **La sesión dice de quién es el pedido.** Un pedido del menú en línea enviado con la sesión de una cuenta de cliente activa (`CustomerAccounts.FindSignedInAsync`: rol `Customer` y activa) queda a nombre de esa cuenta (`CustomerUserId`), sin confirmar el teléfono.
   - Nunca lleva cliente temporal, así que nadie que confirme ese teléfono después lo reclama (`TemporaryCustomerClaimService`).
   - La sesión del equipo de un negocio compra como invitado: el teléfono decide, como antes.
   - El recibo dice si quedó en la cuenta (`PublicOrderReceiptDto.LinkedToAccount`): la tienda ofrece «Ver mis pedidos». A un visitante le ofrece crear su cuenta.
   - La tienda escribe el nombre y el teléfono de la cuenta, que se pueden cambiar en cada pedido.
2. **Direcciones de la cuenta, hasta 10** (`AccountAddress`, tabla `AccountAddresses`).
   - Son de la cuenta, no de un negocio: sirven en cualquier negocio que entregue. Por eso no son `ITenantScoped` (`ArchitectureTests` las lista con las demás entidades sin negocio) y se leen siempre por la cuenta de la sesión.
   - Cada una tiene un nombre (hasta 60 caracteres; «Dirección» si no se escribe), código postal, dirección y referencias, con las reglas de `DeliveryAddress`.
   - La misma dirección (código postal y renglón, sin mayúsculas ni espacios de más) no se guarda dos veces: guardarla otra vez solo cambia su nombre y sus referencias.
   - Se agregan, cambian y borran en Cuenta (`/cuenta`, `GET/POST/PUT/DELETE /api/v1/me/addresses`, `[RequiresAuthentication]`; solo una cuenta de cliente las guarda). Borrar una no cambia los pedidos que ya se enviaron ahí: el pedido guarda su propia dirección (`OrderDelivery`).
   - Al pedir a domicilio, la tienda ofrece primero la última usada (salvo que el cliente ya esté escribiendo otra). Al enviar, el pedido marca como usada la dirección guardada del mismo lugar, o guarda una nueva si el cliente deja marcado «Guardar esta dirección en mi cuenta» (empieza marcado) y le queda lugar. Se escribe en el mismo guardado del pedido: si el pedido falla, la dirección tampoco cambia. Con 10 direcciones el pedido se envía igual, sin guardarla.
3. **Llave del pedido.** La tienda manda una llave por pedido (`IdempotencyKey`), la misma en cada reintento del mismo carrito, y nueva cuando cambia el carrito, la entrega o los datos del cliente.
   - Se guarda en `Order.ClientRequestId`, con el índice único filtrado `IX_Orders_TenantId_ClientRequestId` que ya usaba la venta sin conexión.
   - La misma llave otra vez responde el recibo del pedido ya registrado, antes de revisar el horario o la caja: el negocio pudo cerrar desde el primer intento.
   - Solo responde el pedido en línea del mismo teléfono. Con otro teléfono, o si la llave es de otro pedido, responde «Ese pedido ya se envió.»: la llave no sirve para leer el pedido de otro. Si el negocio ya lo canceló, responde «Ese pedido ya se envió y el negocio lo canceló.» en lugar de un recibo que diga que lo confirmará.
   - Dos envíos a la vez: el segundo choca con el índice, descarta todo su trabajo (folio, insumos y dirección) y responde el recibo del primero.
4. **Explorar por código postal.** El cliente escribe su código postal («Entregar en…») y cada tarjeta de un negocio que entrega dice si llega y cuánto cobra: «Envío $35.00», «Envío gratis», «… más IVA» si sus precios no lo incluyen, o «No llega a tu código postal».
   - El código postal vive en el dispositivo (`localStorage`) y solo viaja como filtro de Explorar (`GET /api/v1/public/directory?postalCode=`). El servidor no lo guarda.
   - Se responde con la zona activa del negocio que lo incluye, sin su nombre: la misma que cobra el menú ([ADR 0017](0017-delivery-zones.md)). Un código postal está en una zona por negocio como mucho.
   - La foto de 30 segundos no cambia. La tarjeta se copia por petición, y las zonas de los negocios de la página que entregan se leen en una consulta (ninguna si ninguno entrega). Usa un `IgnoreQueryFilters` revisado más (`TenantFilterBypassAllowlistTests`), que une cada código postal con la zona de su mismo negocio, y el índice nuevo `IX_DeliveryZonePostalCodes_PostalCode`.
   - La tienda toma ese código postal la primera vez que el pedido va a domicilio, si la cuenta no tiene direcciones.
5. **Carrito guardado en el dispositivo.** El carrito de cada tienda se guarda en el dispositivo (`savedCart.ts`) hasta que se envía o se vacía, por 7 días y en 10 tiendas como mucho.
   - Guarda lo elegido (producto, opciones y cantidad), nunca el precio. Al volver, la tienda lo arma con el menú de ahora, a sus precios. Deja fuera lo que no está disponible ahora y las opciones que ya no existen o no forman una elección válida, y dice cuántos productos quedaron fuera.
   - Explorar ofrece seguir con el último carrito sin enviar.
6. **«Pedir otra vez» llena el carrito.** En Mis pedidos, «Pedir otra vez» guarda las líneas del pedido como carrito de esa tienda y la abre, con la misma regla del punto 5.
   - Mis pedidos manda el producto y las opciones de cada línea (`MyOrderItemDto.ProductId` y `OptionIds`).
   - Solo regresan cantidades enteras, porque la tienda pide por pieza: un producto por kilo o en fracciones no regresa, y la tienda dice cuántos se piden por peso. Un pedido todo por peso abre el menú sin tocar el carrito que haya.

## Consecuencias

- **Lo que logra:**
  - Un cliente con cuenta pide sin escribir sus datos ni su dirección, y encuentra todos sus pedidos en su cuenta.
  - Un reintento ya no duplica el pedido.
  - Explorar dice antes de entrar si el negocio llega al domicilio y cuánto cobra.
  - El carrito sobrevive a una recarga y a ir a ver otro negocio.
- **Lo que limita:**
  - El carrito y el código postal son del dispositivo: no siguen a la cuenta a otro teléfono ni a otra computadora.
  - Las direcciones no se validan contra un mapa ni se geolocalizan. El código postal decide la zona, como en la 22B.
  - La tarjeta de Explorar da el envío de la zona, no el total: el mínimo y el envío gratis los aplica el menú al pedir.
  - Un cliente anterior, sin llave, todavía puede registrar dos veces el mismo pedido.
  - Un pedido enviado sin sesión, también los de antes de la 24B, llega a la cuenta solo cuando la cuenta confirma ese teléfono, como antes.
- **Reversibilidad:**
  - Volver a la versión anterior de la app no necesita la base: no lee las direcciones, y el índice nuevo no cambia lo que lee.
  - `rollback.sql` de `20261017070332_AddAccountAddresses` se detiene con `THROW 50017` en cuanto una cuenta guardó una dirección: volver atrás las borraría. Si no hay ninguna, quita la tabla y el índice, y se puede ejecutar otra vez.
