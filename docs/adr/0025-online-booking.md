# ADR 0025: Citas en línea

- Estado: Aceptada
- Fecha: 2026-10-18

## Contexto

La agenda (Fase 4C) solo la llenaba el negocio: alguien del equipo registraba cada cita que el cliente pedía por teléfono o por WhatsApp. El menú en línea (Fase 3) y Explorar ([ADR 0021](0021-business-directory.md)) ya llevaban clientes a una barbería, un consultorio o una estética, pero ahí solo podían ver sus servicios: para agendar tenían que escribir, esperar a que alguien revisara la agenda y acordar una hora.

Las decisiones del dueño para la Fase 24D-1:

- el cliente agenda desde el menú en línea o desde Explorar: elige el servicio, el día y una hora libre;
- las horas libres salen del horario del negocio, de la duración del servicio y de las citas que ya tiene;
- la cita queda «Por confirmar» hasta que el negocio la acepta, y el cliente recibe el aviso por WhatsApp;
- sin anticipo.

## Decisión

1. **La solicitud es una cita** (`Appointment`, con `AppointmentStatus.Requested = 5`, «Por confirmar»), no otra tabla.
   - Guarda de dónde vino (`Channel`: el negocio o en línea), la cuenta de cliente que la pidió si había sesión (`CustomerUserId`, con llave a `AspNetUsers`), la llave de la solicitud (`ClientRequestId`, única por negocio) y su enlace (`TrackingToken`, 43 caracteres como el de un pedido, único).
   - Ocupa su hora desde que se pide: otro cliente ya no la ve libre mientras el negocio decide.
   - Solo sale de «Por confirmar» por el agregado:
     - `Accept` la vuelve programada, también si su hora ya empezó (el cliente ya está ahí, y entonces no recibe el aviso de una cita agendada), y no si ya terminó (el negocio la mueve antes);
     - `Decline` la cancela y libera su hora;
     - `CancelByCustomer` la cancela antes de que empiece (también programada o confirmada) y guarda cuándo (`CancelledByCustomerAt`).
   - `ChangeStatus` no entra ni sale de «Por confirmar», y una solicitud no se cobra: `RecordSale` la rechaza hasta aceptarla. Cambiar el estado desde la agenda (programada o cancelada) acepta o rechaza.
   - El negocio puede moverla de hora antes de aceptarla. El aviso de WhatsApp dice entonces que sigue por confirmar.
2. **Las horas libres las decide el servidor** (`BookingSlots`, con `OnlineBookingRules`).
   - Cada media hora desde cada apertura del día en la zona horaria del negocio (`BusinessSchedule.OpeningInstantsOn`). El servicio termina antes del cierre, y una apertura que pasa de la medianoche cuenta.
   - La duración es la del servicio en el catálogo («Duración (min)», de 5 a 720), o una hora si no la tiene.
   - La primera hora es una hora después de ahora. Se agenda hasta 30 días adelante, una semana por consulta; una apertura del último día puede terminar después de la medianoche, y nada más.
   - Cada hora lleva la fecha en que empieza: una hora después de la medianoche, de una apertura del día anterior, dice su propio día («12:30 a. m. · Mar 6») y el resumen la fecha correcta.
   - Una hora está libre mientras las citas abiertas que se cruzan con ella (sin contar canceladas ni las que no llegaron) son menos que las que el negocio atiende a la vez (`TenantSettings.OnlineBookingCapacity`, de 1 a 20, 1 por omisión).
   - Los días especiales cuentan como en el menú ([ADR 0012](0012-special-days.md)): cerrado no ofrece horas, y con otro horario ofrece las de ese horario.
   - Se agendan los servicios disponibles del catálogo que se venden por hora, por sesión o por servicio, en una categoría activa (hasta 100).
3. **Cuándo un negocio agenda en línea** (`OnlineBookingAvailability`): el dueño las prendió (`TenantSettings.OnlineBookingEnabled`, apagadas por omisión) y además
   - tiene el módulo de agenda;
   - publicó su menú en línea;
   - su sucursal principal tiene local: las citas en línea son en el negocio, no a domicilio;
   - tiene horario semanal.

   La tarjeta «Citas en línea» de Configuración dice cuál falta. El menú y Explorar dicen si el negocio agenda (`BooksOnline`), y Explorar lista también a un negocio sin productos en su menú que agenda servicios.
4. **La solicitud** (`RequestPublicAppointmentCommand`, `POST /api/v1/public/menu/{slug}/appointments`).
   - Anónima, con el límite `public-order`, como un pedido. La sesión de una cuenta de cliente la deja en esa cuenta ([ADR 0022](0022-shopping-with-an-account.md)); la del equipo de un negocio pide como invitado.
   - Pide nombre, teléfono (el mismo normalizador de los pedidos) y notas opcionales.
   - Un teléfono deja hasta 3 solicitudes esperando a la vez en un negocio, contra el abuso; se cuentan otra vez dentro del candado, así que varias enviadas al mismo tiempo no lo pasan.
   - Va en una transacción con un candado por negocio (`IAppointmentBookingLock`, `sp_getapplock` de SQL Server): dos clientes que piden la última hora libre al mismo tiempo esperan su turno y solo uno la toma; el otro recibe «Esa hora ya no está libre. Elige otra.» y la página vuelve a leer las horas.
   - La llave de la solicitud hace que la misma petición enviada dos veces sea una cita. Las dos reciben el mismo recibo, también si llegan al mismo tiempo. Si la respuesta se pierde (sin conexión o un error del servidor), la página deja la misma solicitud lista para enviarla otra vez con su llave, en lugar de pedir otra hora, que sería otra cita.
   - El recibo dice el servicio, el día, la hora y la dirección, y lleva el enlace de la cita. El aviso de WhatsApp por la cola: «recibimos tu solicitud… Te avisamos en cuanto la confirmen.»
5. **El cliente sigue su cita.**
   - Con el enlace (`/cita#enlace`): como el de un pedido ([ADR 0023](0023-order-tracking.md)), va en el fragmento y en el cuerpo de un POST, nunca en una URL que guarde un servidor; abre una sola cita hasta 30 días después de su hora, sin datos del cliente, y uno desconocido, vencido o de un negocio inactivo responde igual (404). Con el límite `public-menu` para leerla y `public-order` para cancelarla.
   - Con su cuenta (`/mis-citas`): las próximas y las anteriores, en todos los negocios, solo las suyas.
   - En los dos, la cancela antes de que empiece, si la agendó en línea; una que agendó el negocio se cancela escribiéndole. La agenda dice «La canceló el cliente».
   - Una solicitud que nadie decidió y cuya hora ya empezó dice «No se confirmó».
6. **El negocio decide** en la Agenda.
   - Las solicitudes llevan «Por confirmar» y el chip «En línea», con Aceptar, Rechazar (con confirmación) y Editar para moverla antes.
   - Un aviso arriba de la lista cuenta las que esperan, y la campana y «Requiere atención» de Inicio también (`appointmentRequests`, urgentes las de hoy), con un enlace a la agenda filtrada.
   - Aceptar y rechazar piden `ManageAppointments` y el módulo de agenda. Prender las citas en línea y elegir cuántas a la vez piden ser dueño o administrador del negocio, y quedan en la actividad (`OnlineBookingUpdated`).
7. **Sin anticipo:** el negocio cobra la cita en Vender como siempre, después de aceptarla.

## Consecuencias

- **Lo que logra:**
  - El cliente agenda a cualquier hora sin escribir, con horas que de verdad están libres.
  - El negocio no recibe dos citas en la misma hora por más que lleguen al mismo tiempo, y decide cada una.
  - Una solicitud ocupa su hora desde que se pide: el negocio no tiene que apurarse para que nadie más la tome.
- **Lo que limita:**
  - Las citas en línea son en el negocio: no se agenda a domicilio ([ADR 0020](0020-home-visits.md)).
  - El cliente no elige quién lo atiende: la capacidad es del negocio, no por persona. Desde la Fase 24J ([ADR 0032](0032-online-booking-staff.md)), un negocio que elige quién agenda en línea ofrece las horas de cada persona y el cliente la elige.
  - El paso de media hora, la hora de anticipación y los 30 días son fijos, no por negocio.
  - Una solicitud que nadie decide sigue ocupando su hora hasta que pasa; Inicio la marca urgente el mismo día.
  - Sin SQL Server el candado no hace nada: el índice único de la llave evita la doble petición, pero no dos clientes distintos en la última hora.
  - Sin anticipo, quien no llega deja la hora vacía: la agenda lo marca «No asistió» como antes.
  - Sin tiempo real: la agenda abierta ve una solicitud nueva al volver a cargar, como la campana.
  - Cualquiera puede pedir una cita con un teléfono que no es suyo, y el negocio le manda el aviso por WhatsApp: los únicos frenos son 3 solicitudes por teléfono y el límite de peticiones por IP, sin captcha ni verificación del teléfono.
  - Cuando el cliente cancela, el negocio no recibe aviso: lo ve en la agenda («La canceló el cliente»).
- **Reversibilidad:**
  - Volver a la versión anterior de la app funciona sin tocar la base, pero esa versión no conoce «Por confirmar»: mostraría las solicitudes con un estado sin nombre y sin Aceptar ni Rechazar. Antes de volver, el negocio decide las que esperan. El menú y Explorar dejan de ofrecer «Agenda tu cita».
  - `rollback.sql` de `20261017225015_AddOnlineBooking` quita las columnas, la llave y los índices solo si no hay citas agendadas en línea. En cuanto un cliente agendó una, se detiene con el error 50019 sin cambiar nada: borrar su canal y su enlace perdería de dónde vino cada cita, y solo queda corregir hacia adelante.
