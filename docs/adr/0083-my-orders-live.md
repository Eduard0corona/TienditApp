# ADR 0083: «Mis pedidos» se entera al momento

- Estado: Aceptada en la Fase 31A (2026-11-10).
- Fecha: 2026-11-10
- Continúa y cambia en parte:
  - [ADR 0023](0023-order-tracking.md), punto 5: «No hay SignalR para el cliente: los hubs son del equipo de cada negocio». Desde esta fase hay uno para la cuenta del cliente; el enlace de seguimiento sigue sin él.
  - [ADR 0006](0006-domain-events.md): los avisos en pantalla siguen en proceso y sin entrega garantizada.

## Contexto

- **Hasta 20 segundos de retraso.** «Mis pedidos», el detalle de un pedido y el seguimiento con el enlace se enteran de un cambio (lo aceptaron, está listo, salió, lo cancelaron) preguntando cada 20 segundos ([ADR 0023](0023-order-tracking.md)). El equipo de cada negocio se entera al momento por SignalR, pero esos hubs son de un negocio: piden su membresía y publican solo a su grupo.
- **El cliente no es de un negocio.** La cuenta de un cliente no lleva negocio en su token: compra en varios. Un hub del equipo la rechaza, y el barrido que vuelve a revisar las conexiones cada 30 segundos la cerraría.
- **El enlace es un secreto.** El seguimiento sin cuenta se abre con un enlace que nunca llega al servidor en una URL ([ADR 0023](0023-order-tracking.md)). Escucharlo en vivo pide mandarlo por el canal, limitar los intentos (nada limita hoy `/hubs`) y decidir qué pasa cuando vence. Es otra decisión.

El dueño del producto pidió avanzar este punto (el 13) sin esperar su respuesta a cada decisión. Tomé las de la Fase 31A con esa autorización. No lleva migración.

## Decisión

1. **Un hub de la cuenta: `/hubs/my-orders` (`MyOrdersHub`).**
   - Pide una sesión abierta, como los demás. No pide negocio: cada conexión entra solo al grupo de su cuenta (`account_{userId}`, el id del token).
   - El barrido de las conexiones (`HubAccessSweeper`) vuelve a revisar su sesión y la cierra cuando termina (salir, otra contraseña, la cuenta desactivada). No le pide acceso a ningún negocio (`HubConnections.AddAccount`).
2. **Qué se avisa.**
   - Cuando un pedido que pertenece a una cuenta (`Order.CustomerUserId`) se crea, cambia de estado, se cancela o sale con el repartidor, su cuenta recibe `MyOrderChanged`.
   - **Solo el id del pedido.** Ni el negocio, ni el estado, ni nada del pedido o de otra persona: la pantalla vuelve a leer por `/api/v1/me`, que comprueba que el pedido es de la cuenta.
   - Sale del mismo manejador que avisa a las pantallas del negocio (`OrderRealtimeNotifications`), aparte, para que si uno falla no detenga al otro.
   - El pedido se lee con el negocio del evento y con el filtro del negocio en contexto, que cada comando que cambia un pedido pone antes de guardar (también cancelar desde la cuenta y pedir desde el menú). Si no coinciden, no se avisa: falla cerrado, sin decirle nada a nadie.
3. **El cliente.**
   - «Mis pedidos» y el detalle de un pedido escuchan el hub (`useMyOrdersLive`) y vuelven a leer los pedidos de la cuenta con cada aviso, y al conectarse otra vez.
   - SignalR se carga aparte, con un `import` dinámico: estas pantallas no están en la primera visita.
   - **Siguen preguntando cada 20 segundos** detrás del hub: la entrega es en proceso y sin garantía, y sin Azure SignalR varias instancias no comparten grupos.
4. **La regla de los hubs** (`src/FoodParkDigital.WebApi/AGENTS.md`) cambia así: los hubs del equipo validan membresía, negocio y permiso, y publican solo al grupo de su negocio. El de la cuenta valida la sesión y publica solo a la cuenta los ids de sus propios pedidos.

## Consecuencias

- **Lo que logra:**
  - El cliente con cuenta ve al momento que su pedido cambió, sin esperar los 20 segundos.
  - No sale nada nuevo del servidor: la pantalla lee lo mismo que ya leía, con la misma comprobación de que el pedido es suyo.
- **Lo que limita:**
  - **El seguimiento con el enlace sigue preguntando cada 20 segundos.** Hacerlo en vivo pide mandar el enlace por el canal (no en la URL), limitar los intentos y decidir qué pasa cuando vence o el negocio se suspende. Queda para otra fase.
  - Sin avisos en el navegador con la pestaña cerrada (Web Push): piden llaves de la plataforma, que cada persona dé permiso y un service worker ([ADR 0068](0068-tell-the-business-of-each-request.md)).
  - Un aviso perdido se recupera con la pregunta de cada 20 segundos o al reconectar.
  - Los pagos no generan evento: un pago registrado no avisa hasta que cambia el estado. Tampoco avisan los platillos agregados o cancelados de una cuenta abierta, ni un pedido que se liga a la cuenta después (al confirmar el teléfono): esos llegan con la pregunta de cada 20 segundos.
  - Un comando futuro que cambie un pedido sin poner su negocio en contexto perdería el aviso en silencio (sin fuga).
  - Es el primer hub con cuentas de cliente que se registran solas, y `/hubs` no limita las conexiones por cuenta ni su frecuencia: es un riesgo de recursos, no de aislamiento, anotado en la deuda técnica.
