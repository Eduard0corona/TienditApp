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
   - cada solicitud por confirmar dice «Responde antes de las 3:30 p.m. para que cuente a tiempo» si vence hoy, o «Responde antes del martes 2 a las 10:00 a.m. para que cuente a tiempo» si vence otro día, en la zona del negocio;
   - una que ya pasó su límite dice «Ya pasó su hora para responder a tiempo».
3. **La campana cuenta como urgentes** las solicitudes que hay que responder hoy para que cuenten a tiempo, o que ya pasaron su hora y siguen pendientes. Su texto dice «(N por responder hoy)». Antes eran urgentes solo las de hoy.
4. **Fuera de esta fase:**
   - avisar por WhatsApp, por correo o en tiempo real;
   - ordenar la Agenda por límite;
   - lo demás de la [ADR 0065](0065-response-time-outside-explore.md), «Fuera de esta fase».

## Consecuencias

- **Lo que logra:** el negocio sabe qué solicitud responder primero para no volverse lento en Explorar, y la campana lo avisa el día que vence.
- **Lo que limita:**
  - el texto usa el reloj del dispositivo para decir si el límite ya pasó;
  - una solicitud cuyo límite cayó antes de la primera respuesta guardada no cuenta en Explorar ([ADR 0064](0064-response-time-in-explore.md)), aunque la Agenda diga que ya pasó su hora.
- **Decisión por omisión** (sin preferencia expresa del dueño): el texto de la Agenda y que la campana cuente como urgentes las que ya pasaron su hora.
- **Reversibilidad:** no hay migración. Volver a la versión anterior quita el texto de la Agenda, y la campana vuelve a marcar como urgentes solo las de hoy.
