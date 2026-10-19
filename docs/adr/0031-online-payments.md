# ADR 0031: Pago en línea con Mercado Pago

- Estado: Aceptada
- Fecha: 2026-10-19

## Contexto

El menú en línea (ADR 0026) recibe pedidos para recoger y a domicilio (ADR 0018), pero el cliente siempre paga al recibir. Las apps de entrega cobran al pedir, y un negocio que cocina a domicilio pierde comida y tiempo cuando un pedido no se paga. Faltaba que el cliente pudiera pagar al hacer el pedido.

Cobrar en línea trae decisiones que no se pueden tomar a medias: de quién es el dinero y por dónde pasa, qué datos de pago ve TienditApp, cuándo existe el pedido, qué pasa si el pago llega y el pedido ya no se puede hacer, cómo se sabe que un aviso de pago es verdadero y qué hace el negocio para devolver dinero.

## Decisión

1. **Mercado Pago, en la cuenta de cada negocio.**
   - **Checkout Pro:** el cliente paga en la página de Mercado Pago. TienditApp nunca ve una tarjeta.
   - **La cuenta es del negocio:** el dueño o un administrador la conecta en Configuración › Pagos en línea con la aplicación de TienditApp (OAuth con PKCE). El dinero llega a esa cuenta; TienditApp no lo toca ni cobra comisión (sin `marketplace_fee`). Lo que Mercado Pago descuenta lo ve el negocio en su cuenta.
   - **Conectar:** la autorización vale una vez, diez minutos, para el negocio y la persona que la empezó (`OnlinePaymentAuthorization`, su id es el `state`). Mercado Pago regresa a Configuración › Pagos en línea y la página la termina con la sesión del dueño; no hay un regreso anónimo.
   - **Las llaves:** se guardan cifradas con AES-256-GCM (`SecretProtection:Key`, fuera de la base) y con el negocio como dato autenticado: copiadas a otro negocio no se descifran. Se renuevan cuando les quedan 30 días (duran 180). Desconectar las borra. Ningún log lleva una llave, un código o datos del pagador.
   - **Una llave que Mercado Pago rechaza** (401 o 403: el dueño quitó el acceso a TienditApp) cuenta como vencida desde ese momento, no como «Mercado Pago no responde»: el menú deja de ofrecer el pago, las devoluciones pendientes quedan rechazadas para que el negocio las haga en su cuenta y los cobros en curso vencen a su hora. Configuración › Pagos en línea pide conectarla otra vez.
   - **Una cuenta de prueba** no se conecta en producción, ni una real en modo de pruebas (`MercadoPago:Sandbox`).
   - **Sin la configuración de la plataforma** (`MercadoPago:ClientId`, `ClientSecret`, `WebhookSecret`, `SecretProtection:Key` y `AppUrl`) nada de esto se ofrece ni se muestra.
2. **Solo pagos que se deciden al momento.** Tarjeta de crédito, de débito y saldo de Mercado Pago, en una sola exhibición (`binary_mode`, sin OXXO, cajero ni transferencia, `installments` 1). Un pedido de comida no espera días a que alguien pague en una tienda.
3. **Para recoger y a domicilio, no en una mesa.** El negocio lo enciende después de conectar la cuenta; la tienda ofrece pagar en línea o al recibir.
4. **El pedido existe cuando el pago se aprueba.**
   - **Al pagar:** el servidor cotiza el pedido con las reglas del menú (abierto, productos, precios, zona de entrega) y guarda un cobro (`OnlineCheckout`) con lo que se pidió y ese total, que vence en 30 minutos. Mercado Pago deja de aceptar pagos para él al vencer. No se crea nada del cliente todavía.
   - **Al aprobarse:** el pedido se crea con las reglas del menú como están en ese momento y el total debe ser igual a lo cobrado. Lleva un pago «En línea» por su total, con la referencia del pago de Mercado Pago, fechado cuando se registra (un aviso tarde no cambia un día cerrado).
   - **Lo que el cobro sabía del cliente** (lo que pidió y su teléfono) se borra al crearse el pedido o al vencer; queda en el pedido, si lo hay.
5. **Tres vías, y ninguna se cree un aviso.**
   - **El aviso de Mercado Pago:** firmado (`x-signature` sobre el id, el `x-request-id` y la hora, con `MercadoPago:WebhookSecret`), se guarda en el inbox y se responde al instante. Sin el secreto responde 503; con una firma mala, 401. La firma cubre solo el id del pago: de lo demás solo se usa la cuenta que menciona, para saber qué negocios probar, y el inbox lo guarda una vez por pago y petición firmados (el mismo aviso con otra cuenta en el cuerpo no se guarda otra vez).
   - **El regreso del cliente** a `/m/{slug}/pago/{id}`.
   - **Un barrido** cada dos minutos de los cobros que ninguna de las otras dos resolvió.
   - **Las tres leen el pago de Mercado Pago** con la llave del negocio. Cuenta solo si está aprobado, es de ese cobro (`external_reference`), lo cobró la cuenta del negocio, es del modo de la plataforma y es exactamente el monto en pesos. Aplicar el mismo pago otra vez no cambia nada, y dos vías a la vez se ordenan por la versión del cobro (`rowversion`).
   - **Cada pago se aplica en su propia transacción** (`ApplyOnlinePaymentCommand`), con el folio y los insumos del pedido, sin llamar a Mercado Pago dentro de ella.
6. **Si el pedido no se puede crear, el dinero regresa completo.** Si el pago llegó después de los 30 minutos, el negocio dejó de recibir pedidos, un producto ya no está (o salió de su horario, se borró o una de sus opciones se agotó), el total cambió o el pago no coincide, el cobro queda «no creado» con el motivo para el cliente y se pide la devolución. Lo que el catálogo de ahora rechaza se devuelve en una transacción aparte: el intento de crear el pedido no deja nada (ni folio ni cliente). Un segundo pago aprobado del mismo cobro también se devuelve.
7. **«En línea» no entra a ninguna caja.** Como «Plataforma» (ADR 0013): no se registra a mano, el corte lo muestra aparte, Inicio y ventas por canal dicen cuánto se cobró en línea, y el CFDI lleva la forma 31 (intermediario de pagos) con un aviso.
8. **Las devoluciones van por la cola.**
   - **Automáticas o del negocio:** un reembolso con el método «En línea» (o una anulación de un pedido pagado en línea) pide la devolución a Mercado Pago por el outbox (`OnlinePaymentRefund`), con su id como llave de idempotencia: reintentar nunca devuelve dos veces.
   - **El pedido muestra** si Mercado Pago la hizo o la rechazó, con su respuesta. Una rechazada (o sin cuenta conectada) la devuelve el negocio desde su cuenta.
9. **La cuenta no cambia con el pago encendido ni con pagos en curso.** Desconectarla o conectar otra cuenta pide apagar primero el pago en línea (así ningún cobro empieza con la cuenta que se va) y espera a que terminen los cobros y las devoluciones en curso: necesitan la cuenta con la que empezaron. Conectar otra vez la misma cuenta se puede siempre: así vuelve una llave rechazada.
10. **El regreso no da el enlace de seguimiento sin la llave del navegador.** El id del cobro va en la URL de regreso y Mercado Pago le agrega `external_reference`: la telemetría guarda rutas y consultas. Al empezar el cobro la tienda recibe una llave (el id cifrado para el negocio) que guarda en el navegador y manda en el cuerpo; sin ella el regreso dice cómo va el pago y el folio, pero no el enlace de seguimiento (ADR 0023).

## Consecuencias

- **Lo que logra:**
  - El cliente paga al pedir, con tarjeta o su saldo de Mercado Pago, y el negocio recibe solo pedidos pagados.
  - El dinero nunca pasa por TienditApp, ni los datos de la tarjeta.
  - Un pago que no se puede usar regresa solo, y el negocio ve cada devolución.
- **Lo que limita:**
  - Solo Mercado Pago, solo pesos y solo pagos inmediatos: no hay OXXO, transferencias ni meses sin intereses.
  - Un aviso perdido y un cliente que no regresa se resuelven con el barrido, en minutos, no al instante.
  - Si el negocio cierra entre el pago y el aviso, el pedido no se crea y el pago regresa.
  - Sin cuenta conectada (o con la llave vencida) una devolución no se pide sola: queda rechazada para que el negocio la haga en Mercado Pago.
  - Lo que pasa solo en Mercado Pago (un contracargo, una devolución hecha desde la cuenta del negocio) no regresa al pedido.
  - El enlace de seguimiento solo aparece en el dispositivo que empezó el pago.
- **Lo que hay que operar:**
  - La aplicación de TienditApp en Mercado Pago, con la URL de regreso `{AppUrl}/settings/online-payments` y el webhook `{AppUrl}/api/v1/payments/mercadopago/webhook` (evento «Pagos»).
  - La llave de cifrado: cambiarla deja las cuentas sin llave y cada negocio la conecta otra vez.
