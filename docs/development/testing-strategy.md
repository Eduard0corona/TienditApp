# Estrategia de pruebas

## Estado actual

Existen cuatro proyectos xUnit:

- `FoodParkDigital.Domain.Tests`: máquinas de estado, cancelación/pagos de órdenes, caja, producto, configuración de WhatsApp, catálogo de permisos y valores persistidos de los enums de inventario.
- `FoodParkDigital.Application.Tests`: casos de órdenes/clientes, capabilities, tenant access, inventario, compras/caja, reportes, uploads, WhatsApp, empleados, aislamiento de archivos por tenant, configuración de arranque y EF mediante SQLite/InMemory y dobles. `SqliteTestDatabase` cubre constraints relacionales (índices únicos) que InMemory ignora; no reemplaza a SQL Server. `Support/SqliteTestHost` arma SQLite en memoria con FKs activas, una estrategia de ejecución con reintentos, Identity real con las opciones de producción e inyección de fallos (commit transitorio, comando fallido, escritura concurrente) para flujos transaccionales; no sustituye pruebas contra SQL Server. Ambos emulan el `rowversion` de `WhatsAppConversation` (valor aleatorio al insertar y trigger al actualizar), así que guardar una copia desactualizada falla como en SQL Server.
- `FoodParkDigital.Infrastructure.Tests`: servicios de Infrastructure con dobles (directorio temporal, HTTP, SMTP en loopback, logger, EF InMemory y `SqlException` construidas por reflexión con mensajes reales de SQL Server); cubre el confinamiento por tenant del almacenamiento local y de la resolución de blobs de Azure, y que SMS, correo, WhatsApp y la resolución del cliente de un pedido no registren códigos OTP, cuerpos, respuestas de proveedores ni PII fuera de Development. `DatabaseErrorLoggingTests` usa el registro de `AddInfrastructureServices` con un interceptor que responde en lugar de SQL Server para comprobar que EF Core no registra los mensajes de guardados y consultas fallidos fuera de Development.
- `FoodParkDigital.WebApi.Tests`: middleware de tenant, acceso a hubs, controller de órdenes por tenant, verificación de firma del webhook de WhatsApp y su controller, y `GlobalExceptionHandlerMiddleware` (errores de base de datos registrados sin mensajes fuera de Development, `traceId` en `ProblemDetails`), sin pipeline HTTP completo. Comparte dobles de `Infrastructure.Tests/Support` enlazando sus archivos. `ClientEnumContractTests` lee `client/src/types/index.ts` y compara cada copia de un enum del dominio con el original, porque la API los intercambia como números.

No hay pruebas de pipeline HTTP completo, de integración con SQL Server real ni de frontend. El workflow `CI` compila y ejecuta las pruebas, valida migraciones pendientes y corre lint, build y `npm audit` del cliente en cada PR; el despliegue repite build, tests y lint antes de publicar. Producción y pruebas apuntan a .NET 10.

## Pirámide esperada

1. **Domain:** invariantes, state machines, cálculos, cancelaciones y pagos.
2. **Application:** handlers, validadores, autorización/capabilities, errores, idempotencia y concurrencia.
3. **Integración:** EF Core contra SQL Server compatible, filtros tenant, migraciones, constraints y persistencia de agregados.
4. **API:** autenticación, autorización, contratos HTTP, archivos, hubs y aislamiento multi-tenant.
5. **Frontend:** componentes críticos, formularios, rutas/guards y estados de error.

## Casos prioritarios faltantes

- Matriz cross-tenant de lectura/escritura/eliminación/descarga por cada recurso.
- Bypass con IDs, `IgnoreQueryFilters()`, `SUPER_ADMIN`, headers, hubs y webhooks.
- Concurrencia de stock, folios, pagos, compra y rowversion.
- Reintentos/idempotencia de movimientos, WhatsApp y eventos post-save.
- CORS/cookies/refresh/2FA y revocación.
- Validación real de uploads y autorización de descarga (el borrado confinado por tenant en Blob/local ya tiene pruebas).
- Contratos `ProblemDetails`, paginación y cancelación de requests.
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
