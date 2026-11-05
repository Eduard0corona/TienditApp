# ADR 0064: Cuánto tarda en responder cada negocio, en Explorar

- Estado: Aceptada en la Fase 29J (2026-11-03).
- Fecha: 2026-11-03
- Continúa: [ADR 0053](0053-home-visit-quotes.md) (oficios a domicilio), punto de «Después» «el tiempo de respuesta del negocio como criterio de orden en Explorar», y [ADR 0021](0021-business-directory.md) (el orden de Explorar).

## Contexto

Un cliente que pide una visita a un oficio ([ADR 0053](0053-home-visit-quotes.md)) o una cita en línea ([ADR 0025](0025-online-booking.md)) manda una solicitud y espera a que el negocio la acepte o la rechace. Mientras espera no sabe si le van a responder en una hora o en tres días, y Explorar no le dice cuál de los oficios de su ciudad responde antes.

Explorar pone primero a los que reciben pedidos, agendan o reciben visitas, luego a los abiertos y al final a los cerrados, y dentro de cada grupo los ordena por nombre. En «Hogar y reparaciones», «Cerrajería Álvarez» va antes que «Plomería Zamora» solo por su nombre.

Hasta esta fase TienditApp no guardaba cuándo se respondió una solicitud: la cita cambia de estado, pero no queda la hora.

Las otras dos entradas de «Después» que quedan necesitan decisiones del dueño del producto: el anticipo por Mercado Pago (cuánto cobrar y qué pasa con un pago tardío, [ADR 0061](0061-quote-replies-from-link.md)) y el PDF de la cotización, que necesita elegir una biblioteca de PDF y su licencia. Esta fase toma la que no las necesita.

Tomé las decisiones de la Fase 29J con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué se mide:** las solicitudes en línea, es decir, una cita agendada en línea (Fase 24D) o una visita pedida en línea (Fase 29B). Cuenta el tiempo desde que llega hasta que el negocio la acepta o la rechaza antes de la hora que pidió el cliente.
   - **La cita guarda cuándo se respondió** (`Appointment.RespondedAt`). Lo ponen `Accept` y `Decline`, desde la Agenda o desde su estado, una sola vez; un intento rechazado no lo pone.
   - **Y la hora que pidió el cliente** (`Appointment.RequestedStartAt`), que no cambia cuando el negocio mueve la cita: dejarla pasar y moverla después no la vuelve rápida.
   - **Las más lentas** son las que ya esperaron un día o más, o ya no sirven:
     - la que se respondió cuando la hora que pidió el cliente ya había empezado;
     - la que el negocio dejó pasar: esa hora empezó sin respuesta, aunque luego la haya movido;
     - la que lleva un día o más sin respuesta, aunque su hora sea días después;
     - la que el cliente canceló después de esperar un día sin respuesta, si la creó esta versión (guarda la hora pedida): una de antes pudo haberse aceptado sin guardar cuándo.
   - **No cuentan todavía:** una que espera desde hace menos de un día, para una hora que no ha llegado, y una que el cliente canceló antes de un día.
   - **No cuentan:** una cita que agendó el equipo, y lo que se decidió antes de la primera respuesta que TienditApp guardó (el despliegue de la Fase 29J).
     - Cada solicitud se decide en un momento: la rápida cuando se respondió; la lenta cuando llegó a su límite sin respuesta (la hora que pidió el cliente o un día después de llegar, lo que pase primero).
     - Antes de esa primera respuesta, y mientras corre una versión anterior, una respuesta no guarda su hora y no cuenta. Por eso tampoco cuenta una que llegó a su límite antes, aunque se responda después: contar solo esas haría a un negocio más lento de lo que fue, y responder lo que quedó atrasado lo haría más lento todavía.
2. **Cómo se resume:** se toman las solicitudes de los últimos 90 días y, desde 3 (como las reseñas, [ADR 0030](0030-business-reviews.md)), se elige el tramo más corto que reúne al menos la mitad: menos de 1 hora, menos de 3 horas, menos de 12 horas, menos de un día, o más. «Menos de» es estricto: una hora exacta ya no es «menos de una hora».
3. **Qué dice la tarjeta:**
   - Con menos de un día: «Suele responder en menos de una hora», «… de 3 horas», «… de 12 horas» o «… de un día», después de lo que dice de la visita o la entrega.
   - Con un día o más, con menos de 3 solicitudes, o cuando el negocio ya no agenda en línea ni recibe visitas: nada. Explorar no publica lo malo de un negocio.
   - La API tampoco manda «un día o más»: `responseTime` es nulo.
4. **Cómo ordena:** en la página de una categoría, dentro de cada grupo, primero los que responden antes. Uno sin suficientes solicitudes, o que no recibe solicitudes, va en medio, con los de menos de 12 horas: antes de los de menos de un día y de los de un día o más. Los empates siguen por nombre.
   - El grupo manda: uno abierto que no agenda va después de los que agendan, por pronto que responda.
   - **La lista de todos los negocios y «Cerca de mí» no cambian.** La lista mezcla restaurantes que no reciben solicitudes con salones y oficios, y no tiene sentido compararlos; «Cerca de mí» sigue por distancia.
   - **Los productos y servicios de una búsqueda** siguen el orden general de Explorar, también en una categoría.
5. **Dónde vive:**
   - **En la foto de Explorar,** que se arma cada 30 segundos como mucho. Una lectura entre negocios, solo de los negocios listados, busca la primera respuesta guardada y cuenta por tramo las solicitudes de cada uno; lee la llegada, la respuesta, el estado, la hora pedida y la actual y la cancelación, nunca quién la hizo. Está en la lista de lecturas permitidas.
   - **Si esa consulta falla,** Explorar sigue sin el dato hasta la siguiente foto: nadie dice cuánto tarda y todos van en medio.
   - **La migración** `AddAppointmentRespondedAt` agrega las dos columnas (NULL en las citas que existen) y un índice de las solicitudes en línea por su llegada, con lo que lee la consulta. Su `apply.sql` va antes del merge. Una solicitud de antes, sin la hora pedida, se mide con su hora.
6. **Fuera de esta fase:**
   - **Decirlo en la tienda, en la página de citas o en la solicitud de visita:** llegó en la Fase 29K ([ADR 0065](0065-response-time-outside-explore.md)).
   - **Mostrarle al negocio su propio tiempo:** llegó en la Fase 29K, en «Cómo te ven en TienditApp».
   - **Contar las cotizaciones:** cuánto tarda en mandar la cotización después de la visita.
   - **Los pedidos en línea de los restaurantes.**
   - **Defenderse de solicitudes falsas** (ver «Lo que limita»).

## Consecuencias

- **Lo que logra:**
  - El cliente sabe, antes de pedir, si el negocio suele responder pronto.
  - En cada categoría, Explorar pone primero, dentro de cada grupo, a los que responden antes, en lugar del orden alfabético.
  - El negocio tiene una razón para responder sus solicitudes.
- **Lo que limita:**
  - **Empieza vacío:** cuenta desde la primera respuesta guardada, así que la tarjeta no dice nada hasta que un negocio tiene 3 solicitudes desde el despliegue.
  - **Una vuelta a la versión anterior** responde sin guardar la hora: lo que se deje pasar mientras corre sí cuenta, y lo que responda no.
    - Una solicitud que creó esta versión y que la anterior aceptó a tiempo cuenta como lenta si su cliente la cancela después de su límite: con lo que guarda la cita no se distingue de una que seguía sin respuesta.
  - **Las horas son de reloj, no de su horario:** una solicitud de las 11 de la noche que se responde a las 9 de la mañana tardó 10 horas.
  - **Se puede manipular:** cualquiera manda una solicitud con cualquier teléfono, con el tope por teléfono y por IP de la [ADR 0025](0025-online-booking.md).
    - Un negocio puede pedirse citas a sí mismo y aceptarlas al momento.
    - Un competidor puede mandarle solicitudes que nadie va a atender.
  - **Un cambio tarda** hasta 30 segundos en verse, como la foto de Explorar.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - los tramos y el «al menos la mitad»;
  - 90 días y 3 solicitudes;
  - las más lentas;
  - nada en la tarjeta con un día o más;
  - el orden solo en las categorías, con el negocio sin suficientes solicitudes en medio.
- **Reversibilidad:** volver a la versión anterior funciona sin tocar la base: Explorar deja de decirlo y cada categoría ordena por nombre. El `rollback.sql` borra las columnas y su índice y pierde las horas guardadas, sin cambiar ninguna cita.
