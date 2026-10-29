# ADR 0006: Eventos de dominio y Outbox

- Estado: Aceptada e implementada (Fase 2, 2026-10-04)
- Fecha: 2026-08-10

## Contexto

Los eventos se publicaban con MediatR después de `SaveChangesAsync`; los handlers escribían auditoría e invocaban SignalR/WhatsApp. No había atomicidad ni entrega durable: si un handler fallaba después del commit, el efecto se perdía.

## Decisión

Las entidades solo producen eventos. Los efectos durables (auditoría, avisos de WhatsApp al cliente) se guardan como mensajes Outbox en la misma transacción que el agregado y los procesa un worker con reintentos; los consumidores son idempotentes. La actualización de pantallas por SignalR sigue en proceso y best-effort, porque las pantallas también recargan al reconectar y con sondeo.

## Implementación

- `OutboxSaveChangesInterceptor` copia cada `DomainEvent` a `OutboxMessages` (tipo, payload JSON, tenant y actor) dentro del mismo `SaveChanges`.
- Un trabajo que una petición pide pero no debe hacer ni esperar, sin una entidad que lo origine, entra con `IOutbox.Add(evento, tenantId)` (Application; `OutboxWriter` en Infrastructure): se guarda con el siguiente `SaveChanges` de la petición y despierta al despachador igual que los demás. Lo usan desde la Fase 11 los códigos por SMS sin sesión (`VerificationCodeRequestedEvent`, sin negocio), para que la petición haga lo mismo exista o no la cuenta y el código no se pierda al reiniciar.
- `OutboxDispatcher` (Infrastructure, `BackgroundJobs/Outbox`) reclama mensajes con `LockToken` como token de concurrencia y un lease, ejecuta cada `IOutboxEventHandler<T>` en un scope propio con el tenant y el actor del evento, y registra en `OutboxMessageConsumers` qué handler terminó, para no repetirlo en un reintento.
- Desde la Fase 28K el apartado se renueva mientras se trabaja ([ADR 0056](0056-stamping-robustness.md)): `LeaseKeeper` vuelve a apartar cada tercio del lease lo que el lote no ha terminado, y lo que otra instancia tomó se deja de trabajar y no se completa. Lo usan el outbox, la bandeja del webhook y la cola de mensajes.
- Reintentos con espera creciente (`RetrySchedule`, 10 s a 6 h); al agotar los intentos el mensaje se aparta (dead-letter) y se registra en logs y métricas. Los procesados se borran tras `RetentionDays`.
- Los fallos transitorios de proveedores (`TransientDeliveryException`: 408, 429, 5xx, red) se reintentan; un rechazo definitivo se registra y no se repite.
- La misma mecánica (`QueueItem`) atiende la bandeja del webhook de WhatsApp (`InboxMessages`, deduplicada por hash del payload) y la cola de mensajes salientes de campañas y recordatorios (`OutgoingMessages`, ver [mensajería](../domain/messaging.md)).
- `BackgroundQueuesHealthCheck` degrada `/health/ready` si hay trabajo apartado en el último día o pendiente con más de 15 minutos de atraso.

## Consecuencias

- La auditoría y los avisos ya no se pierden si el proceso cae después del commit; pueden llegar con retraso (segundos) y, ante un timeout del proveedor, un aviso podría enviarse dos veces (el proveedor no ofrece idempotencia en envíos).
- Agregar un efecto durable es implementar `IOutboxEventHandler<T>`; los handlers de MediatR (`INotificationHandler`) quedan para efectos en proceso no críticos.
- El evento serializado es un contrato: renombrar o quitar propiedades de un evento requiere compatibilidad con los mensajes pendientes.
- Lo que un evento lleva queda en `OutboxMessages` hasta su limpieza: un evento con datos personales (el correo de una solicitud de recuperación) se guarda con lo mínimo que su handler necesita.
