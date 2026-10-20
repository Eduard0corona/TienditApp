# ADR 0035: Plazo por cliente y recordatorios del fiado

- Estado: Aceptada
- Fecha: 2026-10-20

## Contexto

La Fase 26B ([ADR 0034](0034-customer-write-offs-and-credit-term.md)) le puso un plazo al fiado: lo que pasa de él se ve como vencido en Por cobrar, el estado de cuenta y Vender. Dejó dos cosas pendientes:

- **Un solo plazo para todo el negocio.** Un negocio de barrio no le da el mismo plazo a todos: a las oficinas de enfrente les cobra cada quincena y a un vecino le da una semana.
- **Nadie le avisa al cliente.** El dueño ve lo vencido, pero cobrarlo sigue siendo mandar un WhatsApp a mano, cliente por cliente, desde el estado de cuenta.

TienditApp ya manda mensajes programados por WhatsApp con su cola (`OutgoingMessage`): recordatorios de citas, avisos y campañas, con plantillas, bajas (BAJA) y reintentos ([messaging.md](../domain/messaging.md)).

Tomé las decisiones de la Fase 26C con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Cada cliente puede tener su plazo** (`Customer.CreditTermDays`).
   - De 1 a 120 días, o el del negocio (null, lo que tienen todos al migrar). No hay «sin plazo» para un cliente: para no cobrarle nunca basta un plazo largo.
   - Lo pone el dueño o un administrador (`SetCustomerCreditTermCommand`, `RequiresTenantManager`) en «Fiado de…», junto al límite, y queda en la actividad (`CustomerCreditTermUpdated`).
   - Necesita teléfono y no es para «Público en general», como el límite. No necesita límite: un pedido entregado sin cobrar también se debe y también vence.
   - Un plazo propio igual al del negocio sigue siendo propio: si después cambia el del negocio, el del cliente se queda.
   - El plazo que cuenta es el del cliente, o si no tiene, el del negocio. Se cuenta igual que en la 26B: desde que empezó a deber y en días del negocio.
2. **De qué ficha sale el plazo.** El fiado se junta por teléfono, y el plazo sale de la ficha del cliente con ese teléfono.
   - Una ficha guardada con diez dígitos (antes de normalizar teléfonos) cuenta como la misma persona, como en la 26A.
   - Si dos fichas comparten teléfono (crear un cliente lo impide, pero pueden venir de antes), cuenta el plazo más corto. La regla es la misma en Por cobrar, el estado de cuenta, Vender y los recordatorios.
   - Una cuenta sin ficha (solo el teléfono de sus pedidos) usa el plazo del negocio.
3. **Lo vencido lo lee la base**, aunque cada cliente tenga su plazo (`CreditTerm.OverdueAsync`).
   - Los pedidos que se deben se juntan con los plazos propios por teléfono.
   - Cada plazo en uso es una condición con el día del negocio en que empieza lo vencido. Hay a lo más 121 plazos distintos (los del negocio y de 1 a 120).
   - Así, Por cobrar sigue sumando, ordenando y paginando en la base, como en la 26A.
4. **Los recordatorios los enciende el dueño** (`TenantSettings.CreditRemindersEnabled`, `CreditReminderEveryDays`, `CreditReminderTemplateId`).
   - Desde Por cobrar, el dueño o un administrador (`UpdateCreditRemindersCommand`, `RequiresTenantManager`) los enciende o apaga y elige cada cuántos días (de 1 a 30, 7 por omisión) y con qué mensaje.
   - Encenderlos pide WhatsApp conectado; apagarlos, nada. Queda en la actividad (`CreditRemindersUpdated`).
   - Empiezan apagados en todos los negocios: nadie recibe un mensaje que el dueño no decidió mandar.
5. **A quién se le recuerda** (`CreditReminderPlanner`):
   - a cada cuenta (teléfono) con algo vencido por su plazo, tenga ficha o no;
   - nunca a quien escribió BAJA, como todo recordatorio (la respuesta a la baja ya promete «ni recordatorios»);
   - el primero cuando algo vence, y luego cada N días mientras siga vencido;
   - uno por cuenta y día del negocio (`DeduplicationKey` `credit:{teléfono}:{día}`), así que dos pasadas o dos instancias no lo encolan dos veces;
   - la cadencia cuenta cualquier recordatorio encolado (enviado, fallido u omitido), y cuenta el día del negocio en que se encoló: a quien no se pudo alcanzar no se le insiste en cada pasada;
   - no la cuenta uno cancelado ni uno omitido por el negocio (los recordatorios se apagaron o se pasó la hora): ese cliente nunca lo recibió;
   - hasta 500 cuentas por pasada y negocio, de la que más debe vencido a la que menos. Quien no toca (ya tiene recordatorio dentro de los días o escribió BAJA) se descarta antes de contar las 500, así que las demás entran en las pasadas siguientes.
6. **Cuándo.** El worker de mensajes revisa cada pocos minutos (`MessagingWorker.QueueCreditRemindersAsync`, a la par de los recordatorios de citas).
   - Solo encola de 10:00 a 19:00 del negocio, en su zona horaria. Nunca de noche.
   - Sin WhatsApp conectado no encola nada: el recordatorio solo fallaría.
7. **Al mandarlo se revisa otra vez** (`OutgoingMessageSender`). Se omite, con su motivo en el registro de mensajes, si:
   - los recordatorios se apagaron;
   - ya no es el día del negocio en que se encoló, o ya pasan de las 20:00 (el worker estuvo detenido);
   - ya no debe nada vencido: pagó, se le condonó o le dieron más plazo;
   - escribió BAJA.
8. **El mensaje** (`MessagePurpose.CreditReminder`).
   - El texto por omisión dice lo que debe en total, lo vencido y el día en que venció lo más viejo, e invita a ignorarlo si ya pagó y a escribir BAJA.
   - El negocio puede escribir sus plantillas con el uso «Recordatorio del fiado» y las variables `{nombre}`, `{negocio}`, `{monto}`, `{vencido}` y `{fecha}` (el último día para pagar que ya pasó).
   - Esas plantillas solo las crea o cambia el dueño o un administrador, como quien enciende los recordatorios: lo que se le dice a quien debe es su decisión, y gestionar campañas no basta.
   - Si la plantilla elegida se desactiva, se manda el texto por omisión, y la pantalla la muestra como «Texto predeterminado».
   - El texto se arma al encolar, con lo que debía en ese momento. Al mandarlo solo se revisa que siga debiendo algo vencido.
   - Como todo mensaje que inicia el negocio, fuera de las 24 horas después del último mensaje del cliente solo llega con una plantilla aprobada en Meta. La pantalla lo advierte cuando el mensaje elegido no tiene una.
9. **Se ve:**
   - **Por cobrar:** lo vencido aunque el negocio no tenga plazo (si algún cliente tiene el suyo), «Paga en N días» en la fila de quien tiene plazo propio, y los recordatorios («Recordatorios cada 7 días», o «Sin recordatorios» con «Activar» para el dueño).
   - **Estado de cuenta:** si el plazo es «solo para este cliente», y el último recordatorio y cada cuánto se repite.
   - **Clientes:** «Paga en N días» junto a «Fiado hasta…».
   - **Campañas:** en Plantillas, el uso «Recordatorio del fiado» y sus variables. La pestaña «Recordatorios del fiado» muestra los últimos que se mandaron, fallaron o se omitieron, con su motivo. La pantalla de los recordatorios lleva a las dos.
10. **Fuera de esta fase:**
    - el aviso antes de vencer;
    - recordatorios por SMS o correo;
    - pausar los recordatorios de un cliente sin su baja (la baja de Mensajes la registra el negocio a petición del cliente);
    - el horario del negocio y sus días cerrados en la ventana de envío;
    - intereses y saldo a favor (el saldo a favor llegó en la 26D, [ADR 0036](0036-customer-balance.md); los intereses siguen fuera).
11. **La migración** `20261020055548_AddCustomerCreditReminders` agrega:
    - `Customers.CreditTermDays` (`int`, con nulos), con `CK_Customers_CreditTermDays` (de 1 a 120);
    - `TenantSettings.CreditRemindersEnabled` (`bit`, 0 por omisión);
    - `TenantSettings.CreditReminderEveryDays` (`int`, 7 por omisión), con `CK_TenantSettings_CreditReminderEveryDays` (de 1 a 30);
    - `TenantSettings.CreditReminderTemplateId` (`uniqueidentifier`, con nulos).

## Consecuencias

- **Lo que logra:**
  - Cada cliente vence con su plazo, y el negocio cobra primero lo que de verdad está atrasado.
  - Lo vencido se cobra solo: el cliente recibe en WhatsApp lo que debe y desde cuándo, cada pocos días, sin que el dueño lo mande a mano. Deja de recibirlo en cuanto paga.
  - El cliente decide: con BAJA ya no le llegan, como cualquier recordatorio.
- **Lo que limita:**
  - Sin plantilla aprobada en Meta, el recordatorio solo llega a quien escribió en las últimas 24 horas. Los demás quedan como fallidos en el registro de mensajes. Las plantillas se aprueban en Meta, fuera de TienditApp.
  - La ventana de envío es fija (de 10:00 a 19:00) y no lee el horario del negocio ni sus días cerrados.
  - Una cuenta sin ficha también recibe recordatorios: debe, y dejó su teléfono en el pedido. Para que no le lleguen, el negocio registra su baja.
  - Un cliente con dos fichas toma el plazo más corto de las dos.
  - Un reintento de un recordatorio puede salir, el mismo día, con lo que debía en la mañana si después pagó solo una parte.
  - Revisar lo vencido corre en el ciclo del worker de mensajes, cada pocos minutos y de 10:00 a 19:00, por cada negocio con recordatorios.
  - Los recordatorios no llevan el estado de cuenta pedido por pedido: para eso sigue «Enviar por WhatsApp» en la cuenta.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (26B) funciona con la base migrada: no conoce las columnas, guarda clientes y ajustes con los valores por omisión, y no encola recordatorios.
  - Mientras conviven las dos versiones (el despliegue o un swap), el worker de la 26B puede mandar un recordatorio que encoló la nueva sin revisar lo vencido ni la hora.
  - Si se vuelve a ella, todos los clientes vuelven al plazo del negocio, y los recordatorios encolados (propósito 3) se mandarían sin la revisión de lo vencido.
- **Reversibilidad:**
  - `rollback.sql` borra las columnas y sus restricciones solo si ningún cliente tiene plazo propio, ningún negocio encendió los recordatorios ni eligió su plantilla, y no hay plantillas ni mensajes de recordatorio del fiado.
  - En cuanto alguno de esos existe, se detiene con el error 50028 sin cambiar nada, y solo queda corregir hacia adelante.
