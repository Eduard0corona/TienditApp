# ADR 0032: Elegir con quién en las citas en línea

- Estado: Aceptada
- Fecha: 2026-10-19

## Contexto

Las citas en línea ([ADR 0025](0025-online-booking.md)) ofrecían las horas libres del negocio con una capacidad para todo el negocio: «Citas a la vez», de 1 a 20. En una barbería, una estética o un consultorio con varias personas, eso no basta:

- el cliente no elegía quién lo atiende;
- la capacidad no sabía quién trabaja cada día, quién hace cada servicio ni qué citas tiene cada persona;
- la agenda ya guarda quién atiende cada cita (`StaffUserId`) y revisa que la misma persona no tenga dos citas cruzadas, pero las solicitudes en línea llegaban sin persona.

Las decisiones de la Fase 24J las tomé con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Quién aparece** (`OnlineBookingStaff`, una fila por cuenta y negocio).
   - El dueño o un administrador (`RequiresTenantManager` y el módulo de agenda) elige en Configuración › Quién atiende a cada persona del equipo que agenda en línea. Debe tener la membresía activa, la cuenta activa y no ser una cuenta de cliente.
   - Le da un nombre para clientes: de 1 a 40 caracteres (un emoji cuenta como uno), sin enlaces, correos ni teléfonos, con la misma regla que las reseñas. Por omisión es «Nombre I.».
   - La puede apagar sin perder lo que configuró.
   - Hasta 30 personas encendidas.
   - Cada cambio queda en la actividad del negocio (`OnlineBookingStaffUpdated`).
2. **Sus servicios.** Todos, incluidos los que el negocio agregue después, o los que el dueño elija (hasta 100) entre los que se agendan en línea, en `OnlineBookingStaffServices`. Borrar un servicio del catálogo lo quita de la lista.
3. **Su horario.**
   - Es el del negocio o uno propio, con el mismo formato (`OpeningHours`, hasta dos horarios por día).
   - Solo se agenda dentro del horario del negocio y del de la persona a la vez.
   - Los días especiales del negocio ([ADR 0012](0012-special-days.md)) valen para todos: cerrado no se agenda con nadie.
   - Las horas siguen yendo cada media hora desde cada apertura del negocio.
4. **El modo.**
   - Con al menos una persona encendida (y activa en el equipo), las horas salen de cada persona y «Citas a la vez» ya no se usa; la tarjeta de Citas en línea lo dice.
   - Sin nadie encendido, todo sigue como en el ADR 0025.
   - Solo se ofrecen los servicios que alguien hace.
5. **Cuándo está libre una persona** (`StaffSlots`, en el dominio).
   - Trabaja toda la cita.
   - Ninguna de sus citas abiertas se cruza con ella. A sus visitas a domicilio se les suma el traslado antes y después, como en la agenda ([ADR 0020](0020-home-visits.md)).
   - Quedan más personas libres que citas sin persona cruzadas en ese momento. Una cita sin persona ocupa a alguien, así que la regla es conservadora: puede dejar de ofrecer una hora que alguien fuera del equipo en línea iba a atender, pero no da dos citas a la misma persona.
6. **El cliente elige.**
   - Después del servicio, la página de citas pregunta «¿Con quién?»: «Sin preferencia» (por omisión) o cada persona que hace el servicio, con su nombre para clientes.
   - Con una persona, las horas son las suyas; sin preferencia, las de quien esté libre.
   - La persona se identifica con el id de su perfil de citas, nunca con el de su cuenta, y la página no recibe su nombre completo.
7. **La solicitud.**
   - Dentro del mismo candado del negocio (`IAppointmentBookingLock`), el servidor revisa otra vez que la persona elegida esté libre, o elige entre las libres a la que tiene menos citas ese día (empate: el nombre y luego el id).
   - La cita guarda a esa persona (`StaffUserId`) y su nombre completo (`StaffName`), como la agenda.
   - Una persona que ya no agenda ese servicio responde «Esa persona ya no agenda ese servicio en línea. Elige otra.», y la página ofrece ver con quien esté libre.
8. **El cliente lee quién lo atiende.**
   - El recibo, el seguimiento con el enlace y Mis citas dicen «Te atiende Ana L.»: el nombre para clientes de la persona asignada, también si el negocio la cambia después en la agenda.
   - Si la persona nueva no tiene nombre para clientes, no se dice nada.
9. **La agenda no cambia.** Una solicitud llega con su persona («Atiende: …»). Aceptar no revisa nada nuevo, y moverla o editarla ya revisaba a la persona.

## Consecuencias

- **Lo que logra:**
  - El cliente elige a su barbero, estilista o especialista, o deja que el negocio asigne.
  - Una persona no recibe dos citas a la misma hora por más que lleguen al mismo tiempo, y no se le ofrece una hora en que no trabaja o está de visita.
  - El negocio que no elige a nadie no ve ningún cambio.
- **Lo que limita:**
  - Sin vacaciones ni ausencias por fecha: una persona que falta un día se apaga ese día, o el negocio marca un día especial.
  - Sin foto de la persona: la página muestra su inicial.
  - El horario propio va dentro de las horas del negocio; las horas siguen cada media hora desde su apertura (una persona que entra a las 9:15 empieza a las 9:30).
  - Las citas sin persona cuentan contra las personas del servicio, no contra todo el equipo.
  - Los avisos de WhatsApp no dicen quién atiende.
  - Un negocio con más de 30 personas que atienden no las ofrece a todas.
  - Dos dueños que configuran a la misma persona al mismo tiempo: el segundo recibe «Alguien más cambió a esta persona al mismo tiempo».
- **Reversibilidad:**
  - Volver a la versión anterior funciona sin tocar la base: no conoce las tablas, las citas en línea vuelven a usar «Citas a la vez» y las ya pedidas conservan a quién se asignaron.
  - `rollback.sql` de `20261019150436_AddOnlineBookingStaff` borra las dos tablas solo si ningún negocio eligió a nadie. En cuanto uno lo hizo, se detiene con el error 50025 sin cambiar nada, y solo queda corregir hacia adelante.
