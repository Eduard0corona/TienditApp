# Ciclos de vida de órdenes

Las transiciones siguientes provienen de las máquinas actuales. No son una propuesta.

## Food

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Confirmed
    Pending --> Accepted
    Pending --> InPreparation
    Pending --> InProgress
    Pending --> Ready
    Pending --> Delivered
    Pending --> Cancelled
    Confirmed --> Accepted
    Confirmed --> InPreparation
    Confirmed --> InProgress
    Confirmed --> Ready
    Confirmed --> Delivered
    Confirmed --> Cancelled
    Accepted --> Confirmed
    Accepted --> InPreparation
    Accepted --> InProgress
    Accepted --> Ready
    Accepted --> Delivered
    Accepted --> Cancelled
    InPreparation --> InProgress
    InPreparation --> Ready
    InPreparation --> Delivered
    InPreparation --> Cancelled
    InProgress --> InPreparation
    InProgress --> Ready
    InProgress --> Delivered
    InProgress --> Cancelled
    Ready --> Delivered
```

## Retail

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Confirmed
    Pending --> Accepted
    Pending --> Delivered
    Pending --> Cancelled
    Confirmed --> Accepted
    Confirmed --> Delivered
    Confirmed --> Cancelled
    Accepted --> Confirmed
    Accepted --> Delivered
    Accepted --> Cancelled
```

## Service

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Accepted
    Pending --> Confirmed
    Pending --> InProgress
    Pending --> Completed
    Pending --> Cancelled
    Confirmed --> Accepted
    Confirmed --> InProgress
    Confirmed --> Completed
    Confirmed --> Cancelled
    Accepted --> Confirmed
    Accepted --> InProgress
    Accepted --> Completed
    Accepted --> Cancelled
    InProgress --> Completed
    InProgress --> Cancelled
```

## Piezas de servicio

```mermaid
stateDiagram-v2
    [*] --> Accepted
    Accepted --> InProgress
    Accepted --> Cancelled
    InProgress --> Completed
    InProgress --> Cancelled
```

## Efectos coordinados actuales

- Al crear una orden de negocio, el caso de uso puede aplicar un estado inicial operativo y consumo de inventario.
- Al entrar en estados operativos definidos por `ShouldConsumeInventory`, se crea una salida idempotente por `ExternalReference`.
- Al cancelar, se crea un movimiento compensatorio si existió consumo.
- Al completar una pieza, se puede consumir su receta.
- Los eventos post-save crean auditoría y notifican por SignalR/WhatsApp.

## Deuda e inconsistencias

- `Cancel()` restringe Food a Pending/Confirmed/InPreparation, Retail a Pending/Confirmed/Accepted y Service a Pending/Accepted/Confirmed/InProgress; sin embargo, las tablas de transición permiten `Cancelled` desde más estados Food. `UpdateOrderStatusCommand` puede transicionar a `Cancelled` por la tabla, mientras `CancelOrderCommand` usa `CanCancel`. Debe unificarse en una decisión posterior.
- Las máquinas permiten saltos amplios (por ejemplo Pending a Delivered/Completed). Se documentan porque existen, no como flujo operativo ideal.
- `UpdateOrderStatusCommand` captura concurrencia por rowversion de `Order`; no todas las operaciones relacionadas tienen control equivalente.
- Cancelar mediante cambio de estado puede carecer de motivo.
