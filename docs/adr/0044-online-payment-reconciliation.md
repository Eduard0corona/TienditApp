# ADR 0044: Lo que pasa en Mercado Pago regresa al pedido

- Estado: Aceptada
- Fecha: 2026-10-22

## Contexto

La Fase 24I ([ADR 0031](0031-online-payments.md)) cobra en línea con la cuenta de Mercado Pago del negocio. TienditApp solo sabe de los pagos que aplica y de las devoluciones que pide, y dejó fuera lo que pasa solo en Mercado Pago:

- **Una devolución hecha desde la cuenta del negocio** (o por Mercado Pago) no se ve en el pedido. El pedido dice que se cobró todo, los informes cuentan dinero que ya regresó al cliente, y el pedido todavía deja reembolsar «En línea» lo que ya se devolvió.
- **Una devolución que Mercado Pago rechazó** se queda como rechazada para siempre, aunque el negocio ya la haya hecho desde su cuenta como se le pidió. Configuración › Pagos en línea la sigue contando.
- **Un contracargo** (el banco del cliente desconoce el cargo) o **una reclamación** (el cliente abre una disputa en Mercado Pago) no se ven. Mientras está abierto, Mercado Pago retiene el dinero; perdido, se lo regresa al cliente.

Lo que dice Mercado Pago (su documentación para México):

- Una devolución parcial deja el pago `approved` / `partially_refunded`; una total, `refunded`. `GET /v1/payments/{id}/refunds` lista cada devolución con su id, monto, estado (`approved`, `in_process`, `rejected`, `cancelled`), quién la hizo (`source.type`: `collector`, `admin`, `bpp`…) y su fecha.
- Un contracargo pone el pago en `charged_back`, con `in_process` (abierto), `settled` (perdido: el dinero regresó al comprador) o `reimbursed` (ganado). Una reclamación lo pone en `in_mediation` y termina con `bpp_refunded` (devuelto) o `bpp_covered` (cubierto), también parciales.
- El aviso de «Pagos» que ya escucha TienditApp llega con cualquier cambio del pago, también una devolución hecha desde el panel. El aviso propio de contracargos pide una configuración que no está confirmada para cuentas conectadas por OAuth.
- `/v1/payments/search` busca los pagos de la cuenta por fecha de su última actualización.

Tomé las decisiones de la Fase 24K con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué cambia el dinero del pedido.** Solo dos cosas, y solo en el pedido que creó el cobro (el pago que lo creó):
   - cada devolución **aprobada** en Mercado Pago que TienditApp no pidió;
   - el contracargo que el negocio pierde.

   Una devolución en curso, rechazada o cancelada no cuenta. Un cobro que no creó pedido, o un segundo pago del mismo cobro, no tiene pedido en el que reflejarse: solo resuelve las devoluciones de TienditApp de ese pago (punto 3).
2. **Una devolución hecha en Mercado Pago** se vuelve un reembolso «En línea» del pedido:
   - por su monto, hasta lo que queda pagado en línea (si es más, se registra lo que queda y se deja en el log);
   - registrado cuando TienditApp se entera, como todo pago: enterarse tarde no reescribe un día cerrado;
   - por «Mercado Pago», con el id de su devolución como referencia y sin regresar artículos al inventario;
   - con el motivo de quién la hizo: «Devolución hecha desde la cuenta de Mercado Pago.» (el negocio, o quien no se sabe), «Mercado Pago hizo la devolución.» (`admin`) o «Mercado Pago se lo devolvió al cliente por una reclamación.» (`bpp`).

   Queda también como `OnlinePaymentRefund` ya hecha (`MadeInMercadoPago`: motivo 4 `InMercadoPago` o 5 `Claim`, estado `Completed`, con su id), sin pasar por la cola ni avisar al cliente. Un pedido cancelado no recibe reembolsos.
3. **Antes de tomarla como de fuera,** se busca entre las de TienditApp del mismo pago:
   1. la que ya tiene ese id: ya está;
   2. una pedida y en curso del mismo monto: se deja a la cola, que anota su id;
   3. una hecha sin id del mismo monto: se le anota el id (`IdentifyInMercadoPago`);
   4. una que Mercado Pago rechazó, del mismo monto: el negocio la hizo desde su cuenta, como se le pidió. Pasa al estado nuevo 3 `DoneInMercadoPago` (`CompleteInMercadoPago`), conserva por qué la rechazó Mercado Pago y deja de contar en Pagos en línea. Vale también para las automáticas de un cobro que no creó pedido o se pagó dos veces;
   5. si no, es de fuera (punto 2).

   Un rechazo no cambia el pago, así que ni el aviso ni la revisión lo volverían a leer: cuando Mercado Pago rechaza una que pidió la cola, la cola busca enseguida una devolución aprobada del mismo monto que no pueda ser otra de TienditApp (el negocio ya la había hecho en su cuenta) y, si la hay, la deja como hecha en Mercado Pago. Deja fuera las de id conocido y tantas como devoluciones de TienditApp de ese monto haya hechas sin su id o en curso, como el reflejo, que las empareja primero.
4. **La disputa** (contracargo o reclamación) se guarda en el cobro (`OnlineCheckout.Dispute`: 0 ninguna, 1 abierta, 2 perdida, 3 ganada; y `DisputeUpdatedAt`), y solo en uno que creó pedido (`RecordDispute`):
   - `charged_back` / `settled` → perdida; `reimbursed` → ganada; cualquier otro detalle de `charged_back` → abierta;
   - `in_mediation` → abierta; `bpp_refunded` (o parcial) → perdida; `bpp_covered` (o parcial) → ganada;
   - una abierta cuyo pago vuelve a `approved` → ganada; a `refunded` → perdida.
   - `DisputeUpdatedAt` es la hora en que el pago cambió en Mercado Pago para decirlo: un pago leído antes de ese cambio (una revisión que lo leyó minutos antes de que llegara el aviso) no cambia la disputa.

   El dinero de una reclamación perdida entra por su devolución (`bpp`, punto 2).
5. **Un contracargo perdido** devuelve lo que quedaba pagado en línea, después de reflejar las devoluciones: un reembolso «En línea» por «Mercado Pago» con el motivo «Contracargo: el banco del cliente le regresó el pago.» y su `OnlinePaymentRefund` (motivo 6 `Chargeback`). Uno por pago: un índice único filtrado lo garantiza. Mientras el contracargo está abierto no se mueve dinero: Mercado Pago lo retiene y puede resolverlo a favor del negocio. Uno ganado después de perdido no deshace el reembolso.
6. **Cómo se entera TienditApp:**
   - **el aviso de «Pagos»** (el de la 24I): después de aplicar un pago aprobado, o directamente si ya no está aprobado, refleja lo que haya;
   - **una revisión cada 15 minutos por negocio** (`ReviewOnlinePaymentsCommand`, en el worker de pagos en línea y en el alcance del negocio), para el aviso que se perdió:
     - pregunta a Mercado Pago por los pagos de la cuenta que cambiaron desde hasta donde se leyó todo (`OnlinePaymentAccount.PaymentsReviewedUntil`), del cambio más viejo al más nuevo;
     - una revisión que leyó todo deja la cuenta revisada hasta 10 minutos antes de ahora (el reloj de Mercado Pago puede ir adelantado: la siguiente vuelve a leer esos minutos), y la siguiente toca 15 minutos después;
     - hasta 4 páginas de 50 por revisión; una que llega a su límite deja la cuenta revisada hasta el último cambio que leyó, por el reloj de Mercado Pago, y la siguiente sigue de ahí en la siguiente pasada;
     - cada página empieza en el último cambio de la anterior (no en una posición): un pago que cambia otra vez mientras corre la revisión se va al final sin recorrer a los demás. Solo una página que cambió toda en el mismo instante sigue por posición;
     - una página trae completa lo que Mercado Pago devolvió, también lo que no se pudo leer; una respuesta sin su lista de resultados es como si Mercado Pago no respondiera;
     - siempre avanza: si una revisión vuelve a leer solo el instante en que empezó, la siguiente sigue después de él y deja fuera el resto de ese instante (queda en el log);
     - la primera vez, desde el cobro más viejo pagado con esa cuenta (pudo estar conectada antes) o desde que se conectó, a lo más 180 días atrás (la ventana de devoluciones de Mercado Pago);
     - de lo que llega, solo los pagos de cobros de este negocio;
     - sin llave que sirva no revisa; si Mercado Pago no responde, no avanza, y la siguiente empieza donde terminó la anterior; si rechaza la llave, se marca para conectarla otra vez. La fecha de revisión solo avanza, y conectar otra cuenta la borra;
     - las revisiones de una pasada del worker empiezan dentro de un minuto, después del barrido de la 24I: las cuentas que quedan van primero en la siguiente pasada.
   - **El reflejo de cada pago** (`ReconcileOnlinePaymentCommand`) va en su propia transacción, con el pago y sus devoluciones ya leídos: las llamadas a Mercado Pago quedan fuera. Primero revisa que el pago sea de ese cobro, de la cuenta del negocio y del modo de la plataforma. Las devoluciones solo se leen cuando el pago puede tener alguna (`refunded`, `partially_refunded` o un monto devuelto); sin ellas, solo se mira la disputa.
   - **Dos vías con el mismo pago a la vez** (el aviso y la revisión) chocan en los índices únicos (el id de cada devolución por negocio y el contracargo por pago) y en la versión del pedido. La que pierde vuelve a intentar una vez y encuentra lo que hizo la otra. Reflejar el mismo pago otra vez no cambia nada.
7. **Se ve:**
   - **Pedidos › detalle** (que pide el pedido completo al abrir uno en línea: la lista no trae esto): bajo cada reembolso «En línea», de dónde vino: «Se hizo en Mercado Pago.», «Mercado Pago lo devolvió por una reclamación.» o «El banco se lo regresó al cliente (contracargo).»; una rechazada que después se devolvió en la cuenta del negocio dice «Mercado Pago no lo devolvió al pedirlo; después se devolvió en tu cuenta de Mercado Pago.». Un aviso con la disputa abierta (Mercado Pago retiene el dinero mientras se resuelve), perdida o ganada, y la última noticia de Mercado Pago. Una perdida sin reembolso de la disputa (el negocio ya había devuelto todo) manda a revisar la cuenta de Mercado Pago; una ganada que conserva el reembolso del contracargo lo avisa.
   - **Configuración › Pagos en línea:** cuántos pagos están en disputa, hasta cuándo se revisó la cuenta y, en «Cómo funciona», que la revisión es cada 15 minutos. Las rechazadas ya no cuentan las que el negocio hizo después.
8. **La API se mantiene compatible:** `OrderDto` suma `onlineDispute` y `onlineDisputeUpdatedAt`; `OrderRefundDto`, `onlineReason`; `OnlinePaymentSettingsDto`, `disputesOpen` y `paymentsReviewedUntil`; `OnlinePaymentRefundStatus` suma `DoneInMercadoPago` (3) y llegan los enums `OnlinePaymentRefundReason` y `OnlinePaymentDispute`. Nada se quita ni cambia de nombre.
9. **Fuera de esta fase:**
   - el aviso de contracargos de Mercado Pago y mandar pruebas de un contracargo desde TienditApp;
   - avisarle al negocio por WhatsApp o correo;
   - la nota de crédito fiscal de lo devuelto;
   - deshacer un contracargo perdido que Mercado Pago cubre después;
   - contracargos parciales: se registra lo que quedaba pagado en línea;
   - pagos de la cuenta que no son de cobros de TienditApp.
10. **La migración** `20261022213253_AddOnlinePaymentReconciliation`:
    - `OnlinePaymentRefunds`: `CK_OnlinePaymentRefunds_Status` de 0 a 3; `CK_OnlinePaymentRefunds_Settled` también para el estado 3 (con la fecha y el motivo del rechazo); `CK_OnlinePaymentRefunds_Link`, los motivos 4, 5 y 6 con pedido y reembolso; `CK_OnlinePaymentRefunds_MadeInMercadoPago` (los motivos 4, 5 y 6 están hechos, y los dos primeros llevan su id); el índice único `IX_OnlinePaymentRefunds_TenantId_ProviderRefundId` (sin nulos) y el único `IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback` (motivo 6).
    - `OnlineCheckouts`: `Dispute` (`int`, 0 por omisión) con `CK_OnlineCheckouts_Dispute` (de 0 a 3), `DisputeUpdatedAt` (con nulos) y el índice `IX_OnlineCheckouts_TenantId_Dispute` (las abiertas).
    - `OnlinePaymentAccounts`: `PaymentsReviewedUntil` (con nulos).
    - Antes del índice de los ids, se detiene con el error 50036 si dos devoluciones de un negocio tienen el mismo id de Mercado Pago.

## Consecuencias

- **Lo que logra:**
  - El pedido, el corte y los informes dicen lo que de verdad quedó cobrado en línea, aunque la devolución se haya hecho en Mercado Pago.
  - Ya no se puede reembolsar «En línea» lo que Mercado Pago ya devolvió.
  - Una devolución rechazada que el negocio hizo desde su cuenta deja de pedirle que la haga.
  - El negocio ve en el pedido y en Pagos en línea un pago en disputa mientras Mercado Pago retiene el dinero, y cómo terminó.
  - Un aviso perdido llega a lo más en unos 15 minutos.
- **Lo que limita:**
  - Una devolución de fuera se registra el día en que TienditApp se entera, no el día en que se hizo: el corte de ese día la muestra, el del día en que se hizo no.
  - Se reconoce una devolución de TienditApp por su monto: dos del mismo monto en el mismo pago se pueden tomar una por la otra, y el total del pedido queda bien.
  - Un contracargo perdido devuelve todo lo que quedaba pagado en línea, aunque Mercado Pago haya regresado solo una parte; uno ganado después no lo deshace.
  - La revisión lee hasta 200 pagos cambiados por pasada y negocio, todos los de la cuenta (también las ventas fuera de TienditApp). Una cuenta con muchos cambios, o la primera revisión de 180 días, avanza 200 por pasada (cada 2 minutos).
  - Si más pagos de los que lee una revisión cambiaron en el mismo instante (al milisegundo), el resto de ese instante se deja fuera.
  - Una devolución hecha en Mercado Pago en partes (o por otro monto) que la rechazada no se reconoce como suya: se registra como de fuera, hasta lo que queda pagado en línea, y la rechazada sigue pidiéndose al negocio.
  - Cada revisión le pide a Mercado Pago al menos una página por negocio conectado cada 15 minutos, y las devoluciones de cada pago que puede tenerlas.
  - Una disputa de un pago que no creó pedido no se guarda: ese dinero ya se devolvió o se está devolviendo.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (26E) funciona con la base migrada: no conoce las columnas nuevas ni los motivos y el estado nuevos, y no los escribe.
  - Mientras conviven las dos, la anterior enseña una devolución hecha en Mercado Pago como «Mercado Pago ya lo devolvió.» y una rechazada que el negocio hizo después como «Mercado Pago lo está devolviendo.» (no conoce el estado 3). Su cola solo trabaja las pedidas, así que no toca ninguna de las nuevas.
  - Si se vuelve a ella, lo ya registrado se queda en los pedidos y deja de revisarse lo nuevo.
- **Reversibilidad:**
  - `rollback.sql` quita las columnas, los índices y la restricción nueva, y regresa las otras restricciones a como estaban, solo si no hay devoluciones hechas en Mercado Pago, por reclamación o por contracargo, ni rechazadas que el negocio hizo, ni disputas.
  - En cuanto alguna existe, se detiene con el error 50035 sin cambiar nada: quitarlas dejaría reembolsos que la versión anterior no entiende. Solo queda corregir hacia adelante.
