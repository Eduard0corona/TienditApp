# ADR 0070: Tope de pedir y cancelar, Agenda por límite y sonido

- Estado: Aceptada en la Fase 29P (2026-11-06).
- Fecha: 2026-11-06
- Continúa:
  - [ADR 0069](0069-tell-the-business-what-customers-answer.md), «Lo que limita»: quien pide y cancela una y otra vez manda dos correos por vuelta.
  - [ADR 0066](0066-answer-requests-in-time.md), «Fuera de esta fase»: ordenar la Agenda por límite.
  - [ADR 0068](0068-tell-the-business-of-each-request.md), «Fuera de esta fase»: un sonido con cada solicitud.

## Contexto

Desde la Fase 29O, cada cita que su cliente cancela le llega al negocio por correo ([ADR 0069](0069-tell-the-business-what-customers-answer.md)). El tope de solicitudes por teléfono ([ADR 0025](0025-online-booking.md)) solo cuenta las que esperan respuesta. Por eso no frenaba a quien pide y cancela una y otra vez: cada vuelta manda dos correos, y solo lo acotaba el límite de peticiones por dirección IP.

La Agenda dice para cuándo responder cada solicitud a tiempo ([ADR 0066](0066-answer-requests-in-time.md)), pero «Por confirmar» las ordenaba por su hora. Una solicitud para la semana siguiente que vence hoy quedaba abajo de las de mañana.

La barra superior avisa cada solicitud y cada cancelación al momento ([ADR 0068](0068-tell-the-business-of-each-request.md)), pero en silencio. Los pedidos en línea ya tenían un sonido que cada dispositivo prende.

El dueño del producto pidió avanzar estos puntos, entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29P con esa autorización.

## Decisión

1. **Las canceladas cuentan en el tope por teléfono.**
   - Un teléfono no pide otra cita ni otra visita en un negocio si ya tiene 3 que esperan respuesta (como antes), o si llega a 3 contando las citas en línea que su cliente canceló en las últimas 24 horas (`OnlineBookingRules.CancelledCountFor`).
   - Cuenta cualquier cita en línea que su cliente canceló, desde su cuenta o desde la liga, en cualquier estado. La que rechaza o cancela el negocio no cuenta.
   - El teléfono se compara como número, con la clave que guarda cada solicitud al llegar (`RequestPhone`, [ADR 0067](0067-requests-that-count.md)): un celular escrito con el 1 de WhatsApp es el mismo. Antes se comparaba como se escribió.
   - Un solo mensaje para los dos casos: «Ya pediste varias citas desde este teléfono. Espera a que el negocio confirme las que tienes, o vuelve a intentarlo más tarde.». Así, quien escribe el teléfono de otra persona no sabe si esa persona canceló una cita en ese negocio.
2. **«Por confirmar» ordena por el límite.**
   - Con el filtro «Por confirmar» (de hoy en adelante), la Agenda pone primero la que vence antes: la hora que pidió el cliente o un día después de llegar, lo que pase primero ([ADR 0066](0066-answer-requests-in-time.md)). Las ya vencidas van arriba.
   - Las del equipo no tienen límite ([ADR 0067](0067-requests-that-count.md)): van al final, por su hora.
   - El resto de la Agenda sigue por hora, y las anteriores, de la más nueva a la más vieja.
   - Arriba de la lista dice: «Primero las que hay que responder antes; al final, las de tu equipo.».
   - El servidor ordena con el mismo límite que la proyección escribe para SQL; no hay parámetro nuevo en la API. Dos citas a la misma hora se desempatan por su id, en toda la Agenda, para que una página no repita ni salte ninguna.
3. **Un sonido con cada solicitud o cancelación.**
   - Cada navegador lo prende o lo apaga en la Agenda: «Sonido para solicitudes en línea», con su estado en `aria-pressed`. Va junto a «Nueva cita», que sigue siendo la acción principal.
   - Suena desde la barra superior, en cualquier pantalla que la tenga, con cada solicitud o cancelación de un cliente. Las del equipo no suenan.
   - Una solicitud suena con dos notas hacia arriba, como un pedido; una cancelación, con las mismas hacia abajo.
   - Como el de los pedidos, se guarda en el navegador, que lo deja sonar después del primer toque o tecla. Al prenderlo en la Agenda, la barra superior lo prende con el mismo toque, y las otras pestañas lo siguen. Mientras falta ese toque, la Agenda lo pide.
4. **Fuera de esta fase:**
   - Contar en el tope las canceladas de otros teléfonos de la misma persona.
   - Elegir el sonido o su volumen: llegó en la Fase 29Z ([ADR 0080](0080-quote-sound-and-device-tone.md)), para todos los sonidos del dispositivo.
   - Lo demás de las ADR [0066](0066-answer-requests-in-time.md), [0068](0068-tell-the-business-of-each-request.md) y [0069](0069-tell-the-business-what-customers-answer.md), «Fuera de esta fase».

## Consecuencias

- **Lo que logra:**
  - Quien pide y cancela una y otra vez desde un teléfono hace tres vueltas (seis correos) y la cuarta solicitud se rechaza, hasta que pasen 24 horas de la primera cancelación.
  - Quien responde ve primero la solicitud que se le acaba, y escucha cada una aunque esté en otra pantalla.
- **Lo que limita:**
  - **Un cliente de verdad** que cancela tres veces en un día espera hasta que pasen 24 horas de la primera para pedir otra en ese negocio. Es raro, y el mensaje lo dice.
  - **Quien cambia de teléfono** sigue pidiendo; lo acota el límite de peticiones por IP.
  - **El sonido** depende de que la barra superior esté conectada (no en el modo enfocado de Vender o Cocina) y de que el navegador haya recibido un toque o una tecla desde que se abrió.
  - **Es del navegador, no de la persona:** en un equipo compartido suena para cualquier negocio o persona que lo use, y cada pestaña abierta suena.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - las 24 horas y el tope de 3;
  - el orden de las del equipo;
  - el sonido de la cancelación y el texto del interruptor y de la nota.
- **Reversibilidad:** sin migración. Volver al código anterior quita el tope de las canceladas, el orden y el sonido; lo que cada dispositivo eligió se queda guardado en él.
