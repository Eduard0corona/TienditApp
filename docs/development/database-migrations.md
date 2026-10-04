# Migraciones de base de datos

## Estado actual

`ApplicationDbContext`, configuraciones y migraciones están en `FoodParkDigital.Infrastructure`. `MigrationsAssembly` apunta a ese assembly. En Development, `Program.cs` ejecuta migración y seed al arranque solo si `Database:AutoMigrateOnStartup`/`Database:SeedOnStartup` están activos y el servidor es local (o `Database:AllowRemoteDatabase=true`); fuera de Development nunca se migra al iniciar.

El CI ejecuta `dotnet ef migrations has-pending-model-changes` en cada PR, por lo que una configuración sin migración rompe el build.

Migraciones recientes con notas operativas:

- `CashSessionSingleOpenAndRevision`: agrega `CashSessions.Revision` y el índice único filtrado `IX_CashSessions_TenantId_SingleOpen`. Antes de crear el índice cierra las cajas abiertas duplicadas de un mismo tenant (conserva la más reciente y deja una nota); ese paso de datos no se revierte con `Down`.
- `InventoryConcurrencyTokens`: solo modelo (tokens de concurrencia); no cambia el esquema.

## Reglas

- Configuraciones EF y migraciones pertenecen a Infrastructure.
- No edites una migración aplicada ni el snapshot manualmente.
- No elimines/recrees bases como proceso normal de actualización.
- Cada cambio de esquema incluye migración descriptiva, revisión SQL y documentación.
- Producción no ejecuta migraciones automáticamente al iniciar; un paso de despliegue controlado aplica y observa el cambio.
- Cambios destructivos usan expand/contract, backfill, compatibilidad de versiones y rollback/roll-forward.
- Índices, claves y precisiones conservan `TenantId` cuando corresponda.
- Consultas aplican filtros y paginación antes de materializar.

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
  - `XACT_ABORT ON`. EF Core 9 ejecuta todas las migraciones del script en una sola transacción y no lo incluye; sin él, un error que solo termina una sentencia deja que el `COMMIT` confirme lo demás.
- **`rollback.sql`:** el `Down` idempotente de la migración, para usarlo después de volver al código anterior.

El orden es: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y, al final, desplegar el código. Como cada push a `develop` o `master` despliega, `apply.sql` se ejecuta antes de fusionar la PR.

Antes de publicarlos:

1. Ejecútalos con `sqlcmd` contra bases desechables de LocalDB en los estados posibles de producción: vacía, en la migración anterior con datos y con datos que la migración rechaza.
2. Vuelve a ejecutarlos para confirmar que la segunda vez no hacen nada.

Las reparaciones de datos sin cambio de esquema van en `database/production/data-repair/<fecha>_<Nombre>/`, con un `verify.sql` de solo lectura y un `README.md` con el procedimiento. Cuando la corrección debe pasar por las reglas del dominio, como compensar inventario, no hay `apply.sql`: se registra desde la aplicación.

## Deuda actual

- `PendingModelChangesWarning` está ignorado en la configuración del DbContext; el chequeo del CI lo compensa, pero no en ejecución local.
- Falta automatización que valide migraciones contra SQL Server desde cero y desde baseline.
- Falta el proceso productivo: generar `migrations script --idempotent` como artefacto del pipeline, revisarlo y aplicarlo en un paso controlado antes de desplegar.
- La divergencia de ramas puede contener historias de migración distintas; se resuelve en tarea separada antes de promover esquema.
