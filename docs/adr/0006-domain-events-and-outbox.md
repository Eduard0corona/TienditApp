# ADR 0006: Eventos de dominio y Outbox

- Estado: Aceptada como dirección objetivo
- Fecha: 2026-08-10

## Contexto

Los eventos actuales se publican con MediatR después de `SaveChangesAsync`; handlers escriben auditoría e invocan SignalR/WhatsApp. No existe atomicidad ni entrega durable.

## Decisión

Las entidades solo producen eventos. Side effects viven en handlers/Infrastructure y son idempotentes. Antes de depender de eventos para procesos críticos o integraciones, implementar Outbox persistido en la misma transacción, dispatcher observable y consumidores idempotentes.

## Consecuencias

Hasta implementar Outbox, documentar los procesos como post-commit best-effort y no “transaccionales”. Outbox añade tabla, worker, reintentos, retención y monitoreo; se implementará en una tarea dedicada, no aquí.
