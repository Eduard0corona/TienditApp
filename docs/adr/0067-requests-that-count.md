# ADR 0067: Las solicitudes que cuentan

- Estado: Aceptada en la Fase 29M (2026-11-05).
- Fecha: 2026-11-05
- Continúa: [ADR 0064](0064-response-time-in-explore.md), [ADR 0065](0065-response-time-outside-explore.md) y [ADR 0066](0066-answer-requests-in-time.md) (cuánto tarda en responder cada negocio). Toma el punto de «Fuera de esta fase» que pedía defenderse de las solicitudes falsas.

## Contexto

Desde la Fase 29J, Explorar dice en cuánto suele responder cada negocio, y cada categoría pone primero a los que responden antes. Desde la 29K lo dicen también la tienda, la página de citas y la solicitud de visita.

La [ADR 0064](0064-response-time-in-explore.md) dejó dicho que se puede manipular («Lo que limita»):

- **Un negocio puede pedirse citas a sí mismo** y aceptarlas al momento. Con tres basta para decir «Suele responder en menos de una hora» y subir en su categoría.
- **Un competidor puede mandarle solicitudes** que nadie va a atender. Cada una cuenta como de las más lentas.

El tope de la [ADR 0025](0025-online-booking.md) limita cuántas solicitudes por confirmar tiene un teléfono a la vez, no cuántas manda en 90 días.

En la lista de «Después» de la [ADR 0053](0053-home-visit-quotes.md) solo quedan el anticipo por Mercado Pago y el PDF de la cotización, que necesitan decisiones del dueño del producto. Esta fase toma lo que las ADR 0064 a 0066 dejaron fuera y no necesita decisiones suyas.

Tomé las decisiones de la Fase 29M con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Las del propio equipo no cuentan.**
   - Una solicitud en línea o de visita viene del equipo si:
     - la manda alguien con sesión de un miembro del negocio (invitado o activo; no uno dado de baja);
     - o llega con el teléfono de la cuenta de uno de ellos, el del negocio, el de una de sus sucursales o el de su WhatsApp.
   - Los teléfonos se comparan como número (E.164), como los haya escrito su dueño. Un celular de México con el 1 que pone WhatsApp (`+52 1 …`) es el mismo (`MessagingPhone.ToKey`).
   - Se decide una vez, al llegar, en el negocio que la página pública ya fijó y antes del candado de la reserva. Se guarda en `Appointment.FromTeam` (migración `20261105081150_AddAppointmentFromTeam`). Las de antes de la fase quedan como de clientes.
   - Explorar lee solo esa marca: no compara teléfonos entre negocios.
2. **Cada cliente cuenta una vez.**
   - El cliente es el teléfono con que llegó la solicitud. Se guarda al llegar como clave en `Appointment.RequestPhone` (`MessagingPhone.ToKey`: un celular escrito `+521` y `+52` es el mismo), así que cambiar después el teléfono de la cita en la Agenda no junta ni separa clientes. La migración llena la clave de las solicitudes que ya existen con el teléfono que tienen hoy.
   - De las solicitudes de un mismo cliente a un negocio en los 90 días, Explorar cuenta solo la última que llegó entre las que ya se decidieron (respondida a tiempo, o llegada a su límite, [ADR 0064](0064-response-time-in-explore.md)).
   - Una más nueva que todavía no se decide no toma su lugar, ni una del equipo con el mismo teléfono.
   - El mismo cliente cuenta aparte en cada negocio.
   - La clave solo se compara dentro de la base, en la consulta: no sale de ella. La consulta busca la última llegada de cada clave en un índice nuevo (`IX_Appointments_OnlineRequests_Customer`, por negocio, clave y llegada); otra forma, como un `NOT EXISTS`, compara cada pareja de solicitudes de un negocio.
   - Desde 3 solicitudes ([ADR 0064](0064-response-time-in-explore.md)) quiere decir ahora desde 3 clientes.
3. **La Agenda y la campana:**
   - una solicitud del equipo dice «De tu equipo: no cuenta en Explorar» y no lleva para cuándo responderla ([ADR 0066](0066-answer-requests-in-time.md)). La API manda `fromTeam` y `answerBy` nulo en `AppointmentDto`;
   - la campana la marca como urgente solo si es de hoy.
4. **Lo que lee el dueño** en «Cómo te ven en TienditApp» ([ADR 0065](0065-response-time-outside-explore.md)):
   - con un tramo: «Lo calculamos con tus solicitudes en línea de los últimos 90 días, la última de cada cliente y sin las de tu equipo. …»;
   - sin suficientes: «Todavía no contamos solicitudes en línea de 3 clientes para saber en cuánto sueles responder: …».
5. **Fuera de esta fase:**
   - **Que el negocio marque una solicitud como falsa.** Se usaría para borrar las que dejó pasar.
   - **Pedir que el cliente confirme su teléfono** antes de pedir: es otro paso para todos los clientes.
   - **Clasificar las solicitudes de antes de la fase.** No se sabe quién las mandó.
   - Lo demás de la [ADR 0066](0066-answer-requests-in-time.md), «Fuera de esta fase».

## Consecuencias

- **Lo que logra:**
  - Un negocio ya no mejora su tiempo pidiéndose citas desde su teléfono, el de su equipo o con su sesión.
  - Con un solo teléfono, ni el negocio ni un competidor mueven el número: necesitan tres teléfonos distintos para decir algo.
  - Lo que cuenta es la última experiencia de cada cliente.
- **Lo que limita:**
  - **Un teléfono que el negocio no registró** (el de un pariente, otro número suyo) cuenta como de un cliente. Uno solo pesa como un cliente.
  - **Los teléfonos del equipo los declara el negocio**, sin confirmarlos: un negocio podría registrar como empleado el teléfono de un cliente para que sus solicitudes siguientes no cuenten.
  - **Una solicitud con el teléfono de un cliente** que el negocio se mande y acepte rápido toma el lugar de la última de ese cliente. Ese cliente recibe por WhatsApp el aviso de la solicitud y de su respuesta.
  - **Un competidor con varios teléfonos** todavía puede mandar solicitudes que nadie atiende, una por teléfono, con el tope de la [ADR 0025](0025-online-booking.md).
  - **Un miembro que cambia su teléfono** después de pedir no cambia la marca de lo que ya pidió.
  - **Un cliente que repite** pesa como uno: si la última vez se le dejó pasar, eso cuenta aunque antes se le haya respondido rápido.
  - **Dos solicitudes del mismo cliente** guardadas en el mismo instante (al tick) cuentan las dos.
  - **Las que guarde la versión anterior** mientras se publica esta no tienen clave (`''`): Explorar las cuenta como un solo cliente de su negocio.
  - **La Agenda** sigue diciendo «para que cuente a tiempo» en una solicitud de un cliente que tiene otra más nueva por decidir. Solo cuenta la que se decida al último.
  - Lo demás es lo de las [ADR 0064](0064-response-time-in-explore.md) y [ADR 0065](0065-response-time-outside-explore.md): empieza vacío, horas de reloj y la foto de hasta 10 minutos fuera de Explorar.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - quiénes son el equipo: los miembros no dados de baja, el negocio, sus sucursales y su WhatsApp;
  - contar la última solicitud de cada cliente;
  - el texto de la Agenda y de la nota del dueño.
- **Reversibilidad:**
  - la migración agrega dos columnas (0 y `''` por omisión), llena la clave de las solicitudes que existen y agrega un índice; la versión anterior funciona sin `rollback.sql`;
  - volver al código anterior hace que Explorar cuente otra vez todas las solicitudes;
  - `rollback.sql` borra las columnas y el índice, con lo que se pierde qué solicitudes vinieron del equipo y con qué teléfono llegó cada una.
