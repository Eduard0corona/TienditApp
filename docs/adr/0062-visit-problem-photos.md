# ADR 0062: Las fotos de lo que pasa en la solicitud de visita

- Estado: Aceptada en la Fase 29H (2026-11-02).
- Fecha: 2026-11-02
- Continúa: [ADR 0053](0053-home-visit-quotes.md) (oficios a domicilio, la visita para cotizar), segundo punto de «Después».

## Contexto

Desde la Fase 29B ([ADR 0053](0053-home-visit-quotes.md), punto 11) el cliente pide la visita en línea y escribe qué pasa, en hasta 500 caracteres. Un plomero o un técnico entiende mejor el problema con una foto: la fuga, la mancha en la pared, la etiqueta del equipo. Hoy el cliente la manda por WhatsApp, separada de la solicitud, y el negocio la busca entre sus chats.

La ADR 0053 dejó las fotos para después porque «necesitan una subida pública validada, privada por negocio y con límite por solicitud, y esta etapa no abre subidas anónimas». Hasta esta fase, TienditApp no recibía archivos de alguien sin sesión: las fotos de un servicio ([ADR 0024](0024-service-photos.md)) y los comprobantes las sube el equipo del negocio.

Al preparar la fase, `dotnet restore` marcaba seis avisos de seguridad en `SixLabors.ImageSharp` 3.1.12, la librería que decodifica las imágenes. Tres eran nuevos y no estaban en la deuda técnica, y uno está en la lectura del perfil de color ICC que pueden traer los JPEG y PNG. La 3.2.0 los corrige, pero exige una clave de Six Labors para compilar en Release.

Tomé las decisiones de la Fase 29H con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Hasta 5 fotos por visita, opcionales.** El cliente las elige en la hoja de la solicitud, con su vista previa, y puede quitar las que no quiere antes de mandar.
   - **Se mandan después de guardar la solicitud**, una por una y con el enlace de la solicitud como llave ([ADR 0023](0023-order-tracking.md)), igual que el seguimiento. La solicitud no espera a las fotos: si una falla, la visita ya está pedida y el recibo dice cuál no se envió.
   - **Desde el enlace de la visita** (`/cita`) el cliente agrega más o reintenta las que fallaron, mientras la visita las reciba.
   - **Las fotos que van en camino siguen** si el cliente pasa del recibo al enlace: los dos muestran las mismas, y el navegador pregunta antes de cerrar la página mientras falta alguna. Reintentar está para las que fallaron por la conexión o el servidor; cuando el servidor rechaza una, la página lee otra vez cómo está la visita.
   - **El enlace lee la visita al abrirse** y toma solo las respuestas a las fotos que llegan mientras se muestra: una de antes puede ser vieja, porque el negocio pudo aceptar, rechazar o el cliente cancelar desde entonces.
   - **Mis citas se vuelve a leer** con cada foto que llega: en ese momento si está abierta, o la próxima vez que se abra.
2. **Cuándo se reciben:** solo una visita a domicilio que el cliente pidió en línea, no una cita en el local, una visita que agendó el equipo o el trabajo de una cotización ([ADR 0053](0053-home-visit-quotes.md), punto 17).
   - La visita debe estar por confirmar o agendada, y su hora no debe haber llegado.
   - **Cada visita tiene hasta 5 fotos.** El agregado lo revisa (`Appointment.AddProblemPhoto`), y la base guarda cada lugar una sola vez y ninguno después del quinto, así que dos fotos mandadas a la vez nunca hacen una sexta.
   - **Una foto no cambia la cita** ni toca su `rowversion`: mandarla nunca choca con que el negocio acepte o mueva la solicitud.
3. **La foto se revisa por lo que es:** JPG, PNG o WebP según su firma, nunca por su nombre ni por el tipo que dice el navegador, y de hasta 10 MB.
   - Se vuelve a codificar a WebP de 2048 px como máximo, sin sus metadatos (dónde se tomó, el teléfono).
   - Se guarda en el almacenamiento privado del negocio: `orders/{tenantId}/visit-problem/{yyyyMM}/{guid}.webp` ([ADR 0024](0024-service-photos.md), Fase 8A).
4. **La ruta:** `POST /api/v1/public/appointments/tracking/photos`, anónima, con `{ token, file }` como formulario. El enlace va en el cuerpo, nunca en la dirección.
   - **Límites:** su propio límite por IP (`public-upload`, 20 cada 10 minutos) y un tope del cuerpo de 10 MB más el formulario.
   - **A lo más dos fotos se decodifican a la vez** por instancia, porque una foto grande ocupa mucha memoria y cualquiera puede mandarla. Una tercera espera hasta 10 segundos; si no hay lugar, se rechaza (429) para mandarla otra vez.
   - **Lo que responde:** la cita como la ve el cliente, con cuántas fotos tiene (`problemPhotos`) y si recibe más (`canAddPhotos`), nunca las fotos. Un enlace desconocido o vencido no existe (404), igual que al abrirlo.
5. **Solo el negocio las ve:**
   - **En la Agenda**, una visita pedida en línea tiene «Lo que pasa · 2 fotos», que abre el texto del cliente y sus fotos.
   - **En la cotización de la visita**, «Lo que pasa» muestra lo mismo. Una visita que agendó el equipo no lo muestra, porque sus notas son del equipo.
   - **Se abren con la sesión** por `/api/v1/files/…`, para quien lleva la agenda o cotiza (`ManageAppointments` o `ManageOrders`, con el módulo de agenda o de órdenes de servicio).
   - **Solo abre un archivo que una foto del negocio guarda.** Otro negocio, otra área o un archivo suelto responden como si no existieran.
6. **El cliente ve cuántas mandó** («Mandaste 3 fotos de lo que pasa») y la vista previa mientras las sube, pero no las vuelve a ver desde el servidor.
7. **ImageSharp sigue en la 3.1.12.** La 29H probó la 3.2.0, que corrige los seis avisos, y el build en Release falla sin una clave de Six Labors. El 2026-11-03 se decidió quedarse en la 3.1.12 mientras ninguno sea alcanzable ([deuda técnica](../roadmap/technical-debt.md)): cinco están en TIFF, que no se decodifica, y el del perfil ICC solo se alcanza al leer sus entradas, que la app no pide.
   - **Mientras, un JPEG más grande que el tamaño en que se guarda se decodifica ya reducido**, que es casi toda foto de un teléfono, y toda foto con 8 bits por canal (un PNG de 16 bits ocupa la mitad).
   - **Un PNG o un WebP grande se decodifica completo** y luego se reduce: ImageSharp 3.1 no lo escala al leerlo. Lo acotan el tope de 50 megapíxeles y las dos fotos a la vez.
   - **Una foto más chica se guarda en su tamaño**, nunca ampliada.
   - **Lo demás que la cubre:** el tope de 50 megapíxeles, que se revisa antes de decodificar; el límite por IP; las 5 fotos por visita; y las dos fotos a la vez por instancia.
   - **Actualización del 2026-11-05:** ImageSharp pasó a la 3.2.0, que corrige los seis avisos, con la clave de Six Labors como el secreto `SIXLABORS_LICENSE_KEY` del CI, el despliegue y el build de Docker ([seguridad](../../SECURITY.md#dependencias)). Lo demás de este punto no cambia: la 3.2.0 tampoco escala un PNG o un WebP al leerlo.
8. **La migración** `20261102205836_AddAppointmentProblemPhotos`:
   - agrega la tabla `AppointmentProblemPhotos`: la cita (se borra con ella), el lugar de la foto de 0 a 4 (único por cita) y la llave de su archivo (única por negocio);
   - no cambia datos ni tablas que ya existen, y la versión anterior la tolera;
   - su rollback se detiene con el error 50056 en cuanto un cliente mandó una foto, con la tabla bloqueada hasta borrarla.
9. **Fuera de esta fase:**
   - **Que el cliente quite una foto o la vuelva a ver desde el servidor:** pediría abrir sus fotos con el enlace.
   - **Que el negocio borre una foto.**
   - **Avisar al negocio cuando llega una foto:** la ve al abrir la Agenda o la cotización.
   - **Llevar las fotos a la orden de servicio** al convertir la cotización: la orden tiene sus propias fotos del antes y el después ([ADR 0024](0024-service-photos.md)).
   - **Videos y achicar la foto en el teléfono antes de mandarla.**

## Consecuencias

- **Lo que logra:**
  - El negocio ve el problema antes de ir, junto a la solicitud, sin buscar en sus chats, y cotiza con la foto a la mano.
  - Las fotos no salen del negocio ni guardan dónde se tomaron. Otro negocio no las abre.
  - TienditApp recibe por primera vez archivos sin cuenta, con su firma revisada, su límite por IP y por visita, y el tope de memoria.
- **Lo que limita:**
  - **Quien tenga el enlace puede mandar fotos**, como con la cancelación ([ADR 0025](0025-online-booking.md)). Lo frenan las 5 por visita y el límite por IP.
  - **Un archivo puede quedar sin foto** si la base falla después de guardarlo, o si la visita se llena con otras justo entre la última revisión y el guardado. Queda en el almacenamiento privado sin que nadie lo abra; no hay limpieza, igual que con las fotos de un servicio.
  - **Reintentar puede guardar dos veces la misma foto** si el servidor la guardó y su respuesta se perdió: ocupa un lugar. Evitarlo pediría una llave por foto.
  - **«Lo que pasa» son las notas de la visita**, que el equipo puede editar en la Agenda. Lo que el negocio cambie se lee ahí como si lo hubiera escrito el cliente.
  - **Las fotos se guardan mientras exista la cita.** Ninguna se borra al cancelarse la visita.
  - **Una foto de más de 10 MB se rechaza en el teléfono** antes de mandarla. Una cámara con muchos megapíxeles puede pasar de ese tamaño.
  - **Un iPhone convierte su HEIC a JPEG** al elegir la foto desde el navegador, porque el campo pide JPG, PNG o WebP.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - 5 fotos por visita de hasta 10 MB;
  - el límite de 20 fotos cada 10 minutos por IP;
  - dos fotos decodificándose a la vez por instancia.
- **Reversibilidad:** volver a la versión anterior funciona sin `rollback.sql`. La solicitud y el enlace dejan de ofrecer las fotos, y las que ya se mandaron se quedan guardadas sin que el negocio las vea. El `rollback.sql` se detiene si quitarlo perdería las fotos.
