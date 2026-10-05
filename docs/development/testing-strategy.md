# Estrategia de pruebas

## Estado actual

Existen cuatro proyectos xUnit y las pruebas del cliente:

- `FoodParkDigital.Domain.Tests`: máquinas de estado, cancelación/pagos de órdenes, caja, producto, configuración de WhatsApp, catálogo de permisos y valores persistidos de los enums de inventario.
- `FoodParkDigital.Application.Tests`: casos de órdenes/clientes, capabilities, tenant access, la regla de cada trabajo (`UseCaseRulesTests` y `AuthorizationBehaviorTests`, Fase 5), inventario, compras/caja, reportes, uploads, WhatsApp, empleados, aislamiento de archivos por tenant, configuración de arranque y EF mediante SQLite/InMemory y dobles. `SqliteTestDatabase` cubre constraints relacionales (índices únicos) que InMemory ignora; no reemplaza a SQL Server. `Support/SqliteTestHost` arma SQLite en memoria con FKs activas, una estrategia de ejecución con reintentos, Identity real con las opciones de producción e inyección de fallos (commit transitorio, comando fallido, escritura concurrente) para flujos transaccionales; no sustituye pruebas contra SQL Server. Ambos emulan el `rowversion` de `WhatsAppConversation` (valor aleatorio al insertar y trigger al actualizar), así que guardar una copia desactualizada falla como en SQL Server.
- `FoodParkDigital.Infrastructure.Tests`: servicios de Infrastructure con dobles (directorio temporal, HTTP, SMTP en loopback, logger, EF InMemory y `SqlException` construidas por reflexión con mensajes reales de SQL Server); cubre el confinamiento por tenant del almacenamiento local y de la resolución de blobs de Azure, y que SMS, correo, WhatsApp y la resolución del cliente de un pedido no registren códigos OTP, cuerpos, respuestas de proveedores ni PII fuera de Development. `DatabaseErrorLoggingTests` usa el registro de `AddInfrastructureServices` con un interceptor que responde en lugar de SQL Server para comprobar que EF Core no registra los mensajes de guardados y consultas fallidos fuera de Development.
- `FoodParkDigital.WebApi.Tests`: middleware de tenant (membresía, suspensión y rutas del negocio por defecto), acceso a hubs, controller de órdenes por tenant, verificación de firma del webhook de WhatsApp y su controller, y `GlobalExceptionHandlerMiddleware` (errores de base de datos registrados sin mensajes fuera de Development, `traceId` en `ProblemDetails`), sin pipeline HTTP completo. Comparte dobles de `Infrastructure.Tests/Support` enlazando sus archivos. `ClientEnumContractTests` lee `client/src/types/index.ts` y compara cada copia de un enum del dominio con el original, porque la API los intercambia como números. `ArchitectureTests` (Fase 4F) verifica dependencias entre capas, handlers en Application, controllers sin DbContext y sin roles (Fase 5), el filtro de negocio de cada entidad y que los comandos que toman folio corran en una transacción; `ApiProblemContractTests`, que todo error sea `ProblemDetails` con `code`.
- Cliente (Fase 5): Vitest con jsdom y Testing Library (`npm test`, archivos `*.test.ts(x)` junto al código): la matriz de acceso (`access.ts`), dinero y cantidades, la estimación de totales con IVA que replica a la API, los mensajes de error por `code` y la tarjeta de existencias del producto con la API simulada.
- Pruebas contra SQL Server 2022 (Fase 4F): las clases `*SqlServerTests` de `Infrastructure.Tests` corren en el job `Migrations` contra el contenedor (`TIENDITAPP_TEST_SQLSERVER`) y se omiten en el resto: contadores de folios con solicitudes simultáneas, la traducción de las proyecciones de listas (`DtoProjectionSqlServerTests`) y la venta como transacción con la configuración de producción (reintentos y MARS): folio devuelto al rechazarla, aviso en tiempo real después del commit y la copia tardía de una venta sin conexión deshecha completa (`SaleTransactionSqlServerTests`, Fase 4G). `TransactionBehaviorTests` (SQLite) prueba commit, rollback, reintento y unión a una transacción abierta.

No hay pruebas de pipeline HTTP completo; la integración con SQL Server real cubre folios y proyecciones. El workflow `CI` compila y ejecuta las pruebas, valida migraciones pendientes, aplica las migraciones en SQL Server 2022 con el script y el `sqlcmd` de producción (job `Migrations`, ver [migraciones](database-migrations.md)) y corre lint, pruebas, build y `npm audit` del cliente en cada PR; el despliegue repite build, pruebas y lint antes de publicar. Producción y pruebas apuntan a .NET 10.

## Pirámide esperada

1. **Domain:** invariantes, state machines, cálculos, cancelaciones y pagos.
2. **Application:** handlers, validadores, autorización/capabilities, errores, idempotencia y concurrencia.
3. **Integración:** EF Core contra SQL Server compatible, filtros tenant, migraciones, constraints y persistencia de agregados.
4. **API:** autenticación, autorización, contratos HTTP, archivos, hubs y aislamiento multi-tenant.
5. **Frontend:** componentes críticos, formularios, rutas/guards y estados de error.

## Casos prioritarios faltantes

- Matriz cross-tenant de lectura/escritura/eliminación/descarga por cada recurso.
- Bypass con IDs, `IgnoreQueryFilters()`, `SUPER_ADMIN`, headers, hubs y webhooks.
- Concurrencia de stock, pagos, compra y rowversion (los folios ya tienen prueba contra SQL Server).
- Reintentos/idempotencia de movimientos, WhatsApp y eventos post-save.
- CORS/cookies/refresh/2FA y revocación.
- Validación real de uploads y autorización de descarga (el borrado confinado por tenant en Blob/local ya tiene pruebas).
- Paginación y cancelación de requests (el contrato `ProblemDetails` ya tiene pruebas y CI verifica el OpenAPI).
- Flujo frontend autenticación/capabilities/permisos.

## Reglas

- Cada bug corregido añade regresión que falla antes del fix.
- Cada nueva regla de dominio tiene pruebas positivas y negativas.
- No debilites assertions ni ocultes tests.
- No uses base compartida, credenciales ni datos reales.
- Tests multi-tenant usan al menos dos tenants y objetos con IDs conocidos.
- Separa tests unitarios de dependencias externas y reserva tests de integración para comportamiento que el provider falso no reproduce.

## Comandos

```powershell
dotnet test tests/FoodParkDigital.Domain.Tests/FoodParkDigital.Domain.Tests.csproj
dotnet test tests/FoodParkDigital.Application.Tests/FoodParkDigital.Application.Tests.csproj
dotnet test tests/FoodParkDigital.Infrastructure.Tests/FoodParkDigital.Infrastructure.Tests.csproj
dotnet test tests/FoodParkDigital.WebApi.Tests/FoodParkDigital.WebApi.Tests.csproj
dotnet test FoodParkDigital.sln
```
