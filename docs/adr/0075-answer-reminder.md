# ADR 0075: Recordar una solicitud antes de su límite

- Estado: Aceptada en la Fase 29U (2026-11-07).
- Fecha: 2026-11-07
- Continúa: [ADR 0068](0068-tell-the-business-of-each-request.md), «Fuera de esta fase»: recordar una solicitud antes de su límite. Usa el límite para responder a tiempo de la [ADR 0066](0066-answer-requests-in-time.md).

## Contexto

Desde la Fase 29L, cada solicitud en línea tiene un límite para responderla a tiempo: la hora que pidió el cliente, o un día después de que llegó, lo que pase primero ([ADR 0066](0066-answer-requests-in-time.md)). La Agenda lo dice y la campana marca las que vencen hoy. Una que se responde después de su límite cuenta entre las más lentas en Explorar ([ADR 0064](0064-response-time-in-explore.md)).

Desde la Fase 29N, el negocio recibe un correo cuando llega cada solicitud ([ADR 0068](0068-tell-the-business-of-each-request.md)). Pero si no la responde ese día, nada se lo vuelve a decir fuera de la app, y la solicitud llega a su límite sin respuesta.

El dueño del producto pidió avanzar este punto (el 1), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29U con esa autorización. Es la primera de las dos fases de la lista que llevan migración: su `apply.sql` se aplica antes del merge.

## Decisión

1. **Cuándo se recuerda:**
   - una hora antes del límite para responderla a tiempo, si sigue por confirmar;
   - una sola vez por solicitud: la cita guarda cuándo se recordó (`Appointment.AnswerReminderSentAt`).
2. **Cuándo no:**
   - si es del propio equipo del negocio ([ADR 0067](0067-requests-that-count.md));
   - si su límite llega antes de dos horas de que llegó: el correo de su llegada basta;
   - si el negocio tiene la Agenda apagada o está suspendido: nadie puede responderla. Se recuerda si la vuelve a prender antes de su límite;
   - si ya se respondió, el cliente la canceló o ya pasó su límite. Un recordatorio que no salió a su hora (el worker estuvo detenido) sale en la siguiente pasada, mientras no llegue su límite.
3. **A quién:** a los mismos que reciben los correos de las solicitudes ([ADR 0068](0068-tell-the-business-of-each-request.md)): los miembros que eligieron recibirlos o, mientras no elijan, los dueños, con su cuenta activa y su correo confirmado, que pueden responderla. No hay una preferencia aparte: quien apaga los correos de las solicitudes tampoco recibe el recordatorio.
4. **Cómo sale:**
   - **El barrido:** el worker de mensajes, cada 5 minutos, junto a los recordatorios de las citas.
     - Lee entre negocios solo qué negocios activos, con la Agenda prendida, tienen solicitudes en línea de las últimas 24 horas sin recordar. Usa el índice que ya existe de las solicitudes en línea por fecha (Fase 29J).
     - Después entra al alcance de cada negocio. Ahí cada solicitud decide si le toca y se marca. Si otra instancia la marcó primero, o el negocio la respondió en ese momento, la versión de la cita lo detecta y la siguiente pasada lo ve.
   - **El correo:** por el outbox, uno por miembro, como el de la llegada. Antes de mandarlo se revisa otra vez que siga por confirmar antes de su límite y que el miembro los siga recibiendo.
5. **Qué dice:**
   - asunto: «Responde a tiempo la solicitud de cita en {negocio}: {servicio}, {día} a las {hora}» (o «de visita»);
   - quién la pidió y qué, para cuándo responderla para que cuente a tiempo, y la liga a la Agenda;
   - nunca el teléfono, las notas, la dirección ni las fotos del cliente;
   - el pie de los correos de la Agenda y la pantalla de Mi perfil dicen que también llega el recordatorio.
6. **La migración** `20261107061906_AddAppointmentAnswerReminderSentAt` agrega una columna que admite `NULL`, sin valor por omisión, y no cambia datos. La versión anterior sigue funcionando con ella. Su `apply.sql`, `rollback.sql` y `verify.sql` están en `database/production/`.
7. **Fuera de esta fase:**
   - un aviso en pantalla con el recordatorio: la campana ya marca las que vencen hoy;
   - por WhatsApp o con notificaciones del navegador (las mismas razones de la [ADR 0068](0068-tell-the-business-of-each-request.md));
   - que cada negocio elija cuánto antes o que no quiera el recordatorio aparte de los correos de las solicitudes: llegó en la Fase 30B ([ADR 0082](0082-order-cancellation-email-reminder-lead-and-online-orders-index.md)).

## Consecuencias

- **Lo que logra:**
  - El negocio se entera, antes de que sea tarde, de una solicitud que se le quedó sin responder.
  - Responde más a tiempo, y Explorar lo refleja.
- **Lo que limita:**
  - **De noche:** el límite de una solicitud puede caer de madrugada (un día después de que llegó), y el recordatorio sale una hora antes, a esa hora. No se espera al horario del negocio: después ya sería tarde.
  - **Puntualidad:** el barrido corre cada 5 minutos, así que el recordatorio sale entre 55 y 60 minutos antes del límite. Si el worker estuvo detenido, sale cuando vuelve, mientras no llegue el límite, aunque falten pocos minutos.
  - **Al mismo tiempo:** marcar la solicitud cambia su versión. Si el negocio la acepta o la rechaza, o el cliente la cancela, en los mismos milisegundos en que el barrido la marca, quien respondió ve «Alguien más modificó este registro» y lo intenta otra vez; el recordatorio de una ya respondida no sale.
  - **Un correo que el proceso alcanzó a mandar** antes de detenerse sale otra vez al reintentar, como los demás del outbox.
  - **Mientras se publica:** el worker de la versión anterior no conoce los eventos nuevos y aparta los que tome (`Unknown event type`). Esos recordatorios no salen, y la solicitud queda marcada como recordada.
  - **Una solicitud que llegó antes del despliegue** se recuerda mientras no llegue su límite: las que ya estén en su última hora lo reciben en la primera pasada.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - una hora antes, una sola vez, y nada si el límite llega antes de dos horas;
  - a los mismos que reciben los correos de las solicitudes, sin preferencia aparte;
  - el texto del correo y de Mi perfil.
- **Reversibilidad:**
  - volver al código anterior funciona sin `rollback.sql`: deja de mandar los recordatorios;
  - `rollback.sql` borra la columna, y con ella a qué solicitudes ya se les mandó.
