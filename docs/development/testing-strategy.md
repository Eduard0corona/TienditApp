# Estrategia de pruebas

## Estado actual

Existen tres proyectos xUnit:

- `FoodParkDigital.Domain.Tests`: máquinas de estado, cancelación/pagos de órdenes, caja y catálogo de permisos.
- `FoodParkDigital.Application.Tests`: casos de órdenes/clientes, capabilities, tenant access, inventario, compras/caja, reportes, uploads, WhatsApp, empleados, configuración de arranque y EF mediante InMemory y dobles.
- `FoodParkDigital.WebApi.Tests`: middleware de tenant, acceso a hubs y controller de órdenes por tenant.

No hay pruebas de integración con SQL Server real ni de frontend. El workflow `CI` compila y ejecuta las pruebas, valida migraciones pendientes y corre lint, build y `npm audit` del cliente en cada PR; el despliegue repite build, tests y lint antes de publicar. Los proyectos de producción apuntan a .NET 9 mientras tests apuntan a .NET 10.

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
- Validación real de uploads y aislamiento de rutas Blob/local.
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
dotnet test FoodParkDigital.sln
```
