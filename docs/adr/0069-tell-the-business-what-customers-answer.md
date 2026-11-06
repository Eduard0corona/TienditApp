# ADR 0069: Avisarle al negocio lo que responden sus clientes

- Estado: Aceptada en la Fase 29O (2026-11-06).
- Fecha: 2026-11-06
- Continúa: [ADR 0068](0068-tell-the-business-of-each-request.md) (avisarle al negocio de cada solicitud). Toma de su «Fuera de esta fase» el punto que pedía avisar por correo otras cosas: que el cliente canceló, o su respuesta a una cotización.

## Contexto

Desde la Fase 29N, el negocio se entera de cada solicitud al momento en la app y por correo ([ADR 0068](0068-tell-the-business-of-each-request.md)). Pero sus clientes también hacen otras cosas en línea:

- **Cancelan su cita** desde su cuenta o desde la liga de seguimiento (Fase 24D). Antes de esta fase, el negocio solo lo veía en la Agenda, si la abría.
- **Aceptan o rechazan una cotización** desde su liga (Fase 29G, [ADR 0061](0061-quote-replies-from-link.md)). La campana marca como urgentes las que aceptó el cliente y todavía no son orden, pero solo con la app abierta.

Para un oficio, una cotización aceptada es trabajo nuevo, y una cita cancelada es un horario libre. Enterarse tarde cuesta las dos cosas.

El dueño del producto eligió este punto entre los que no necesitan decisiones suyas.

Tomé las decisiones de la Fase 29O con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Cuándo se avisa.**
   - **Una cita cancelada por su cliente** (`Appointment.CancelByCustomer`, desde la cuenta o la liga) levanta `AppointmentCancelledByCustomerEvent`. Lleva solo ids, si es visita, si vino del equipo ([ADR 0067](0067-requests-that-count.md)) y si todavía esperaba respuesta. La que rechaza o cancela el negocio no avisa.
   - **Una cotización que el cliente acepta o rechaza desde su liga** (`Quote.ReplyFromLink`) levanta `QuoteAnsweredFromLinkEvent`, con solo ids y la respuesta. Tocar otra vez la misma respuesta no cambia nada y no avisa otra vez. La respuesta que registra el negocio (por teléfono, WhatsApp o en persona) tampoco avisa.
2. **En la app, al momento: solo las cancelaciones.**
   - El hub de la Agenda (`/hubs/appointments`) manda `AppointmentCancelled`, con el servicio, la hora, si es visita, si vino del equipo y si era solicitud. No viaja quién la canceló.
   - La barra superior hace lo mismo que con una solicitud:
     - la campana y la Agenda abierta la leen otra vez;
     - sale un aviso: «Cita cancelada», «Visita cancelada» o «Solicitud de … cancelada», con el servicio, la hora y que el horario quedó libre. Las del equipo no lo muestran.
   - **Las respuestas a cotizaciones no tienen aviso en pantalla.** Las cotizaciones piden otro permiso (`ManageOrders`), y el hub de la Agenda es para quien tiene `ManageAppointments`. Un hub de cotizaciones queda fuera de esta fase. La campana ya marca como urgentes las que aceptó el cliente desde su liga, y se lee cada minuto.
3. **Por correo.**
   - **Una cita cancelada** les llega a quienes reciben los avisos de la Agenda ([ADR 0068](0068-tell-the-business-of-each-request.md)): la misma elección (`TenantUser.RequestEmails`) y las mismas condiciones.
     - **No sale** si es del equipo, si se canceló hace más de un día o si ya llegó su hora: el horario ya no está libre para nadie.
     - **Qué dice:** asunto «Cita cancelada en {negocio}: {servicio}, {día} a las {hora}», «Visita cancelada en …» o «Solicitud de cita (o de visita) cancelada en …»; quién la canceló, de qué y para cuándo; que el horario quedó libre en la Agenda y, si era solicitud, que ya no tiene que responderla; y la liga a la Agenda.
   - **Una respuesta a una cotización** les llega a quienes:
     - son miembros activos del negocio que eligieron recibirlas o, mientras no elijan, son dueños;
     - tienen la cuenta activa y su correo confirmado;
     - pueden trabajar las cotizaciones: `ManageOrders`, en un negocio con servicios o citas que no está suspendido. Es lo que la API les pediría para abrirla.
     - **No sale** si el cliente no respondió desde su liga, si pasó más de un día, si el negocio ya convirtió la aceptada en orden, o si ya reemplazó la rechazada con otra versión.
     - **Qué dice:** asunto «Cotización aceptada (o rechazada) en {negocio}: {folio} por {total}»; quién respondió, qué cotización y por cuánto; si dejó un mensaje, que lo lea en la cotización (nunca el mensaje); y lo que sigue. En una aceptada, convertirla en orden de servicio, solo si el negocio tiene servicios. En una rechazada, corregirla y mandar otra versión. Al final, la liga a la cotización.
   - **Cómo salen los dos:**
     - por el outbox, con el proveedor de correo de la plataforma;
     - un mensaje del outbox por miembro (`AppointmentCancellationEmailEvent` y `QuoteReplyEmailEvent`), guardado con el registro de que se repartieron: cada correo sale y se reintenta solo;
     - antes de mandar cada uno se revisa otra vez que valga la pena y que el miembro los siga recibiendo y pueda atenderlo;
     - abajo dicen por qué llegan y que se dejan de recibir en Mi perfil;
     - las horas van en la zona del negocio, y los nombres escritos por el negocio y por el cliente, codificados;
     - nunca llevan el teléfono, el correo, las notas, la dirección ni las fotos del cliente, ni lo que escribió en su respuesta.
4. **Cada quien elige.**
   - **Las citas en línea:** la elección de la 29N cubre ahora las solicitudes y las cancelaciones. El pie del correo de las solicitudes lo dice.
   - **Las cotizaciones:** `TenantUser.QuoteReplyEmails` (migración `20261106060148_AddTenantUserQuoteReplyEmails`): sí, no o sin elegir (`NULL`). Mientras no elija, solo a los dueños les llegan.
     - Se elige con `GET` y `PUT /api/v1/quotes/reply-emails`, con lo que pide la API de cotizaciones (`ManageOrders` y servicios o citas).
   - **Mi perfil › Avisos por correo** reemplaza a «Avisos de solicitudes». Tiene un interruptor por aviso, «Citas en línea» y «Cotizaciones», y cada persona ve solo los que puede elegir. Dice a qué correo llegan y si falta confirmarlo.
   - Nadie elige por otro. Quien no es del equipo (el administrador de la plataforma) no tiene qué elegir. Desde la Fase 29Q, quien administra el equipo también los elige, menos los del dueño ([ADR 0071](0071-team-notices-and-orders-cleanup.md)).
5. **Lo compartido.**
   - Quién recibe un aviso y cómo se escribe el correo viven en `Application/Common/Notices`. Cada aviso dice qué eligieron sus miembros y quién puede atenderlo. La 29N usa lo mismo.
6. **Fuera de esta fase:**
   - **Un aviso en pantalla de las respuestas a cotizaciones:** necesita un hub para quien trabaja las cotizaciones.
   - **Avisar cuando el cliente cancela un pedido en línea**, o paga un anticipo.
   - **Avisar por WhatsApp, con notificaciones del navegador o con un sonido:** siguen como en la [ADR 0068](0068-tell-the-business-of-each-request.md).
   - **Que el dueño elija por su equipo.** Desde la Fase 29Q, el dueño y los administradores eligen los de cada persona, menos los del dueño ([ADR 0071](0071-team-notices-and-orders-cleanup.md)).
   - Lo demás de la [ADR 0068](0068-tell-the-business-of-each-request.md), «Fuera de esta fase».

## Consecuencias

- **Lo que logra:**
  - El negocio se entera al momento de una cita cancelada, en la app y por correo, y por correo de una cotización respondida, aunque no tenga la app abierta.
  - Cada quien decide qué avisos le llegan, por separado y sin que nadie más lo decida.
  - Los correos no reparten los datos del cliente ni lo que escribió: quien atiende los ve en la Agenda o en la cotización.
- **Lo que limita:**
  - **Un cliente que pide y cancela muchas veces** manda dos correos con cada vuelta: el de la solicitud y el de la cancelación. El tope de solicitudes por teléfono ([ADR 0025](0025-online-booking.md)) no lo acota, porque solo cuenta las que esperan respuesta y una cancelada ya no espera. Lo acota el límite de peticiones de las páginas públicas, por dirección IP (6 cada 10 minutos para pedir y para cancelar), que quien cambia de dirección rodea. Desde la Fase 29P las canceladas de las últimas 24 horas cuentan en ese tope ([ADR 0070](0070-cancellations-cap-agenda-order-and-sound.md)).
  - **Quien recibe los avisos de la Agenda recibe ahora también las cancelaciones**, sin elegirlo aparte. El pie del correo lo dice, y se apagan juntos en Mi perfil.
  - **Las respuestas a cotizaciones no avisan en pantalla:** la campana las muestra en menos de un minuto.
  - **Un correo que el proceso alcanzó a mandar** antes de detenerse sale otra vez al reintentar, como los demás del outbox.
  - **Mientras se publica:** el worker de la versión anterior no conoce los eventos nuevos y aparta los que tome (`Unknown event type`). Esos correos no salen; la Agenda y la cotización sí muestran lo que pasó.
  - **A los dueños les llegan desde el primer día:** quien no los quiera los apaga en Mi perfil.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - las cancelaciones con la misma elección que las solicitudes, y las cotizaciones con una aparte;
  - a quién le llegan sin elegir: a los dueños;
  - no avisar la respuesta que registra el negocio, ni la cancelación que hace él;
  - no mandar el de hace más de un día;
  - el texto de los avisos, de los correos y de Mi perfil.
- **Reversibilidad:**
  - la migración agrega una columna que admite `NULL`, sin valor por omisión, y no cambia datos; la versión anterior funciona sin `rollback.sql`;
  - volver al código anterior deja de mandar estos avisos, y lo que cada quien eligió se queda guardado;
  - `rollback.sql` borra la columna y, con ella, lo que cada quien eligió para las cotizaciones; no toca la elección de la Agenda.
