# ADR 0082: El correo del pedido que cancela el cliente, cuánto antes recordar una solicitud y el índice de los pedidos en línea

- Estado: Aceptada en la Fase 30B (2026-11-08).
- Fecha: 2026-11-08
- Continúa:
  - [ADR 0072](0072-quote-answers-live-and-customer-order-cancellation.md), «Fuera de esta fase»: avisar por correo cuando el cliente cancela un pedido en línea.
  - [ADR 0075](0075-answer-reminder.md), «Fuera de esta fase»: que cada negocio elija cuánto antes recordar una solicitud, o que no quiera el recordatorio.
  - [ADR 0074](0074-order-confirm-time.md) y [ADR 0079](0079-order-ready-time.md): un índice filtrado de los pedidos en línea para la foto de Explorar, que pedía migración.

## Contexto

- **El pedido que cancela el cliente.** Desde la Fase 29R el cliente cancela su pedido en línea mientras el negocio no lo acepta, y Pedidos lo avisa en pantalla ([ADR 0072](0072-quote-answers-live-and-customer-order-cancellation.md)). Entonces se decidió no mandar correo: quien recibe pedidos en línea los atiende con la app abierta. Pero quien no está frente a la pantalla, o la cerró, no se entera y puede empezar a prepararlo.
- **Cuánto antes recordar.** Desde la Fase 29U el negocio recibe un correo una hora antes del límite de cada solicitud que sigue sin respuesta ([ADR 0075](0075-answer-reminder.md)). La hora era fija: a un consultorio que revisa la Agenda dos veces al día no le alcanza, y a uno que contesta al momento le sobra el correo.
- **El índice.** La foto de Explorar lee los pedidos en línea de 90 días de las cocinas y tiendas listadas para contar cuánto tardan en confirmarlos (Fase 29T) y en tenerlos listos (Fase 29Y). Los busca con el índice de todos los pedidos de cada negocio, también los de mostrador y los de las mesas. Las dos fases dejaron anotado que un índice filtrado de los pedidos en línea podía ayudar, y que pedía migración.

El dueño del producto pidió avanzar estos puntos (el 9, el 10 y el 13), entre otros, sin esperar su respuesta a cada decisión. Tomé las de la Fase 30B con esa autorización. Es la única fase de la lista que lleva migración: su `apply.sql` se aplica antes del merge.

## Decisión

1. **El correo del pedido que cancela el cliente.**
   - Cuando un cliente cancela su pedido en línea antes de que lo acepten, le llega un correo a cada persona que lo eligió: «Pedido cancelado en Cocina Luna: ORD-000123 por $120.00».
   - Dice quién lo canceló, el folio, el total y que ya no hay que prepararlo, con el enlace a Pedidos. Nunca el teléfono ni la dirección del cliente, ni lo que pidió línea por línea: eso se queda en el pedido.
   - **A quién:** a quien puede confirmar los pedidos (el permiso de Pedidos, en un negocio que vende y no está suspendido), activo en el equipo, con su cuenta activa y su correo confirmado. Como los otros avisos, los dueños lo reciben mientras no eligen; los demás, cuando lo prenden.
   - **Dónde se elige:** en Mi perfil, «Pedidos cancelados», y en Equipo, como los otros avisos ([ADR 0071](0071-team-notices-and-orders-cleanup.md)). El del dueño solo desde su perfil. Si alguien más lo cambia, el correo de la Fase 30A lo dice ([ADR 0081](0081-notice-changes-fake-watch-and-account-cap.md)).
   - Sale por el outbox, un mensaje por persona, y se reintenta solo. Al mandarlo se comprueba otra vez que el pedido siga cancelado por su cliente, hace un día o menos, y que la persona lo siga recibiendo y pueda confirmar pedidos.
   - El negocio que cancela un pedido no se manda correo a sí mismo.
2. **Cada negocio elige cuánto antes recordar una solicitud.**
   - En Configuración › Citas en línea, «Recordatorio de solicitudes sin responder»: media hora, una, dos o tres horas antes del límite, o «No recordarme».
   - Una hora mientras no elige, como desde la 29U.
   - Sigue la regla de la 29U con la anticipación elegida: no se recuerda la solicitud cuyo límite llegó antes de dos anticipaciones desde que llegó. Con «No recordarme» solo llega el correo de cada solicitud al llegar.
   - Cambiarla vale para las solicitudes que todavía no se recordaron. Queda en la bitácora con el resto de las citas en línea.
   - La base solo acepta esas opciones (`CK_TenantSettings_AnswerReminderMinutes`), como el dominio.
3. **Los índices: se midieron y no cambian.**
   - **No se agrega el índice filtrado de los pedidos en línea.** Se probó uno, por negocio y fecha, con el teléfono y el estado, solo de los pedidos del menú en línea de clientes. Se midió en SQL Server, sin otra carga, con la foto completa de Explorar y una cocina con 25,000 y 100,000 pedidos en 90 días (uno de cada diez en línea):
     - la lectura de los pedidos listos baja de 85 a 35 ms con 25,000 pedidos, y de 556 a 178 ms con 100,000;
     - la del tiempo de confirmar sube de 0.4 a 2.2 s con 25,000, y de 1.8 a 28 s con 100,000. Con el índice, SQL Server cambia el plan de sus dos subconsultas del historial, y con 100,000 pedidos la foto llega a los 30 segundos de la petición.
     - Las dos lecturas buscan los mismos pedidos, así que las dos usan el índice: no se puede dar solo a la de los pedidos listos.
   - Mejorar la del tiempo de confirmar pide otra forma de la consulta (como la de los pedidos listos, que une el historial en vez de buscarlo por pedido) y medirla otra vez; queda para otra fase.
     - *Hecho en la Fase 30E:* la lectura une el historial una vez y dice, por cliente, cuándo llegó el último pedido de cada tramo. Sin índice tarda unos 85 a 90 ms con 25,000 pedidos y 200 a 450 ms con 100,000 (antes 370 ms y 0.8 s en la misma máquina). Con el índice filtrado ya no empeora: 35 a 50 ms y 170 a 200 ms, en tres corridas. Agregarlo sigue pidiendo migración.
   - **Se queda** `IX_Appointments_OnlineRequests_CreatedAt` de la 29J: desde la 29M Explorar ya no lo usa, pero el recordatorio de la 29U busca con él las solicitudes que esperan ([ADR 0075](0075-answer-reminder.md)).
   - El detalle de la medición está en [la estrategia de pruebas](../development/testing-strategy.md).
4. **Fuera de esta fase:**
   - el correo de un pedido que el cliente cancela por teléfono o por WhatsApp (lo cancela el negocio);
   - una anticipación distinta por persona o por servicio;
   - recordar también los pedidos en línea sin confirmar por correo (se avisan en pantalla desde la 29X);
   - reescribir la lectura del tiempo de confirmar para que un índice filtrado la ayude.

## Consecuencias

- **Lo que logra:**
  - Quien no está frente a Pedidos se entera de que ya no tiene que preparar un pedido.
  - Cada negocio recibe el recordatorio cuando le sirve, o ninguno.
  - Queda medido que el índice filtrado no conviene con la lectura de hoy, en vez de agregarlo a ciegas.
- **Lo que limita:**
  - **Un correo más por pedido cancelado**, como con las citas: quien pide y cancela una y otra vez manda un correo por vuelta. Lo acota el límite por IP de pedir y de cancelar.
  - **El correo llega aunque Pedidos ya lo avisó en pantalla:** quien no lo quiere lo apaga en Mi perfil.
  - **Un recordatorio de dos o tres horas no llega** a las solicitudes cuyo límite cae antes de cuatro o seis horas desde que llegaron: basta el correo de cuando llegaron.
  - **La foto de Explorar sigue leyendo los pedidos en línea por el índice de todos los pedidos de cada negocio**, con las ventas de mostrador: unos 0.4 s con 25,000 pedidos y 1.8 s con 100,000 en la lectura del tiempo de confirmar.
- **Decisión por omisión** (sin preferencia expresa del dueño):
  - que el correo llegue a los dueños mientras no eligen, como los otros avisos;
  - el día después del cual ya no se manda;
  - las cuatro anticipaciones y que la hora siga mientras no se elige;
  - no agregar el índice filtrado de los pedidos y conservar el de las solicitudes;
  - los textos.
- **Reversibilidad:** `rollback.sql` borra las dos columnas y su restricción, sin tocar personas, negocios ni pedidos. Volver al código anterior deja de mandar el correo y vuelve a recordar una hora antes.
