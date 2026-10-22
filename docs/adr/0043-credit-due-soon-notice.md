# ADR 0043: El aviso antes de vencer del fiado

- Estado: Aceptada
- Fecha: 2026-10-22

## Contexto

La Fase 26C ([ADR 0035](0035-customer-credit-term-and-reminders.md)) le recuerda por WhatsApp al cliente lo que ya venció. Dejó fuera «el aviso antes de vencer»:

- El negocio se entera de lo vencido después de que pasa. Para entonces, el cliente ya se atrasó, y el primer mensaje que recibe es un cobro.
- Un aviso unos días antes («el lunes vence $200») ayuda a que pague a tiempo, y se lee como un recordatorio amable, no como un cobro.

La cola de mensajes (`OutgoingMessage`), las plantillas, las bajas (BAJA), la ventana de envío y la regla de lo vencido (`CreditTerm`) ya existen.

Tomé las decisiones de la Fase 26E con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Un uso nuevo de mensaje** (`MessagePurpose.CreditDueSoon`, 4), aparte del recordatorio de lo vencido.
   - Tiene su propio texto, sus plantillas y su lista en el registro de mensajes.
   - Sus variables son `{nombre}`, `{negocio}`, `{monto}` (lo que debe en total), `{porvencer}` (lo que vence ese día) y `{fecha}` (el próximo día límite).
   - No usa `{vencido}`: el aviso es para quien no debe nada vencido.
   - Como todo recordatorio, la baja (BAJA) lo detiene.
2. **Lo enciende el dueño** (`TenantSettings.CreditDueSoonEnabled`, `CreditDueSoonDaysBefore`, `CreditDueSoonTemplateId`).
   - Va en la misma pantalla que los recordatorios de lo vencido («Recordatorios del fiado», en Por cobrar), y lo cambia el dueño o un administrador.
   - Se elige cuántos días antes (de 1 a 7, 2 por omisión) y con qué mensaje.
   - Encenderlo pide WhatsApp conectado; apagarlo, nada.
   - Empieza apagado en todos los negocios, y se enciende aparte de los recordatorios: un negocio puede tener uno, otro o los dos.
3. **A quién se le avisa** (`CreditDueSoonPlanner`):
   - **Va al corriente.** Una cuenta (teléfono) con algo vencido por su plazo no recibe el aviso: lo suyo es el recordatorio de lo vencido, si está encendido. Así nadie recibe «vence pronto» y «ya venció» a la vez.
   - **Algo le vence pronto.** Debe algo cuyo día límite, por su plazo (el suyo o el del negocio), cae de hoy a dentro de N días, en la zona del negocio. Un aviso que no salió a tiempo (el worker estuvo detenido o se encendió tarde) sale en cuanto puede, mientras ese día no haya pasado.
   - **Nunca el mismo día en que se le fió.** Solo cuenta lo que debe desde antes de hoy. Si el plazo es igual o menor que los días de anticipación, el aviso llega al día siguiente de la venta.
   - **Nunca a quien escribió BAJA.**
4. **Cuántos avisos.**
   - Uno por cuenta y por su próximo día límite (`DeduplicationKey` `creditsoon:{teléfono}:{día límite}:{día}`).
   - Si paga lo de ese día y lo siguiente cae dentro de los días, recibe otro aviso de ese nuevo día límite. Pero a lo más uno al día.
   - Cuenta cualquier aviso encolado (enviado, fallido u omitido): a quien no se pudo alcanzar no se le insiste. No cuenta uno cancelado ni uno omitido por el negocio (el aviso se apagó o se pasó la hora): ese cliente nunca lo recibió, y se encola otra vez al día siguiente si el día límite no ha pasado.
   - Hasta 500 cuentas por pasada y negocio, del día límite más cercano al más lejano. Quien ya tiene su aviso o escribió BAJA se descarta antes de contar.
5. **Cuándo.** El mismo worker y la misma ventana que los recordatorios (`MessagingWorker.QueueCreditRemindersAsync`):
   - se encola de 10:00 a 19:00 del negocio, y nunca sin WhatsApp conectado;
   - recordatorios y avisos se encolan cada uno en su alcance y con su propio guardado, así que si uno choca con otra instancia (la llave única), el otro se guarda igual.
6. **Al mandarlo se revisa otra vez** (`OutgoingMessageSender`). Se omite, con su motivo, si:
   - el aviso se apagó;
   - ya no es el día del negocio en que se encoló, o ya pasan de las 20:00;
   - ya tiene algo vencido (le dieron un plazo más corto): le toca el recordatorio de lo vencido;
   - ya no debe nada cuyo día límite, por su plazo de ahora, sea el del aviso: pagó, se le condonó o le cambiaron el plazo;
   - escribió BAJA.
7. **El mensaje.**
   - El texto por omisión dice lo que debe en total y lo que vence ese día, invita a ignorarlo si ya pagó y a escribir BAJA.
   - Las plantillas «Aviso antes de vencer» solo las crea o cambia el dueño o un administrador, como las del recordatorio.
   - Una plantilla desactivada, o una que no es de este uso, deja el texto por omisión.
   - Fuera de las 24 horas después del último mensaje del cliente, solo llega con una plantilla aprobada en Meta. La pantalla lo advierte.
8. **La API se mantiene compatible.**
   - `GET /api/v1/customers/credit-reminders` suma `dueSoon` con lo que la pantalla necesita.
   - `PUT` acepta `dueSoon` opcional. Sin él, el aviso queda como estaba: una pantalla de la versión anterior, guardada durante el despliegue, no lo apaga.
   - Una sola actividad (`CreditRemindersUpdated`) guarda cómo quedaron los dos.
9. **Se ve:**
   - **Por cobrar:** «Aviso 2 días antes de vencer · recordatorios cada 7 días», o lo que esté encendido.
   - **Recordatorios del fiado:** dos secciones, «Antes de que venza» y «Lo vencido», cada una con su interruptor, sus días, su mensaje y cómo le llega al cliente. Una sección apagada no detiene el guardado: si sus días no se pueden guardar, conserva los que tenía.
   - **Estado de cuenta:** el último aviso que se le mandó.
   - **Campañas:** el uso «Aviso antes de vencer» en Plantillas, y la lista «Avisos antes de vencer» arriba de la de los recordatorios en «Recordatorios del fiado», en el orden de la pantalla de los recordatorios.
10. **Fuera de esta fase:**
    - avisos por SMS o correo;
    - varios avisos por día límite (por ejemplo, 3 días antes y el mismo día);
    - avisarle a quien ya tiene algo vencido de lo que le vence después;
    - pausar el aviso de un cliente sin su baja;
    - el horario del negocio y sus días cerrados en la ventana de envío.
11. **La migración** `20261022153116_AddCreditDueSoon` agrega a `TenantSettings`:
    - `CreditDueSoonEnabled` (`bit`, 0 por omisión);
    - `CreditDueSoonDaysBefore` (`int`, 2 por omisión), con `CK_TenantSettings_CreditDueSoonDaysBefore` (de 1 a 7);
    - `CreditDueSoonTemplateId` (`uniqueidentifier`, con nulos).

## Consecuencias

- **Lo que logra:**
  - El cliente sabe cuánto y cuándo pagar antes de atrasarse.
  - El negocio cobra a tiempo sin mandar mensajes a mano, y el primer mensaje no es un cobro.
  - Cada cliente recibe a lo más un aviso por día límite, y nada si ya pagó.
- **Lo que limita:**
  - Sin plantilla aprobada en Meta, el aviso solo llega a quien escribió en las últimas 24 horas. Los demás quedan como fallidos en el registro.
  - Quien tiene algo vencido no recibe el aviso de lo que le vence después, aunque los recordatorios de lo vencido estén apagados.
  - Con un plazo igual o menor que los días de anticipación, el aviso llega al día siguiente de la venta, no N días antes.
  - Quien fía y paga cada día puede recibir un aviso cada día: cada pago deja un nuevo día límite.
  - El texto se arma al encolar. Un reintento del mismo día sale con lo que debía en la mañana, aunque haya pagado una parte (si pagó todo lo de ese día, se omite).
  - Revisar lo que vence corre en el ciclo del worker de mensajes, de 10:00 a 19:00, por cada negocio con el aviso encendido, como los recordatorios.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (22J) funciona con la base migrada: no conoce las columnas, guarda los ajustes con el aviso apagado y no encola avisos.
  - Mientras conviven las dos versiones, el worker de la anterior puede mandar un aviso que encoló la nueva, revisando la baja pero no si todavía debe lo de ese día ni la hora.
  - Si se vuelve a ella, el aviso deja de encolarse, y los que sigan pendientes salen sin esa revisión.
- **Reversibilidad:**
  - `rollback.sql` borra las columnas y su restricción solo si ningún negocio encendió el aviso ni eligió su plantilla, y no hay plantillas ni mensajes con ese uso.
  - En cuanto alguno existe, se detiene con el error 50034 sin cambiar nada, y solo queda corregir hacia adelante.
