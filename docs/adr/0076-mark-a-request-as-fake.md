# ADR 0076: Marcar una solicitud como falsa

- Estado: Aceptada en la Fase 29V (2026-11-07).
- Fecha: 2026-11-07
- Continúa: [ADR 0067](0067-requests-that-count.md), «Fuera de esta fase»: que el negocio marque una solicitud como falsa. Usa el tiempo de respuesta de la [ADR 0064](0064-response-time-in-explore.md) y el tope por teléfono de la [ADR 0025](0025-online-booking.md).

## Contexto

Desde la Fase 29J, Explorar dice en cuánto suele responder cada negocio, y una solicitud que nadie respondió antes de su límite cuenta entre las más lentas ([ADR 0064](0064-response-time-in-explore.md)). La Fase 29M dejó de contar las del propio equipo, y cada cliente cuenta una vez ([ADR 0067](0067-requests-that-count.md)).

La [ADR 0067](0067-requests-that-count.md) dejó dicho lo que todavía se puede hacer: un competidor con varios teléfonos manda solicitudes que nadie atiende, una por teléfono. El negocio no tenía cómo decir que una solicitud era falsa: solo podía rechazarla, y rechazarla le avisa al cliente por WhatsApp. Ese aviso llega al teléfono escrito, que puede no ser de quien la mandó.

El dueño del producto pidió avanzar este punto (el 12), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29V con esa autorización. Es la segunda de las dos fases de la lista que llevan migración: su `apply.sql` se aplica antes del merge.

## Decisión

1. **Qué se marca:**
   - una solicitud en línea de un cliente (cita o visita) que sigue por confirmar, también pasada su hora o su límite;
   - no una del propio equipo: ya no cuenta ([ADR 0067](0067-requests-that-count.md));
   - no una ya aceptada, rechazada o cancelada.
2. **Quién:** quien responde las solicitudes (el permiso de la Agenda y el módulo de citas), igual que al aceptar o rechazar.
3. **Qué pasa al marcarla** (`Appointment.MarkFake`):
   - se cancela y su horario queda libre, como al rechazarla;
   - no se le avisa a nadie: ni WhatsApp ni correo, porque el teléfono puede no ser de quien la mandó;
   - la cita guarda cuándo se marcó (`Appointment.MarkedFakeAt`) y no guarda una respuesta (`RespondedAt`), porque nadie la esperó;
   - Explorar no la cuenta en el tiempo de respuesta, ni deja que tome el lugar de la última solicitud de su cliente: vuelve a contar la anterior de ese teléfono, si la hay ([ADR 0067](0067-requests-that-count.md)). La consulta de Explorar no cambia: una solicitud cancelada sin respuesta y sin que la cancelara su cliente ya no contaba;
   - ese teléfono no puede pedirle otra cita ni visita a ese negocio durante 90 días desde que se marcó, los mismos días que cuenta Explorar. El cliente lee «Este negocio no está recibiendo solicitudes de este teléfono. Comunícate con él directamente.». A otros negocios sí puede pedirles.
4. **Quitar la marca** (`Appointment.UnmarkFake`, «No era falsa»):
   - la solicitud sigue cancelada y cuenta como rechazada en el momento en que se marcó;
   - el teléfono puede volver a pedir;
   - tampoco se le avisa a nadie.
5. **La Agenda:**
   - en una solicitud de un cliente, junto a «Aceptar», «Rechazar» y «Editar», está «Es falsa». Antes de marcarla dice qué pasa;
   - la marcada dice «Marcada como falsa: no cuenta en Explorar» y ofrece «No era falsa»;
   - la API manda `markedFake` en `AppointmentDto`, y los endpoints son `POST /api/v1/appointments/{id}/mark-fake` y `/unmark-fake`.
6. **Lo que lee el dueño** en «Cómo te ven en TienditApp» ([ADR 0065](0065-response-time-outside-explore.md)): «Lo calculamos con tus solicitudes en línea de los últimos 90 días, la última de cada cliente, sin las de tu equipo ni las que marcaste como falsas. …».
7. **La migración** `20261107190735_AddAppointmentMarkedFakeAt` agrega una columna que admite `NULL`, sin valor por omisión y sin índice, y no cambia datos. La versión anterior sigue funcionando con ella. El teléfono marcado se busca con el índice de cada cliente que ya existe (`IX_Appointments_OnlineRequests_Customer`, Fase 29M). Su `apply.sql`, `rollback.sql` y `verify.sql` están en `database/production/`.
8. **Fuera de esta fase:**
   - un tope de cuántas solicitudes puede marcar un negocio, o que alguien revise las que marca;
   - pedir que el cliente confirme su teléfono antes de pedir (las mismas razones de la [ADR 0067](0067-requests-that-count.md));
   - bloquear por la IP o por la cuenta de quien la mandó;
   - avisarle a TienditApp de un teléfono que varios negocios marcan.

## Consecuencias

- **Lo que logra:**
  - Una solicitud de alguien que no quería agendar ya no hunde el tiempo de respuesta del negocio en Explorar.
  - El teléfono que la mandó deja de llenarle la Agenda durante 90 días.
  - Nadie recibe el aviso de un rechazo que no pidió.
- **Lo que limita:**
  - **Se puede abusar:** un negocio puede marcar como falsas las solicitudes reales que dejó pasar, y así mejorar su tiempo. El costo es real: esos clientes no reciben respuesta y no pueden volver a pedirle en 90 días. No hay un tope ni una revisión (punto 8).
  - **Un cliente real marcado por error** no se entera: su seguimiento dice que se canceló, y si vuelve a pedir lee que el negocio no recibe solicitudes de su teléfono. «No era falsa» lo deshace, sin avisarle.
  - **Quien escribe un teléfono ajeno** en la página de un negocio puede enterarse de que ese negocio lo marcó.
  - **Un competidor con muchos teléfonos** todavía puede mandar solicitudes, una por teléfono. Ahora el negocio las saca de la cuenta una por una.
  - **La marca vale por negocio:** el mismo teléfono puede pedirle a otros negocios.
  - **Mientras se publica,** la versión anterior no conoce la columna: no ofrece la marca, y una solicitud ya marcada se ve como cancelada, sin respuesta.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - no avisarle a nadie al marcar ni al quitar la marca;
  - 90 días sin que ese teléfono pueda pedir;
  - poder quitar la marca, sin límite de tiempo;
  - el texto de la Agenda, del aviso al cliente y de la nota del dueño.
- **Reversibilidad:**
  - volver al código anterior funciona sin `rollback.sql`: deja de ofrecer la marca, y los teléfonos marcados vuelven a poder pedir;
  - `rollback.sql` borra la columna, y con ella qué solicitudes se marcaron. Esas solicitudes se quedan canceladas y sin respuesta, así que tampoco cuentan en Explorar.
