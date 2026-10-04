# Migraciones de base de datos

## Estado actual

`ApplicationDbContext`, configuraciones y migraciones están en `FoodParkDigital.Infrastructure`. `MigrationsAssembly` apunta a ese assembly. En Development, `Program.cs` ejecuta migración y seed al arranque solo si `Database:AutoMigrateOnStartup`/`Database:SeedOnStartup` están activos y el servidor es local (o `Database:AllowRemoteDatabase=true`); fuera de Development nunca se migra al iniciar.

El CI ejecuta `dotnet ef migrations has-pending-model-changes` en cada PR, por lo que una configuración sin migración rompe el build.

Migraciones recientes con notas operativas:

- `CashSessionSingleOpenAndRevision`: agrega `CashSessions.Revision` y el índice único filtrado `IX_CashSessions_TenantId_SingleOpen`. Antes de crear el índice cierra las cajas abiertas duplicadas de un mismo tenant (conserva la más reciente y deja una nota); ese paso de datos no se revierte con `Down`.
- `InventoryConcurrencyTokens`: solo modelo (tokens de concurrencia); no cambia el esquema.
- `AddBranchesAndDailySalesSnapshots` (Fase 3): cada ubicación de inventario se vuelve una sucursal con el mismo Id, y las cajas y órdenes existentes quedan en una sucursal (la de su ubicación o la del primer pago; si no, la predeterminada). Después de aplicarla hay que desactivar las sucursales que solo eran almacén (consulta 5 de su `verify.sql`). Su `Down` se niega (error 50003) si hay sucursales que no venían de una ubicación, salvo la "Sucursal principal" predeterminada sin editar.
- Los `Down` de la Fase 3 que perderían historial se detienen sin cambiar nada: cantidades decimales (50001), mesas de más de 10 caracteres (50002), pagos de planes (50004) y ventas capturadas sin conexión (50005).

## Reglas

- Configuraciones EF y migraciones pertenecen a Infrastructure.
- No edites una migración aplicada ni el snapshot manualmente.
- No elimines/recrees bases como proceso normal de actualización.
- Cada cambio de esquema incluye migración descriptiva, revisión SQL y documentación.
- Producción no ejecuta migraciones automáticamente al iniciar; un paso de despliegue controlado aplica y observa el cambio.
- Cambios destructivos usan expand/contract, backfill, compatibilidad de versiones y rollback/roll-forward.
- Índices, claves y precisiones conservan `TenantId` cuando corresponda.
- Consultas aplican filtros y paginación antes de materializar.
- SQL propio (`migrationBuilder.Sql`) que usa columnas agregadas en la misma migración va dentro de `EXEC(N'...')`. En el script idempotente cada migración es un solo lote que SQL Server compila antes de ejecutarlo, y una sentencia que nombra una columna que todavía no existe en una tabla existente hace fallar el lote (`Invalid column name`). `dotnet ef database update` no lo detecta porque ejecuta sentencia por sentencia.

## Comandos de desarrollo

Con `dotnet-ef` instalado:

```powershell
dotnet ef migrations add NombreDescriptivo --project src/FoodParkDigital.Infrastructure --startup-project src/FoodParkDigital.WebApi
dotnet ef database update --project src/FoodParkDigital.Infrastructure --startup-project src/FoodParkDigital.WebApi
dotnet ef migrations script --idempotent --project src/FoodParkDigital.Infrastructure --startup-project src/FoodParkDigital.WebApi
```

Revisa el código/SQL generado, prueba desde la versión productiva previa y confirma datos/rollback. No ejecutes `database drop` sin autorización explícita y backup verificado.

## Scripts para producción

Como producción no migra al arrancar, cada migración nueva deja en `database/production/<MigrationId>/` los scripts que aplica el despliegue:

- **`verify.sql`:** consultas de solo lectura para antes y después. Muestra las migraciones aplicadas, los datos que harían fallar la migración y las filas afectadas.
- **`apply.sql`:** la salida de `dotnet ef migrations script --idempotent`, que aplica solo las migraciones que faltan en `__EFMigrationsHistory`. Va precedida de un bloque `SET` con dos partes:
  - las opciones que exigen los índices filtrados, porque el `sqlcmd` clásico arranca con `QUOTED_IDENTIFIER OFF`;
  - `XACT_ABORT ON`. EF Core 10 envuelve cada migración del script en su propia transacción (EF Core 9 usaba una sola para todas) y no lo incluye; sin él, un error que solo termina una sentencia deja que el `COMMIT` confirme el resto de esa migración. Si una migración falla, las anteriores ya quedaron confirmadas: corrige la causa y vuelve a ejecutar el script, que salta las aplicadas.
- **`rollback.sql`:** el `Down` idempotente de la migración, para usarlo después de volver al código anterior.

Los `apply.sql` son para la base de producción existente (ya en la Fase 0 o después): el de la última carpeta aplica de una vez todo lo que falte. Una base nueva y vacía se crea con `dotnet ef database update` (o con el arranque en Development): con EF Core 10, el script idempotente desde cero falla en `20260217004713_AddOrderServiceAndTenantCapabilities`, que actualiza en el mismo lote una columna que acaba de agregar. Esa migración ya está aplicada en producción y no se edita.

El orden es: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y, al final, desplegar el código. Como cada push a `master` despliega, `apply.sql` se ejecuta antes de fusionar la PR.

Antes de publicarlos:

1. Ejecútalos con `sqlcmd` contra bases desechables (LocalDB o SQL Server en Docker) en los estados posibles de producción: en la versión productiva previa con datos, en la migración anterior y con datos que la migración rechaza. Prueba también los `rollback.sql` de la más nueva a la más vieja.
2. Vuelve a ejecutarlos para confirmar que la segunda vez no hacen nada.

Los scripts de la Fase 3 se probaron así en SQL Server 2022 (Docker): desde un respaldo en la Fase 0 con negocios con y sin ubicaciones, ventas, pagos y cajas, el `apply.sql` de `20261004165450_AddOfflineSaleIdempotency` aplicó las Fases 1, 2 y 3; los `rollback.sql` revirtieron la Fase 3 (las guardas 50003 y 50005 se detuvieron sin cambiar nada) y volver a aplicar y repetir el script no hizo nada la segunda vez.

Las reparaciones de datos sin cambio de esquema van en `database/production/data-repair/<fecha>_<Nombre>/`, con un `verify.sql` de solo lectura y un `README.md` con el procedimiento. Cuando la corrección debe pasar por las reglas del dominio, como compensar inventario, no hay `apply.sql`: se registra desde la aplicación.

## Deuda actual

- `PendingModelChangesWarning` está ignorado en la configuración del DbContext; el chequeo del CI lo compensa, pero no en ejecución local.
- Falta automatización que valide migraciones contra SQL Server desde cero y desde baseline.
- Falta el proceso productivo: generar `migrations script --idempotent` como artefacto del pipeline, revisarlo y aplicarlo en un paso controlado antes de desplegar.
- La divergencia de ramas puede contener historias de migración distintas; se resuelve en tarea separada antes de promover esquema.
