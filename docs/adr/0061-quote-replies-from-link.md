# ADR 0061: El cliente responde la cotización desde su enlace

- Estado: Aceptada para responder sin anticipo en la Fase 29G (2026-11-02).
- Fecha: 2026-11-02
- Continúa: [ADR 0053](0053-home-visit-quotes.md) (oficios a domicilio, la visita para cotizar), primer punto de «Después».

## Contexto

Desde la Fase 29C ([ADR 0053](0053-home-visit-quotes.md)) la cotización llega al cliente con un enlace que la abre sin cuenta (`/cotizacion#…`). Hasta ahora ese enlace solo la muestra: para aceptarla, el cliente le responde al negocio por WhatsApp o en persona, y alguien del negocio registra la respuesta con el canal. Eso hace que:

- una respuesta llegue tarde porque alguien tiene que capturarla;
- el negocio no sepa si el cliente ya decidió hasta que lo vea en un mensaje;
- el cliente no tenga en la cotización una forma de decir que sí.

La ADR 0053 dejó para después «que el cliente acepte la cotización desde su enlace, con un anticipo por Mercado Pago ([ADR 0031](0031-online-payments.md))». Cobrar el anticipo trae sus propias decisiones (cuánto, qué pasa con un pago que llega tarde, cómo entra a la orden). Esta ADR decide primero la respuesta sin pago, que es útil sola, y deja el anticipo para otra fase.

Tomé las decisiones de la Fase 29G con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El enlace es la llave**, como el seguimiento de un pedido ([ADR 0023](0023-order-tracking.md)). Quien tiene el enlace, que no se puede adivinar y que el negocio le mandó al cliente, acepta o rechaza esa versión. Puede hacerlo mientras la versión está enviada y vigente en el día del negocio, y mientras el enlace abre (hasta 30 días después de su vigencia).
   - **No hay cuenta ni código:** pedir un código por SMS pondría una barrera que el WhatsApp o el correo con el enlace ya resuelven.
   - **El enlace viaja en el cuerpo** de la petición, nunca en la ruta ni en la consulta.
   - **Uno desconocido, cambiado o vencido no existe (404)**, igual que al abrirlo.
2. **El negocio decide si se ofrece:** `TenantSettings.QuoteOnlineReplies`, en Cotizaciones › Respuesta del cliente («Tus clientes responden desde su enlace», dueños y administradores).
   - **Encendida por omisión** para todos los negocios, también los que existen.
   - **Apagada**, el enlace muestra la cotización y «Responder al negocio», como hasta ahora, y el negocio registra la respuesta. Una cotización que el cliente ya respondió conserva su respuesta, y una corregida o vencida dice eso, no que está apagada.
   - **Cambiarla queda en la actividad.**
3. **Un canal nuevo, «Desde su enlace»** (`QuoteReplyChannel.Link`). Solo lo usa el cliente:
   - el agregado rechaza que el negocio lo registre a mano (`Accept` y `Reject`);
   - el validador también lo rechaza;
   - el diálogo del negocio no lo ofrece;
   - la base lo admite en `CK_Quotes_Reply`.
4. **La respuesta** es «Aceptar la cotización» o «No la acepto», con un mensaje opcional de hasta 500 caracteres.
   - **Antes de enviarla**, el cliente lee lo que significa. Al aceptar ve el total por pagar, ya con la visita descontada si aplica, y que el negocio se pondrá en contacto para hacer el trabajo. Al rechazar ve que puede decir por qué.
   - **Su mensaje** queda en la cotización como lo que respondió el cliente.
   - **Aceptada desde el enlace** descarta las correcciones en borrador, como cuando el negocio registra la aceptación.
5. **Una sola respuesta** (`Quote.ReplyFromLink`):
   - la misma respuesta otra vez no cambia nada, sin importar quién la dio;
   - otra respuesta distinta se rechaza: «Esta cotización ya tiene respuesta. Si cambiaste de opinión, escríbele al negocio.»;
   - una versión corregida se rechaza con un enlace a la nueva, y una vencida se rechaza con su propio mensaje para el cliente.
6. **Al mismo tiempo que el negocio:** la cotización tiene `rowversion`. Si el negocio registra una respuesta o envía una corrección mientras el cliente responde, gana el que guardó primero.
   - El otro recibe un conflicto (409): «La cotización acaba de cambiar: ábrela otra vez para ver cómo quedó.»
   - Si lo que se guardó primero es la misma respuesta (un doble toque, otra pestaña o el negocio registrándola), esa es la respuesta: no hay conflicto.
   - La página del cliente y el detalle del negocio se ponen al día solos, y el cliente sigue viendo por qué no se tomó su respuesta.
7. **La actividad** guarda «Cotización aceptada» o «Rechazada» con el folio, la versión, el total y el canal. Quien la hizo aparece como «Cliente (enlace)».
   - Nunca guarda el mensaje, el teléfono, la IP ni otro dato del cliente.
8. **Lo que ve el cliente:**
   - **Mientras puede responder**, «Aceptar la cotización» es la acción principal. El WhatsApp del negocio queda como «Preguntar algo al negocio».
   - **Respondida desde el enlace**, ve cuándo la aceptó o rechazó y su mensaje.
   - **Si la respuesta la registró el negocio**, ve que la aceptó o la rechazó, pero nunca lo que el negocio anotó.
9. **Lo que ve el negocio:**
   - **En el detalle:** «Aceptó desde su enlace el …», con el mensaje.
   - **En Inicio › Requiere atención** (y en la campana): «N cotizaciones aceptadas por convertir en orden» lleva a Cotizaciones › Por convertir.
     - Cuenta las aceptadas en los últimos 30 días que no se han convertido; el filtro «Por convertir» lista las mismas, con la misma regla (`QuotesToConvert`). Las aceptadas desde el enlace cuentan como urgentes, porque el negocio puede no saberlo todavía.
     - Lo ve quien toma pedidos en un negocio con órdenes de servicio.
   - **En la lista:** cada cotización respondida desde el enlace dice «Desde su enlace».
10. **Ruta:** `POST /api/v1/public/quotes/reply` con `{ token, accepted, response }`, anónima y con el límite de peticiones de un pedido en línea (`public-order`). `accepted` es obligatorio: sin él se rechaza, nunca se lee como un rechazo.
    - Responde la cotización como la ve el cliente (`PublicQuoteDto`), con `canReply`, `repliedAt`, `repliedFromLink` y `response`.
    - La configuración de cotizaciones lleva `onlineReplies`. Si el cliente no lo manda (una versión anterior del navegador), queda como está.
    - La lista de cotizaciones suma el filtro `ToConvert` (6) y `repliedFromLink` en cada renglón.
11. **Fuera de esta fase:**
    - **El anticipo por Mercado Pago al aceptar.** Necesita decidir cuánto se cobra, qué pasa si el pago llega tarde o la cotización cambió, y cómo entra el pago a la orden ([ADR 0031](0031-online-payments.md), [ADR 0044](0044-online-payment-reconciliation.md)).
    - **Que aceptar convierta sola la cotización en su orden:** la conversión sigue siendo del negocio (Fase 29E), que decide cuándo promete el trabajo.
    - **Un aviso al negocio por WhatsApp, correo o en tiempo real:** hoy se entera por Inicio, por la lista y por la actividad.
    - **Que el cliente cambie su respuesta desde el enlace.**
12. **La migración** `20261102151555_AddQuoteOnlineReplies`:
    - agrega `TenantSettings.QuoteOnlineReplies` (`bit`, no nula, 1 por omisión);
    - vuelve a crear `CK_Quotes_Reply` para admitir `ReplyChannel` 4;
    - no cambia datos, y la versión anterior la tolera;
    - su rollback se detiene con el error 50055 en cuanto un cliente respondió desde su enlace o un negocio apagó la opción.

## Consecuencias

- **Lo que logra:**
  - El cliente acepta o rechaza en el mismo lugar donde lee la cotización, sin cuenta, y el negocio se entera sin capturar nada.
  - Lo que el cliente escribe llega junto a su respuesta, y la actividad deja claro que respondió el cliente y por dónde.
  - El negocio que prefiere tratar en persona apaga la opción, y todo queda como antes.
- **Lo que limita:**
  - Quien tenga el enlace puede responder, por ejemplo si el cliente lo reenvía. Es lo mismo que con el seguimiento de un pedido, y el negocio ve el mensaje y la hora.
  - Aceptada no se deshace desde el enlace. Si el cliente se equivoca, el negocio no puede reabrirla (hasta hoy, una aceptada no se corrige) y hace una cotización nueva.
  - El negocio se entera al abrir TienditApp, no al instante.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - la opción empieza encendida para todos;
  - el aviso de Inicio cuenta 30 días hacia atrás.
- **Reversibilidad:** volver a la versión anterior funciona sin `rollback.sql`. El enlace deja de ofrecer la respuesta, y una cotización respondida desde el enlace se ve aceptada o rechazada, sin decir por dónde. El `rollback.sql` se detiene si quitarlo perdería por dónde respondió un cliente o la elección de un negocio.
