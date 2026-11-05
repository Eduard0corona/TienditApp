# ADR 0066: Responder a tiempo

- Estado: Aceptada en la Fase 29L (2026-11-05).
- Fecha: 2026-11-05
- Continúa: [ADR 0064](0064-response-time-in-explore.md) y [ADR 0065](0065-response-time-outside-explore.md) (cuánto tarda en responder cada negocio), el punto de «Fuera de esta fase» que pedía avisarle al negocio de las solicitudes sin responder.

## Contexto

Desde la Fase 29J, Explorar cuenta cuánto tarda cada negocio en responder sus solicitudes en línea. Una solicitud es de las más lentas si llega a su límite sin respuesta. Ese límite es la hora que pidió el cliente o un día después de llegar, lo que pase primero ([ADR 0064](0064-response-time-in-explore.md)).

La Agenda ya mostraba las solicitudes por confirmar. Desde la Fase 24D, la campana de Inicio las cuenta y marca como urgentes las de hoy. Pero el negocio no sabía para cuándo tenía que responder cada una para que contara a tiempo. Una solicitud para la semana siguiente que llegó ayer vence hoy, y la campana no la marcaba.

En la lista de «Después» de la [ADR 0053](0053-home-visit-quotes.md) solo quedan el anticipo por Mercado Pago y el PDF de la cotización, que necesitan decisiones del dueño del producto. Esta fase toma lo que la ADR 0065 dejó fuera y no necesita decisiones suyas.

Tomé las decisiones de la Fase 29L con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El límite es uno solo:** `ResponseTimes.AnswerBy` da la hora que pidió el cliente (`RequestedStartAt`, o su hora si no la tiene) o un día después de llegar, lo que pase primero. Es el mismo límite de la consulta de Explorar, que lo escribe para SQL.
2. **La Agenda lo dice:**
   - cada cita lo manda en `answerBy` mientras espera respuesta (`AppointmentDto`, nulo para las demás);
   - cada solicitud por confirmar dice «Responde antes de las 3:30 p.m. para que cuente a tiempo» si vence hoy, o «Responde mañana antes de las 10:00 a.m. para que cuente a tiempo» si vence mañana, en la zona del negocio («de la 1:30» en la hora de la una). Mientras no pasa, el límite cae en las próximas 24 horas; otro día («antes del jueves 4 a las 10:00 a.m.») solo sale si el reloj del dispositivo está mal;
   - una que ya pasó su límite dice «Ya pasó el límite para responder a tiempo»;
   - el texto vuelve a leer el reloj cada minuto, así que cambia con la Agenda abierta.
3. **La campana**, que cuenta las solicitudes de ahora en adelante (Fase 24D), **marca como urgentes** las de hoy, como antes, y además las que hay que responder hoy para que cuenten a tiempo o que ya pasaron su límite y siguen pendientes. Una que el negocio movió a hoy es urgente aunque su límite sea mañana. Su texto dice «(N por responder hoy)».
4. **Fuera de esta fase:**
   - avisar por WhatsApp, por correo o en tiempo real. Desde la Fase 29N se avisa en la app al momento y por correo a quien lo elige o, sin elegir, a los dueños; WhatsApp sigue fuera ([ADR 0068](0068-tell-the-business-of-each-request.md));
   - ordenar la Agenda por límite;
   - lo demás de la [ADR 0065](0065-response-time-outside-explore.md), «Fuera de esta fase».

## Consecuencias

- **Lo que logra:** el negocio sabe qué solicitud responder primero para no volverse lento en Explorar, y la campana lo avisa el día que vence.
- **Lo que limita:**
  - el texto usa el reloj del dispositivo para decir si el límite ya pasó y si cae hoy o mañana;
  - la campana no cuenta una solicitud que ya empezó, como desde la Fase 24D, aunque todavía se pueda aceptar; la Agenda le dice que ya pasó el límite;
  - una solicitud que el negocio movió antes de la hora que pidió el cliente conserva su límite, que puede caer después de la nueva hora: la Agenda dice ese límite, y la campana la marca por su nueva hora si es hoy;
  - una solicitud cuyo límite cayó antes de la primera respuesta guardada no cuenta en Explorar ([ADR 0064](0064-response-time-in-explore.md)), aunque la Agenda diga que ya pasó el límite.
- **Decisión por omisión** (sin preferencia expresa del dueño): el texto de la Agenda y que la campana cuente como urgentes las que ya pasaron su límite.
- **Reversibilidad:** no hay migración. Volver a la versión anterior quita el texto de la Agenda, y la campana vuelve a marcar como urgentes solo las de hoy.
