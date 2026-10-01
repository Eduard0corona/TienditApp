# Estrategia de pruebas

## Estado actual

Existen cuatro proyectos xUnit:

- `FoodParkDigital.Domain.Tests`: máquinas de estado de órdenes y catálogo de permisos.
- `FoodParkDigital.Application.Tests`: casos de órdenes/clientes, capabilities, tenant access, inventario, compras/caja, WhatsApp, empleados, aislamiento de archivos por tenant y configuración EF mediante SQLite/InMemory y dobles. `SqliteTestDatabase` cubre constraints relacionales (índices únicos) que InMemory ignora; no reemplaza a SQL Server. `Support/SqliteTestHost` arma SQLite en memoria con FKs activas, una estrategia de ejecución con reintentos, Identity real con las opciones de producción e inyección de fallos (commit transitorio, comando fallido, escritura concurrente) para flujos transaccionales; no sustituye pruebas contra SQL Server.
- `FoodParkDigital.Infrastructure.Tests`: servicios de Infrastructure con dobles (directorio temporal, HTTP, SMTP en loopback, logger y EF InMemory); cubre el confinamiento por tenant del almacenamiento local y de la resolución de blobs de Azure, y que SMS, correo, WhatsApp y la resolución del cliente de un pedido no registren códigos OTP, cuerpos, respuestas de proveedores ni PII fuera de Development.
- `FoodParkDigital.WebApi.Tests`: verificación de firma del webhook de WhatsApp y su controller, sin pipeline HTTP completo.

No se encontraron pruebas de pipeline HTTP completo, Infrastructure con SQL Server real compatible ni frontend. El workflow de despliegue restaura/publica backend y construye frontend, pero no ejecuta tests ni lint. Los proyectos de producción apuntan a .NET 9 mientras tests apuntan a .NET 10.

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
