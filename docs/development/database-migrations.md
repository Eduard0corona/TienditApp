# Migraciones de base de datos

## Estado actual

`ApplicationDbContext`, configuraciones y migraciones están en `FoodParkDigital.Infrastructure`. `MigrationsAssembly` apunta a ese assembly. En Development, `Program.cs` ejecuta migración y seed al arranque solo si `Database:AutoMigrateOnStartup`/`Database:SeedOnStartup` están activos y el servidor es local (o `Database:AllowRemoteDatabase=true`); fuera de Development nunca se migra al iniciar.

El CI ejecuta `dotnet ef migrations has-pending-model-changes` en cada PR, por lo que una configuración sin migración rompe el build.

Además, el job `Migrations` de `.github/workflows/ci.yml` aplica las migraciones en SQL Server 2022 (Docker) en cada PR y en cada push a `master`:

1. Crea con `dotnet ef database update` una base en `PRODUCTION_BASELINE`, una migración que producción ya tiene (idealmente la última; hoy `20261004165450_AddOfflineSaleIdempotency`, la de la Fase 3).
2. Le aplica con el `sqlcmd` clásico y `-b` el mismo contenido que un `apply.sql`: el bloque `SET` de los scripts de producción y la salida de `dotnet ef migrations script --idempotent`.
3. Lo ejecuta otra vez con la caché de planes vacía y falla si cambia el historial o algún objeto. Así SQL Server vuelve a compilar todos los lotes, incluidos los que salta, como en una ejecución posterior en producción: un lote viejo que nombra una columna que una migración nueva borró falla ahí.
4. Revierte con el `Down` idempotente hasta `PRODUCTION_BASELINE`, lo ejecuta otra vez (no debe cambiar nada: cada lote tiene que compilar aunque ya no existan las columnas que nombra) y aplica el script de nuevo.
5. Crea otra base vacía con `dotnet ef database update`, como se crea una base nueva.

La contraseña de SA se genera en el job y el runner la enmascara en los logs. Cuando producción avance, sube `PRODUCTION_BASELINE`; un valor más viejo solo prueba más migraciones. Si el job falla, sus scripts quedan como artefacto `migration-scripts` de la ejecución.

Migraciones recientes con notas operativas:

- `CashSessionSingleOpenAndRevision`: agrega `CashSessions.Revision` y el índice único filtrado `IX_CashSessions_TenantId_SingleOpen`. Antes de crear el índice cierra las cajas abiertas duplicadas de un mismo tenant (conserva la más reciente y deja una nota); ese paso de datos no se revierte con `Down`.
- `InventoryConcurrencyTokens`: solo modelo (tokens de concurrencia); no cambia el esquema.
- `AddBranchesAndDailySalesSnapshots` (Fase 3): cada ubicación de inventario se vuelve una sucursal con el mismo Id, y las cajas y órdenes existentes quedan en una sucursal (la de su ubicación o la del primer pago; si no, la predeterminada). Después de aplicarla hay que desactivar las sucursales que solo eran almacén (consulta 5 de su `verify.sql`). Su `Down` se niega (error 50003) si hay sucursales que no venían de una ubicación, salvo la "Sucursal principal" predeterminada sin editar.
- Los `Down` de la Fase 3 que perderían historial se detienen sin cambiar nada: cantidades decimales (50001), mesas de más de 10 caracteres (50002), pagos de planes (50004) y ventas capturadas sin conexión (50005).
- Fase 4: `AlignTenantAdminMemberships` vuelve administradoras a las cuentas `TENANT_ADMIN` cuya única membresía decía personal (su `Down` no cambia datos); `UseMexicanPesosByDefault` cambia `Currency` de `USD` a `MXN` (el `Down` regresa a `USD` todo `MXN`); `AddChargePermissionAndSmsCooldown` agrega `ChargeOrders` a los permisos guardados con `ManageOrders`, salvo cocina (el `Down` los deja: el código anterior ignora permisos que no conoce); `AddFolioSequences` crea el contador `ORD` de cada negocio con su folio más alto. El código anterior numera contando, así que volver a él con huecos en la numeración repetiría folios: la consulta 4 del `verify.sql` de `AddFolioSequences` lista los negocios afectados antes de regresar. El `Down` de `AddPaymentTipAndCashReceived` se detiene (50006) si hay pagos con propina.
- Fase 5: `AddProductStockItems` agrega `SupplyItems.ProductId` (llave a `Products` con `SET NULL`) y el índice único filtrado `(TenantId, ProductId)`: el artículo de inventario que es la existencia de un producto. No cambia datos; su `Down` quita la columna y esos artículos quedan como insumos con su receta de 1 a 1, que el código anterior sigue descontando en cada venta.
- Fase 6: `AddPurchaseOrderClosing` agrega `PurchaseOrders.ClosedAt` y `CloseReason` para las compras que se cierran sin esperar el resto (estado 4). No cambia datos; su `Down` se detiene (50007) si hay compras cerradas así, porque el código anterior no conoce ese estado.
- Fase 8: `RevocableSessions` deja `RefreshTokens.Token` con nulos y agrega `TokenHash` (índice único filtrado), `SessionId` (`DEFAULT NEWID()`: cada token que escriba la versión anterior es su propia sesión) y `RevocationReason`; no cambia datos, y cada token existente conserva su valor y recibe una sesión. `ScopeOrderLinesAndHistoryByTenant` agrega `TenantId` a `OrderItems` y `OrderStatusHistory` con el negocio de su orden (un `UPDATE` por tabla, dentro de `EXEC`); lo que la versión anterior agregue mientras se publica la nueva queda con el valor vacío, oculto por el filtro, hasta que esta lo completa al arrancar (`OrderTenantBackfillService`). Sus `Down` quitan las columnas: el negocio de las partidas sale de su orden y no se pierde nada; los tokens guardados como hash no vuelven a texto, así que esas sesiones terminan.

## Reglas

- Configuraciones EF y migraciones pertenecen a Infrastructure.
- No edites una migración aplicada ni el snapshot manualmente.
- No elimines/recrees bases como proceso normal de actualización.
- Cada cambio de esquema incluye migración descriptiva, revisión SQL y documentación.
- Producción no ejecuta migraciones automáticamente al iniciar; un paso de despliegue controlado aplica y observa el cambio.
- Cambios destructivos usan expand/contract, backfill, compatibilidad de versiones y rollback/roll-forward.
- Índices, claves y precisiones conservan `TenantId` cuando corresponda.
- Consultas aplican filtros y paginación antes de materializar.
- SQL propio (`migrationBuilder.Sql`) que usa columnas agregadas en la misma migración va dentro de `EXEC(N'...')`, en `Up` y en `Down`. En el script idempotente cada migración es un solo lote que SQL Server compila antes de ejecutarlo, y una sentencia que nombra una columna que todavía no existe en una tabla existente hace fallar el lote (`Invalid column name`); en el `Down`, eso pasa al ejecutar `rollback.sql` por segunda vez, cuando la columna ya se borró. `dotnet ef database update` no lo detecta porque ejecuta sentencia por sentencia.

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

El orden es: respaldo, `verify.sql`, `apply.sql` (`sqlcmd -S <servidor> -d <base> <autenticación> -b -i apply.sql`), `verify.sql` y, al final, desplegar el código. Como cada push a `master` despliega, `apply.sql` se ejecuta antes de fusionar la PR. Si se olvida, `GET /health/schema` responde 503 y la prueba de humo del despliegue falla pidiendo el script; con el slot de staging, la versión no pasa a producción ([despliegue](deploy-azure.md)).

Antes de publicarlos:

1. Ejecútalos con `sqlcmd` contra bases desechables (LocalDB o SQL Server en Docker) en los estados posibles de producción: en la versión productiva previa con datos, en la migración anterior y con datos que la migración rechaza. Prueba también los `rollback.sql` de la más nueva a la más vieja.
2. Vuelve a ejecutarlos para confirmar que la segunda vez no hacen nada.

El job `Migrations` repite estos pasos en cada PR sobre una base sin datos y con scripts que genera él mismo; los estados con datos y los archivos de la carpeta se siguen probando a mano.

Los scripts de la Fase 3 se probaron así en SQL Server 2022 (Docker): desde un respaldo en la Fase 0 con negocios con y sin ubicaciones, ventas, pagos y cajas, el `apply.sql` de `20261004165450_AddOfflineSaleIdempotency` aplicó las Fases 1, 2 y 3; los `rollback.sql` revirtieron la Fase 3 (las guardas 50003 y 50005 se detuvieron sin cambiar nada) y volver a aplicar y repetir el script no hizo nada la segunda vez.

Los de la Fase 4 se probaron igual (SQL Server 2022, Docker), desde una base en `20261004165450_AddOfflineSaleIdempotency` con tres negocios (en `USD` y `MXN`, uno con un hueco en sus folios), ventas, un pago, movimientos del día y cuentas con permisos guardados (administrador con una y con dos membresías, cajeros con y sin `ChargeOrders`, cocina): los `verify.sql` mostraron lo que cambia antes y el resultado después; el `apply.sql` de `20261005003444_AddPaymentTipAndCashReceived` aplicó las ocho migraciones; repetirlo, con la caché de planes vacía, y ejecutar el de una carpeta intermedia no cambió nada; la guarda 50006 se detuvo con una propina registrada; los ocho `rollback.sql`, de la más nueva a la más vieja, regresaron la base a la Fase 3 y ejecutarlos otra vez no hizo nada; aplicar de nuevo dejó las ocho migraciones y los contadores.

Los de la Fase 5 se probaron igual (SQL Server 2022, Docker), desde una base creada con `dotnet ef database update` en `20261005003444_AddPaymentTipAndCashReceived` con un negocio, productos con y sin existencia capturada e insumos: el `verify.sql` de `20261005022701_AddProductStockItems` mostró los productos con existencia capturada; el `apply.sql` creó la columna, los dos índices y la llave con `SET_NULL`, y repetirlo con la caché de planes vacía no cambió nada; el índice único rechazó un segundo artículo para el mismo producto; el `rollback.sql` regresó la base a la Fase 4 con los insumos intactos y ejecutarlo otra vez no hizo nada; aplicar de nuevo dejó la migración, y borrar un producto dejó su artículo como insumo.

Los de la Fase 6 se probaron igual (SQL Server 2022, Docker), desde una base creada con `dotnet ef database update` en `20261005022701_AddProductStockItems` con un negocio, un proveedor y una orden de compra en cada estado que conoce el código anterior (borrador, enviada, recibida en parte, recibida y cancelada): el `verify.sql` de `20261005045624_AddPurchaseOrderClosing` mostró la Fase 5 como última migración y las órdenes por estado; el `apply.sql` creó `ClosedAt` (`datetimeoffset`) y `CloseReason` (`nvarchar(500)`), las dos con nulos, sin cambiar ninguna orden, y repetirlo con la caché de planes vacía no cambió nada; con una orden cerrada sin el resto, el `rollback.sql` se detuvo con el error 50007 sin cambiar nada (la consulta 3 la mostró); sin ella, regresó la base a la Fase 5 y ejecutarlo otra vez no hizo nada; aplicar de nuevo dejó la migración con las cinco órdenes intactas.

Los de la Fase 8 se probaron igual (SQL Server 2022, Docker), desde una copia de una base de pruebas en `20261005045624_AddPurchaseOrderClosing` con dos negocios, órdenes con partidas e historial, cuentas y refresh tokens en texto (vigentes, uno reemplazado y uno vencido): el `verify.sql` de `20261005095544_ScopeOrderLinesAndHistoryByTenant` mostró que faltaban las columnas; el `apply.sql`, con el `sqlcmd` clásico, aplicó las dos migraciones, dio a cada partida y cambio de estado el negocio de su orden sin cambiar totales ni importes, y repetirlo no hizo nada. Luego se simularon las escrituras de la versión anterior (una partida, un cambio de estado y un token, sin las columnas nuevas): quedaron sin negocio y sin hash hasta que la versión nueva, al arrancar, completó su negocio y convirtió los tokens a hash. Con la versión en producción (`master`) sobre esa base, como en un swap de regreso, renovar una sesión guardada como hash respondió 401, y iniciar sesión y cambiar la contraseña funcionaron (revocando también las sesiones de la versión nueva); con un `Token` nulo, en cambio, cambiar la contraseña respondió 500 (por eso la versión nueva lo deja vacío). De vuelta en la versión nueva, el token emitido por la anterior siguió renovando. El `rollback.sql` regresó la base a la Fase 6 sin perder partidas, importes ni tokens, y aplicar otra vez dejó sin hash, y sin abrir ninguna sesión, los tokens que la versión nueva había guardado como hash; la versión nueva arrancó sin errores e inició sesión.

Las reparaciones de datos sin cambio de esquema van en `database/production/data-repair/<fecha>_<Nombre>/`, con un `verify.sql` de solo lectura y un `README.md` con el procedimiento. Cuando la corrección debe pasar por las reglas del dominio, como compensar inventario, no hay `apply.sql`: se registra desde la aplicación.

## Deuda actual

- `PendingModelChangesWarning` está ignorado en la configuración del DbContext; el chequeo del CI lo compensa, pero no en ejecución local.
- El CI no prueba migraciones con datos ni ejecuta los `apply.sql`/`rollback.sql` versionados: regenera el script, así que un archivo que quedó desactualizado frente a su migración no se detecta.
- La guarda 50005 de `20261004165450_AddOfflineSaleIdempotency` nombra `CapturedOfflineAt` fuera de `EXEC`: si su `rollback.sql` se ejecuta otra vez, ya revertido, falla con `Invalid column name` (no cambia nada). La migración está aplicada en producción y no se edita.
- Falta el proceso productivo: generar `migrations script --idempotent` como artefacto del pipeline, revisarlo y aplicarlo en un paso controlado antes de desplegar.
- La divergencia de ramas puede contener historias de migración distintas; se resuelve en tarea separada antes de promover esquema.
