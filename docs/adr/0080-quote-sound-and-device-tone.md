# ADR 0080: El sonido de las respuestas a cotizaciones y el tono y volumen de cada dispositivo

- Estado: Aceptada en la Fase 29Z (2026-11-08).
- Fecha: 2026-11-08
- Continúa:
  - [ADR 0072](0072-quote-answers-live-and-customer-order-cancellation.md), «Fuera de esta fase»: un sonido para las respuestas a cotizaciones.
  - [ADR 0070](0070-cancellations-cap-agenda-order-and-sound.md), «Fuera de esta fase»: elegir el sonido o su volumen.

## Contexto

Cuatro cosas suenan en un dispositivo que lo prende: los pedidos en línea (en Pedidos), las comandas (en Cocina), las solicitudes y cancelaciones de citas (en la Agenda, desde la Fase 29P) y, desde la 29X, el pedido que lleva 15 minutos sin confirmar. Las respuestas a cotizaciones se avisan en pantalla desde la Fase 29R, pero en silencio: entonces se decidió que una respuesta no urge como un pedido.

Todos los sonidos son el mismo: dos notas hacia arriba (hacia abajo para una cancelación), al mismo volumen. Quien tiene la tableta en una cocina ruidosa no lo oye, y quien la tiene en un consultorio lo oye demasiado.

El dueño del producto pidió avanzar estos puntos (el 6 y el 7), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 29Z con esa autorización. No lleva migración ni cambia el contrato.

## Decisión

1. **Un sonido para las respuestas a cotizaciones.**
   - Cada navegador lo prende o lo apaga en Cotizaciones: «Sonido para respuestas», con su estado en `aria-pressed`. Va junto a «Nueva cotización».
   - Suena desde la barra superior, en cualquier pantalla que la tenga, con cada respuesta que un cliente manda desde su enlace: una aceptada suena hacia arriba y una rechazada hacia abajo, como una cita pedida y una cancelada.
   - Como los demás, se guarda en el navegador y suena después del primer toque o tecla. Mientras falta ese toque, Cotizaciones lo pide.
   - Empieza apagado: quien no lo prende no oye nada nuevo.
2. **El tono y el volumen de cada dispositivo.**
   - Son del dispositivo, no de cada sonido: los mismos para pedidos, solicitudes, cotizaciones y cocina. Cada sonido se sigue prendiendo y apagando aparte.
   - **Tonos:** «Dos notas» (el de siempre), «Campana» (dos notas más largas y brillantes), «Timbre» (tres notas) y «Suave» (dos notas graves y lentas). Una cancelación suena con las notas del tono al revés.
   - **Volumen:** de 10 a 100, de diez en diez. 50 es el de siempre.
   - Mientras el dispositivo no elige nada, todo suena como antes.
   - **Dónde:** un botón «Tono y volumen de los sonidos» junto a cada interruptor de sonido (Pedidos, Cocina, Agenda y Cotizaciones) abre un diálogo con los tonos, el volumen y «Probar». Elegir un tono lo hace sonar.
   - Se guarda en el navegador. Las otras pestañas lo siguen, y un valor que esta versión no conoce suena como antes.
3. **Fuera de esta fase:**
   - un tono o volumen distinto para cada sonido;
   - subir un sonido propio;
   - guardar la elección en la cuenta para que siga a la persona en otros dispositivos;
   - notificaciones del navegador o del teléfono con la pestaña cerrada.

## Consecuencias

- **Lo que logra:**
  - Quien trabaja las cotizaciones oye la respuesta del cliente sin mirar la pantalla.
  - Cada dispositivo suena como le sirve a su lugar: fuerte en una cocina, suave en un consultorio.
- **Lo que limita:**
  - **Es del navegador:** otra persona en el mismo dispositivo oye lo mismo, y un navegador nuevo empieza con el de siempre.
  - **Depende de la barra superior**, como los demás: en el modo enfocado de Vender o de Cocina no suenan las respuestas ni las solicitudes. La Cocina sí suena con sus comandas.
  - **El volumen es relativo** al del dispositivo: 100 no sube el del sistema.
  - **Los tonos se generan en el navegador** (Web Audio), sin archivos: no hay sonidos grabados.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - que el sonido de las respuestas empiece apagado y que una rechazada suene hacia abajo;
  - que el tono y el volumen sean del dispositivo y no de cada sonido;
  - los cuatro tonos, la escala del volumen y los textos.
- **Reversibilidad:** sin migración. Volver al código anterior quita el sonido de las respuestas y vuelve al tono y volumen de siempre; lo que cada dispositivo eligió se queda guardado en él.
