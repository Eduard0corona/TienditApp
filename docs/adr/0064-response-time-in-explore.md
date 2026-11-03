# ADR 0064: Cuánto tarda en responder cada negocio, en Explorar

- Estado: Aceptada en la Fase 29J (2026-11-03).
- Fecha: 2026-11-03
- Continúa: [ADR 0053](0053-home-visit-quotes.md) (oficios a domicilio), punto de «Después» «el tiempo de respuesta del negocio como criterio de orden en Explorar».

## Contexto

Un cliente que pide una visita a un oficio ([ADR 0053](0053-home-visit-quotes.md)) o una cita en línea ([ADR 0025](0025-online-booking.md)) manda una solicitud y espera a que el negocio la acepte o la rechace. Mientras espera no sabe si le van a responder en una hora o en tres días, y Explorar no le dice cuál de los oficios de su ciudad responde antes.

Explorar ordena dentro de cada grupo por nombre: primero los que reciben pedidos, agendan o reciben visitas, luego los abiertos y al final los cerrados. Dentro de un grupo, «Cerrajería Álvarez» va antes que «Plomería Zamora» solo por su nombre.

Hasta esta fase TienditApp no guardaba cuándo se respondió una solicitud: la cita cambia de estado, pero no queda la hora.

Las otras dos entradas de «Después» que quedan necesitan decisiones del dueño del producto: el anticipo por Mercado Pago (cuánto cobrar y qué pasa con un pago tardío, [ADR 0061](0061-quote-replies-from-link.md)) y el PDF de la cotización, que pide una biblioteca de PDF. La que se usaba como referencia, PDFsharp, pasó en su versión 6.5 a AGPL-3.0, que para un SaaS obliga a publicar el código. Esta fase toma la que no necesita esas decisiones.

Tomé las decisiones de la Fase 29J con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué se mide:** las solicitudes en línea, es decir, una cita agendada en línea (Fase 24D) o una visita pedida en línea (Fase 29B). Cuenta el tiempo desde que llega hasta que el negocio la acepta o la rechaza.
   - **La cita guarda cuándo se respondió** (`Appointment.RespondedAt`). Lo ponen `Accept` y `Decline`, una sola vez; un intento rechazado no lo pone.
   - **Una que el negocio deja pasar** (su hora empezó sin respuesta) cuenta como la más lenta.
   - **No cuentan:** una que todavía espera, una que el cliente canceló antes de la respuesta, una cita que agendó el equipo y las respondidas antes de esta fase, que no tienen la hora.
2. **Cómo se resume:** se toman las solicitudes de los últimos 90 días y, desde 3 (como las reseñas, [ADR 0030](0030-business-reviews.md)), se elige el tramo más corto que reúne al menos la mitad: menos de 1 hora, menos de 3 horas, menos de 12 horas, menos de un día, o más.
3. **Qué dice la tarjeta:**
   - Hasta un día: «Responde en menos de 1 hora», «… de 3 horas», «… de 12 horas» o «… de un día».
   - Más de un día, o con menos de 3 solicitudes: nada. Explorar no publica lo malo de un negocio; lo pone después.
   - La API tampoco manda «más de un día»: `responseTime` es nulo.
4. **Cómo ordena:** dentro de su grupo, primero los que responden antes. Uno sin suficientes solicitudes, o que no recibe solicitudes (un restaurante), va en medio, con los de menos de 12 horas; después sigue el nombre.
   - El grupo manda: uno abierto que no agenda va después de los que agendan, por pronto que responda.
   - «Cerca de mí» sigue por distancia: el tiempo de respuesta solo desempata.
   - Los productos y servicios de una búsqueda siguen el orden de Explorar, así que también lo toman.
5. **Dónde vive:**
   - **En la foto de Explorar,** que se arma cada 30 segundos como mucho. Una consulta entre negocios cuenta por tramo las solicitudes de cada negocio listado, sin leer quién las hizo; está en la lista de lecturas permitidas.
   - **La migración** `AddAppointmentRespondedAt` agrega la columna (NULL en las citas que existen) y un índice de las solicitudes en línea por su llegada. Su `apply.sql` va antes del merge.
6. **Fuera de esta fase:**
   - **Decirlo en la tienda, en la página de citas o en la solicitud de visita.**
   - **Mostrarle al negocio su propio tiempo** (la vista previa de su tarjeta en Configuración lo deja fuera).
   - **Contar las cotizaciones:** cuánto tarda en mandar la cotización después de la visita.
   - **Los pedidos en línea de los restaurantes.**

## Consecuencias

- **Lo que logra:**
  - El cliente sabe, antes de pedir, si el negocio suele responder pronto.
  - Explorar pone primero, dentro de cada grupo, a los que responden antes, en lugar del orden alfabético.
  - El negocio tiene una razón para responder sus solicitudes.
- **Lo que limita:**
  - **Empieza vacío:** las solicitudes respondidas antes de esta fase no tienen hora, así que la tarjeta no dice nada hasta que un negocio responde 3 desde ahora.
  - **Las horas son de reloj, no de su horario:** una solicitud de las 11 de la noche que se responde a las 9 de la mañana tardó 10 horas.
  - **Un cambio tarda** hasta 30 segundos en verse, como la foto de Explorar.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - los tramos y el «al menos la mitad»;
  - 90 días y 3 solicitudes;
  - nada en la tarjeta pasado un día;
  - el negocio sin suficientes solicitudes en medio del orden.
- **Reversibilidad:** volver a la versión anterior funciona sin tocar la base: Explorar deja de decirlo y ordena solo por nombre. El `rollback.sql` borra la columna y su índice y pierde las horas guardadas, sin cambiar ninguna cita.
