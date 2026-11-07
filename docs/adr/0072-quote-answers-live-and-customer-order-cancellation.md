# ADR 0072: El aviso en pantalla de las cotizaciones y el cliente cancela su pedido en línea

- Estado: Aceptada en la Fase 29R (2026-11-07).
- Fecha: 2026-11-07
- Continúa: [ADR 0069](0069-tell-the-business-what-customers-answer.md), «Fuera de esta fase»: el aviso en pantalla de las respuestas a cotizaciones, que pedía un hub para quien las trabaja, y avisar cuando el cliente cancela un pedido en línea.

## Contexto

Desde la Fase 29O, el negocio se entera por correo de cada cotización que su cliente acepta o rechaza desde su enlace ([ADR 0069](0069-tell-the-business-what-customers-answer.md)). En la app no había aviso: el hub de la Agenda pide el permiso de la Agenda, y las cotizaciones son de quien trabaja los pedidos. La campana marcaba las aceptadas, pero solo al leerse cada minuto.

Avisar cuando el cliente cancela un pedido en línea tenía otro problema: **el cliente no podía cancelarlo.** El enlace de seguimiento (Fase 24C, [ADR 0023](0023-order-tracking.md)) y Mis pedidos solo mostraban cómo iba. Para cancelar, el cliente tenía que escribirle o llamarle al negocio.

El dueño del producto pidió avanzar estos puntos, entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29R con esa autorización.

## Decisión

1. **Un hub para quien trabaja las cotizaciones.**
   - `/hubs/quotes` acepta lo mismo que la API de cotizaciones: el permiso de Pedidos (`ManageOrders`) en un negocio con servicios o citas. Cierra la conexión si pierde la sesión o el permiso, como los demás hubs.
   - Cuando el cliente acepta o rechaza una cotización desde su enlace, el hub de su negocio oye `QuoteAnswered` con la cotización, su folio, su versión y si la aceptó. **Nunca lo que escribió el cliente**, su nombre ni su teléfono.
   - El aviso en pantalla es de buena fe: sale en el mismo proceso después de guardar. El correo sigue siendo el aviso que no se pierde (outbox).
   - La barra superior se conecta, aparte de la primera pantalla, para quien trabaja las cotizaciones en un negocio activo. Con cada respuesta, la campana, la lista y la cotización abierta se leen otra vez, y un aviso dice «Te aceptaron la cotización COT-000007» o «Te rechazaron…», con la versión si es una corrección. Sin sonido: una respuesta no urge como un pedido.
2. **El cliente cancela su pedido en línea mientras el negocio no lo acepta.**
   - Desde el enlace de seguimiento (`POST /api/v1/public/orders/tracking/cancel`, sin sesión, con el enlace en el cuerpo) o desde Mis pedidos (`POST /api/v1/me/orders/{id}/cancel`, solo los de su cuenta).
   - Solo un pedido del menú en línea (`Online`) que sigue «Por confirmar» (`Pending`), sin pagos, sin condonaciones, que no se pidió con el QR de una mesa y de un negocio activo. Lo decide el agregado (`Order.CancelByCustomer`), con un mensaje para cada caso: «El negocio ya aceptó tu pedido: para cancelarlo, llámale.», «Tu pedido ya tiene un pago…», «Un pedido de una mesa se cancela con quien te atiende.», «El negocio ya canceló tu pedido.».
   - Queda en el historial y en la bitácora del negocio como lo hizo «Cliente» (nunca el correo de la cuenta), con el motivo «Lo canceló el cliente.». Lo que tomó del inventario regresa, como en cualquier cancelación.
   - Cancelar otra vez (dos toques, o un reintento que perdió la respuesta) contesta el pedido como quedó, sin cancelarlo de nuevo.
   - Si el negocio lo acepta al mismo tiempo, gana el negocio: la cancelación lee el pedido otra vez y dice que ya lo aceptó.
   - El enlace y Mis pedidos dicen si se puede cancelar (`canCancel`) y muestran «Cancelar pedido», con confirmación, solo mientras se puede.
3. **El negocio se entera al momento.**
   - El evento de la cancelación dice si fue el cliente (`OrderCancelledEvent.ByCustomer`), y el hub de pedidos lo manda con el folio (`byCustomer`, `orderNumber`).
   - Pedidos dice «El cliente canceló el pedido ORD-000123 antes de que lo aceptaran.» y suena hacia abajo, con el mismo sonido que avisa los pedidos en línea.
   - Al cliente no le llega el WhatsApp de «pedido cancelado»: acaba de hacerlo él.
4. **Fuera de esta fase:**
   - Avisar por correo una cancelación de un pedido: el negocio que recibe pedidos en línea los atiende con la app abierta.
   - Que el cliente cancele un pedido ya aceptado, pagado o de otro canal, o pida que le devuelvan un pago en línea.
   - Avisar en pantalla un anticipo pagado.
   - Un sonido para las respuestas a cotizaciones.

## Consecuencias

- **Lo que logra:**
  - Quien trabaja las cotizaciones se entera en la app de cada respuesta, en cualquier pantalla, sin esperar el correo ni la campana.
  - El cliente cancela un pedido que todavía no se prepara sin llamar, y el negocio no prepara lo que nadie va a recoger.
- **Lo que limita:**
  - **El aviso en pantalla se pierde** si la conexión está caída: al reconectar se leen otra vez la campana y las cotizaciones, sin aviso. El correo no se pierde.
  - **Un pedido sin aceptar se cancela sin preguntar al negocio.** Si ya empezó a prepararlo sin aceptarlo en la app, lo pierde. Aceptarlo en la app lo evita.
  - **El enlace cancela sin sesión:** quien tiene el enlace cancela el pedido, como ya lo seguía. Es el mismo enlace secreto de 43 caracteres, con el límite de peticiones de los pedidos públicos.
  - **Contrato:** el payload de `OrderCancelled` suma `orderNumber` y `byCustomer`; el seguimiento y el detalle de Mis pedidos suman `canCancel`. Los clientes anteriores los ignoran.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - qué pedido puede cancelar el cliente y desde dónde;
  - que el aviso de cotizaciones no suene y que la cancelación del cliente no avise por correo;
  - los textos del botón, la confirmación y los avisos.
- **Reversibilidad:** sin migración. Volver al código anterior quita el hub de cotizaciones, los dos endpoints de cancelar y el botón; los pedidos que el cliente canceló siguen cancelados, con su historial.
