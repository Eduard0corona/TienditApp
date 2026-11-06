# ADR 0068: Avisarle al negocio de cada solicitud

- Estado: Aceptada en la Fase 29N (2026-11-05).
- Fecha: 2026-11-05
- Continúa: [ADR 0066](0066-answer-requests-in-time.md) (responder a tiempo), el punto de «Fuera de esta fase» que pedía avisar por WhatsApp, por correo o en tiempo real. Toma también el de la [ADR 0065](0065-response-time-outside-explore.md) que pedía avisarle al negocio cuando tiene solicitudes sin responder.

## Contexto

Desde la Fase 29J, Explorar dice en cuánto suele responder cada negocio. Desde la 29L, la Agenda dice para cuándo responder cada solicitud y la campana marca las que vencen hoy ([ADR 0066](0066-answer-requests-in-time.md)).

Pero el negocio solo se entera de una solicitud si abre la app. El WhatsApp que sale al llegar una solicitud es para el cliente. La campana se lee cada minuto, y solo mientras la app está abierta.

El dueño del producto eligió este punto entre los que no necesitan decisiones suyas: avisar en tiempo real dentro de la app y por correo al dueño.

Tomé las decisiones de la Fase 29N con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Cuándo se avisa.**
   - Cada solicitud en línea o de visita avisa que llegó (`AppointmentRequestedEvent`, que levantan `Appointment.RequestOnline` y `RequestHomeVisit`).
   - El evento lleva solo ids, si es visita y si vino del equipo ([ADR 0067](0067-requests-that-count.md)). El cliente se queda en la cita.
2. **En la app, al momento.**
   - Un hub nuevo, `/hubs/appointments`, para quien atiende la Agenda: el mismo permiso que pide `/api/v1/appointments` (`ManageAppointments`), en un negocio con el módulo de citas. Cada conexión entra solo al grupo de su negocio, y se cierra si pierde la sesión o el permiso.
   - Le llega `AppointmentRequested` con el servicio, la hora y si es visita o del equipo. No viaja el nombre, el teléfono ni las notas del cliente.
   - La barra superior se conecta en cualquier pantalla que la tenga: no en el modo enfocado de Vender o Cocina, ni con el negocio suspendido (el servidor lo rechazaría). Con cada solicitud:
     - la campana la lee al momento;
     - la Agenda abierta recarga la lista sin indicador de carga;
     - sale un aviso: «Nueva solicitud de cita» (o «de visita»), con el servicio y la hora. Las del equipo no lo muestran.
   - Al reconectar, la campana y la Agenda leen otra vez: lo que llegó sin conexión no se repite. La campana se sigue leyendo cada minuto.
   - Una conexión que el servidor cierra apenas la acepta (le quitaron el permiso, o el negocio se suspendió con la página abierta) cuenta como un intento fallido: los reintentos esperan cada vez más, hasta 30 segundos, en lugar de volver cada segundo. Vale para todos los hubs.
3. **Por correo.**
   - **A quién:**
     - a los miembros activos del negocio que eligieron recibirlos o, mientras no elijan, a los dueños;
     - con la cuenta activa y su correo confirmado;
     - que pueden atender la solicitud: el permiso de la Agenda, en un negocio con citas que no está suspendido. Es lo que la API les pediría para aceptarla.
   - **Cuándo no sale:** si es del equipo, si ya se respondió, si llegó hace más de un día o si ya llegó su hora.
   - **Cómo sale:**
     - por el outbox, con el proveedor de correo de la plataforma;
     - cada miembro recibe el suyo como un mensaje aparte del outbox (`AppointmentRequestEmailEvent`). Se guardan con el registro de que se repartieron, así que cada correo sale y se reintenta solo: uno que falla no manda otra vez los de los demás;
     - antes de mandarlo se revisa otra vez que la solicitud siga por confirmar y que el miembro los siga recibiendo.
   - **Qué dice:**
     - asunto: «Nueva solicitud de cita en {negocio}: {servicio}, {día} a las {hora}» («a la» a la una), o «de visita»;
     - quién la pidió y qué, para cuándo responderla para que cuente como respondida a tiempo ([ADR 0066](0066-answer-requests-in-time.md)), y la liga a la Agenda;
     - abajo: por qué le llega y que puede dejar de recibirlos en Mi perfil;
     - las horas en la zona del negocio. Los nombres escritos por el negocio y por el cliente van codificados;
     - nunca el teléfono, las notas, la dirección ni las fotos del cliente.
4. **Cada quien elige.**
   - `TenantUser.RequestEmails` (migración `20261105171726_AddTenantUserRequestEmails`): sí, no o sin elegir (`NULL`). Mientras no elija, solo a los dueños les llegan.
   - Se elige en **Mi perfil › Avisos de solicitudes**, por negocio y solo para uno mismo (`GET` y `PUT /api/v1/appointments/request-emails`, con el permiso de la Agenda). Dice a qué correo llegan y si falta confirmarlo.
   - Nadie elige por otro. Quien no es del equipo (el administrador de la plataforma) no tiene qué elegir.
5. **Fuera de esta fase:**
   - **Avisar al dueño por WhatsApp:** necesita una plantilla aprobada por Meta y cuesta cada conversación. Lo decide el dueño del producto.
   - **Las notificaciones del navegador o del teléfono** (Web Push): necesitan llaves de la plataforma y que cada persona dé permiso.
   - **Un sonido con cada solicitud**, como el de los pedidos por confirmar. Llegó en la Fase 29P ([ADR 0070](0070-cancellations-cap-agenda-order-and-sound.md)), también con cada cancelación.
   - **Recordar una solicitud antes de su límite.**
   - **Avisar otras cosas por correo:** que el cliente canceló, o la respuesta a una cotización. Llegó en la Fase 29O ([ADR 0069](0069-tell-the-business-what-customers-answer.md)): la cita cancelada también avisa en la app.
   - **Que el dueño elija por su equipo.**
   - Lo demás de la [ADR 0066](0066-answer-requests-in-time.md), «Fuera de esta fase».

## Consecuencias

- **Lo que logra:**
  - El dueño se entera de cada solicitud aunque no tenga abierta la app, y quien tiene abierta la app la ve al momento.
  - Cada quien decide si le llegan, sin que nadie más lo decida.
  - El correo no reparte los datos del cliente: quien la atiende los ve en la Agenda.
- **Lo que limita:**
  - **Un competidor que manda muchas solicitudes** también manda muchos correos al dueño. Lo acotan el tope de solicitudes por teléfono ([ADR 0025](0025-online-booking.md)) y el límite de peticiones.
  - **El nombre del cliente** lo escribe quien pide. Va codificado, pero un texto engañoso llega en el correo, como llega a la Agenda.
  - **Un negocio que apaga el módulo de citas** con la conexión abierta la conserva, pero sin módulo no le llegan solicitudes.
  - **Un correo que el proceso alcanzó a mandar** antes de detenerse sale otra vez al reintentar, como los demás del outbox.
  - **Mientras se publica:** el worker de la versión anterior no conoce los eventos nuevos y aparta los que tome (`Unknown event type`). Esos correos no salen; la campana y la Agenda sí los muestran.
  - **El aviso en pantalla** depende de que la conexión esté arriba: sin ella, la campana lo dice en menos de un minuto. En el modo enfocado de Vender o Cocina no hay barra superior: ni aviso ni campana.
  - **A los dueños les llegan desde el primer día:** quien no los quiera los apaga en Mi perfil.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - a quién le llegan sin elegir: a los dueños;
  - un correo por persona y por solicitud, sin resumen;
  - no mandar el de una solicitud de más de un día;
  - el texto del aviso, del correo y de Mi perfil.
- **Reversibilidad:**
  - la migración agrega una columna que admite `NULL`, sin valor por omisión, y no cambia datos; la versión anterior funciona sin `rollback.sql`;
  - volver al código anterior deja de avisar, y lo que cada quien eligió se queda guardado;
  - `rollback.sql` borra la columna y, con ella, lo que cada quien eligió.
