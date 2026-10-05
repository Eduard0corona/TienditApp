# Capas y dependencias

## Dirección obligatoria

```mermaid
flowchart BT
    Domain[Domain]
    Application[Application] --> Domain
    Infrastructure[Infrastructure] --> Application
    WebApi[WebApi / composition root] --> Application
    WebApi --> Infrastructure
    Client[client] -. contratos HTTP y SignalR .-> WebApi
```

- `Domain` no depende de Application, Infrastructure ni WebApi.
- `Application` depende de Domain.
- `Infrastructure` implementa interfaces definidas por Application.
- `WebApi` compone Application e Infrastructure.
- `client` consume contratos; no contiene reglas autoritativas.
- Ninguna capa salta Application para implementar un caso de uso.

## Responsabilidades

### Domain

Contiene invariantes y comportamiento de agregados, value objects, máquinas de estado y eventos. Las colecciones se exponen como solo lectura. Domain no conoce EF Core, HTTP, SignalR, almacenamiento ni proveedores. Excepción actual: el proyecto referencia `Microsoft.Extensions.Identity.Stores` y `MediatR.Contracts`; es una dependencia técnica existente que debe vigilarse para no ampliar acoplamiento.

### Application

Orquesta Commands/Queries, validación con FluentValidation, DTOs, interfaces de puertos, autorización contextual y transacciones del caso de uso. Usa `IApplicationDbContext`, lo que introduce referencias de EF Core en Application; es estado actual. Los DTOs se mapean a mano (`XxxMappings` junto a cada DTO); desde la Fase 4F no hay AutoMapper.

Los puertos que un caso de uso necesita son obligatorios en su constructor (`IOrderInventoryService`, `IOrderCashService`, `IFolioGenerator`); las implementaciones de inventario y caja son reglas de negocio y viven en Application (`Inventory/OrderInventoryService`, `Finance/OrderCashService`, Fase 5A), con `InventoryLedger` como único lugar que registra movimientos manuales y existencias iniciales: desde la Fase 4F no hay respaldos que no hagan nada si falta el registro, porque una venta guardada sin consumir inventario o sin entrar a la caja pasaría inadvertida. Las pruebas pasan sus dobles de forma explícita (`NoOpOrderInventoryService`, `NoOpOrderCashService` en `tests/.../Support`).

El pipeline de MediatR corre, en orden, `AuthorizationBehavior`, `ValidationBehavior`, `LoggingBehavior` y `TransactionBehavior`. El último solo envuelve los comandos marcados `ITransactionalRequest` (sus restricciones genéricas lo dejan fuera de los demás) y corre el handler en una transacción (ver [consistencia de datos](data-consistency-and-domain-events.md)).

### Infrastructure

Implementa persistencia SQL Server, configuraciones y migraciones EF, Identity/JWT, archivos, imagen, WhatsApp, email, SMS, reloj y servicios coordinadores de inventario/caja. No debe introducir reglas que solo sean visibles desde un provider; si una regla es de dominio, muévela al agregado.

### WebApi

Es el composition root. Controllers deben recibir DTOs/commands, delegar a MediatR, mapear resultados HTTP y permanecer delgados. Middleware resuelve errores, tenant y controles transversales. La excepción actual más clara es `WhatsAppWebhookController`, que usa `IApplicationDbContext` directamente y contiene flujo considerable; se registra como deuda, no como patrón.

### client

Mantiene páginas, componentes, hooks, contextos, tipos y una única capa `src/api`. Puede calcular valores para presentación, pero backend decide estados, totales, impuestos, saldos y existencias finales.

## Guardrails de revisión

Desde la Fase 4F, `tests/FoodParkDigital.WebApi.Tests/ArchitectureTests.cs` verifica en CI lo que se puede comprobar sobre los ensamblados compilados: las capas solo dependen hacia adentro, Domain no conoce persistencia, web ni proveedores (salvo `MediatR.Contracts` e Identity), Application no conoce HTTP ni proveedores de base de datos, los handlers viven en Application, los controllers no reciben el DbContext (excepción conocida: `WhatsAppWebhookController`), toda entidad con `TenantId` es `ITenantScoped` y tiene el filtro de negocio (excepto `TenantSettings` y `WhatsAppConfig`, que se leen por `TenantId` antes de haber negocio en contexto), y una entidad nueva sin `TenantId` debe clasificarse a propósito (plataforma, cuentas o hija de un agregado filtrado). Una excepción nueva se agrega a su lista con la razón.

Rechaza un cambio si:

- añade referencia ascendente prohibida;
- retorna entidades de Domain/EF desde un endpoint;
- pone acceso a datos o integración en un controller;
- duplica reglas autoritativas en React;
- hace que una entidad invoque WhatsApp, SignalR, email, SMS, Blob o HTTP;
- modifica estado con setters/reflection fuera de materialización controlada;
- crea una abstracción sin consumidor o separa servicios sin necesidad comprobada.
