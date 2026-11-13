# ADR 0090: El carrito y el código postal siguen a la cuenta

- Estado: Aceptada en la Fase 31J (2026-11-12).
- Fecha: 2026-11-12
- Continúa: la [ADR 0022](0022-shopping-with-an-account.md) (comprar con tu cuenta), que dejó el carrito y el código postal en el dispositivo.

## Contexto

- **El carrito de cada tienda y el código postal de Explorar vivían en el dispositivo** (`localStorage`): no seguían al cliente a otro teléfono ni a otra computadora, y un navegador que bloquea el almacenamiento los perdía al recargar.
- **Estaba en la deuda técnica** (punto 16 de la lista).

El dueño del producto pidió avanzar los puntos de la lista sin esperar su respuesta a cada decisión. Tomé las de la Fase 31J con esa autorización. **Lleva migración:** su `apply.sql` va antes del merge.

## Decisión

1. **La cuenta de cliente guarda un carrito por tienda** (`AccountCart`, tabla `AccountCarts`), como un favorito: es de la cuenta, no del negocio, el negocio no lo ve y se borra con la cuenta.
   - Guarda lo que se eligió (producto, opciones, cantidad y nota), nunca un precio: la tienda lo cobra con su menú de ahora y deja fuera lo que ya no tiene, como con el carrito del dispositivo.
   - Los límites de la tienda y de un pedido: hasta 50 productos distintos, hasta 99 de cada uno y notas de hasta 300 caracteres. La misma línea dos veces es una. Entre todas sus líneas, hasta 500 opciones: un carrito pesa unos kilobytes, no lo que una línea podría llevar 50 veces.
   - La cuenta guarda sus 10 carritos más recientes de la última semana, como un dispositivo, los vaciados incluidos; los demás se borran al guardar otro.
   - **Un carrito pedido o vaciado no se borra: queda vacío, con cuándo**, una semana (`AccountCart.Empty`). Así otro dispositivo que lo guardó antes ve que el de la cuenta es más reciente y olvida el suyo, en lugar de volver a llenar la cuenta con lo que ya se pidió. `GET` los devuelve sin líneas; `PUT` vacío y `DELETE` lo vacían.
   - Solo se ve el de una tienda activa con su menú publicado; uno de una tienda suspendida vuelve cuando la tienda vuelve.
   - `GET /api/v1/me/carts`, `PUT` y `DELETE /api/v1/me/carts/{slug}`. Solo una cuenta de cliente lo guarda: el equipo de un negocio compra como invitado.
2. **El pedido de la cuenta vacía el carrito de su tienda** en el servidor (`PublicOrderPlacement`), también el pagado en línea, desde cualquier dispositivo. Solo uno que ya existe: crear la fila podría chocar con la que otro dispositivo guarda en ese momento y el pedido fallaría por eso.
3. **En la tienda gana el más reciente.** Con sesión, la tienda espera a saber quién es y lee los carritos de la cuenta cada vez que abre (no restaura antes), y trae el carrito que cambió al último, el del dispositivo o el de la cuenta; si el de la cuenta está vacío y es más reciente, el del dispositivo se olvida. Lo que cambia se guarda en los dos: en la cuenta, un momento después del último cambio (0.8 s), una petición a la vez y la última al final, para que la cuenta nunca termine con uno más viejo. Si falla (sin conexión), el dispositivo lo conserva y el siguiente cambio lo manda.
   - **Se comparan horas del servidor:** cuando la cuenta guarda el carrito, el dispositivo toma la hora de la cuenta para ese carrito (si sigue siendo el que mandó), así un reloj adelantado no gana siempre.
   - **Si los carritos de la cuenta no se pueden leer,** la tienda trae el del dispositivo pero no lo manda tal cual: solo un cambio hecho ahí va a la cuenta, nunca el del dispositivo encima de uno más nuevo de la cuenta.
   - **Al pedir,** la tienda vuelve a leer los carritos de la cuenta, y cada guardado que llega deja en ellos lo que la cuenta guardó: Explorar ya no ofrece el que se pidió o se vació, y no ofrece el del dispositivo si la cuenta cambió el de esa tienda después.
   - **Al terminar la sesión de una cuenta de cliente,** el dispositivo olvida sus carritos y su código postal (están en la cuenta) y no manda lo que esperaba: quien entre después en ese dispositivo no se los lleva a su cuenta. Termine como termine: al cerrar sesión, aquí o en otra pestaña del navegador (la tienda abierta en esta suelta el carrito, no lo vuelve a guardar en el dispositivo y no manda lo que esperaba), al vencer o revocarse, o al no poder recuperarse cuando se abre la app. Para eso el dispositivo recuerda de qué cuenta de cliente guarda lo que tiene (`tienditapp.customer`); una sesión del equipo de un negocio no lo cambia y conserva sus carritos, que solo estaban en el dispositivo. Si entra otra cuenta de cliente y el dispositivo todavía guarda lo de una que no se olvidó (una sesión que terminó con la app cerrada), lo olvida antes de que la nueva lo vea.
4. **La cuenta guarda su código postal** (`AspNetUsers.PostalCode`), con `GET` y `PUT /api/v1/me/postal-code`. En Explorar, una vez por cuenta, el de la cuenta pasa al dispositivo, y uno escrito solo en el dispositivo pasa a la cuenta. Un cambio va a los dos. Como antes, no se escribe en el registro (ADR 0017).
5. **Deshacer la migración se detiene (error 50060) sin cambiar nada si una cuenta ya dejó un carrito (también uno vaciado) o su código postal**: se perderían. La consulta 3 del `verify.sql` los cuenta, aparte los vaciados, y dice cómo borrarlos si de todos modos se decide revertir.
6. **`sqlcmd -t 15`**: agregar la columna y crear la tabla toman un candado de esquema sobre `AspNetUsers` y `Tenants` hasta el commit.

## Consecuencias

- **Lo que logra:** el cliente empieza un pedido en el teléfono y lo termina en la computadora, y Explorar le dice desde el primer momento quién llega a su código postal.
- **Compatibilidad:** la versión desplegada no conoce la tabla ni la columna; sigue guardando el carrito y el código postal en el dispositivo. Volver a ella funciona sin `rollback.sql`.
- **Lo que limita:**
  - **Dos dispositivos que cambian el mismo carrito a la vez:** gana el último que guarda, como con el resto del perfil.
  - **«Pedir otra vez» no viaja:** el aviso de lo que no volvió se queda en el dispositivo donde se pidió; la cuenta guarda las líneas.
  - **Sin conexión,** la cuenta se entera del carrito con el siguiente cambio que sí llegue. Mientras no llega, ese carrito lleva la hora del dispositivo, y es la que se compara.
  - **Las páginas de la tienda y de la visita** siguen tomando el código postal del dispositivo (que Explorar ya alinea con la cuenta) y, con sesión, las direcciones de la cuenta.
  - **Un carrito sin pedir** se queda en la cuenta hasta una semana: el cliente lo ve en otro dispositivo aunque ya no lo quiera, hasta que lo vacíe.
- **Decisiones por omisión** (sin preferencia expresa del dueño): gana el más reciente en lugar de juntar los dos; 10 carritos y una semana, como el dispositivo; el código postal de la cuenta gana sobre el del dispositivo; detener el rollback en lugar de perder lo guardado.
