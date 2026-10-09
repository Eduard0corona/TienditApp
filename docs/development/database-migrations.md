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
- Fase 9: `StopUsingPlainRefreshTokenValues` no cambia el esquema. Calcula en SQL el mismo SHA-256 que `RefreshToken.HashOf` para los refresh tokens que aún guardan su valor en texto, marca como rotados los que ya habían sido reemplazados y vacía `Token` y `ReplacedByToken` (todo dentro de `EXEC`). El modelo deja de mapear esas dos columnas, pero ellas y su índice se quedan: la Fase 8 todavía las lee, y los tokens nuevos dejan `Token` en NULL, que ella tolera. Su `Down` no cambia nada.
- Fase 10: `DropPlainRefreshTokenColumns`, escrita a mano porque el modelo ya no tiene esas columnas, borra `IX_RefreshTokens_Token`, `Token` y `ReplacedByToken` (su snapshot no cambia). La versión que corre mientras se aplica (Fase 9) no las mapea. Su `Down` las crea otra vez con nulos y vacías, y el índice: la Fase 8 trata el `Token` nulo como "sin valor en texto", así que se puede volver a ella. Antes de borrar, el `verify.sql` muestra lo que depende de las columnas: una estadística creada a mano bloquearía el `DROP COLUMN` (las automáticas se borran con la columna).
- `StopMappingRequireOpenCashSession` no cambia el esquema: el modelo deja de mapear `TenantSettings.RequireOpenCashSession`, la política opcional que pedía caja abierta para cobrar en efectivo y que nada lee desde que todo cobro exige caja (PR #40). La columna se queda con sus valores: las configuraciones nuevas toman su valor por omisión, 0, y la versión anterior, que la mapea, sigue funcionando. Su `Down` no cambia nada. La siguiente entrega la borra, con esta versión ya desplegada.
- Fase 15: `AddTabItemCancellations` agrega `OrderItems.CancelledQuantity` y `CancelledAmount` (sin nulos, 0 por omisión) y la tabla `OrderItemCancellations`; no cambia datos, y su `Down` los quita (la bitácora conserva cada cancelación). La Fase 15 también dejó de mapear `RequireOpenCashSession`, en su rama, con `20261008074715_StopMappingRequireOpenCashSession`, mientras la PR #47 lo hacía en master con `20261009041644_StopMappingRequireOpenCashSession`. Quedan las dos, porque una base puede tener registrada cualquiera y ninguna cambia nada. En C#, la de la Fase 15 se llama `StopMappingRequireOpenCashSessionPhase15` para que compilen juntas; su Id es el que registraron sus scripts. La migración que borre la columna va después de las dos.
- Fase 16: `AddBusinessTheme` agrega `TenantSettings.Theme` (int, sin nulos, 0 por omisión: Nopal). EF siempre escribe el valor; el de la base solo llena las filas que guarde la versión anterior. Su `Down` quita la columna y con ella el tema que eligió cada negocio (la bitácora conserva cada cambio).
- `DropRequireOpenCashSession`, escrita a mano porque el modelo ya no tiene la columna, borra `TenantSettings.RequireOpenCashSession` con su valor por omisión (su snapshot no cambia). Va después de las dos que dejaron de mapearla: la versión que corre mientras se aplica (`master` desde la PR #47, con o sin las Fases 14 a 16) no la mapea. Su `Down` la crea otra vez (`bit`, sin nulos, 0 por omisión) para una versión anterior, que la lee: los valores no vuelven, así que la política queda desactivada en todos los negocios (desde la PR #40 ninguna versión la usa).

## Reglas

- Configuraciones EF y migraciones pertenecen a Infrastructure.
- No edites una migración aplicada ni el snapshot manualmente.
- No elimines/recrees bases como proceso normal de actualización.
- Cada cambio de esquema incluye migración descriptiva, revisión SQL y documentación.
- Producción no ejecuta migraciones automáticamente al iniciar; un paso de despliegue controlado aplica y observa el cambio.
- Cambios destructivos usan expand/contract, backfill, compatibilidad de versiones y rollback/roll-forward.
- Índices, claves y precisiones conservan `TenantId` cuando corresponda.
- Consultas aplican filtros y paginación antes de materializar.
- SQL propio (`migrationBuilder.Sql`) que usa columnas agregadas en la misma migración va dentro de `EXEC(N'...')`, en `Up` y en `Down`. En el script idempotente cada migración es un solo lote que SQL Server compila antes de ejecutarlo, y una sentencia que nombra una columna que todavía no existe en una tabla existente hace fallar el lote (`Invalid column name`); en el `Down`, eso pasa al ejecutar `rollback.sql` por segunda vez, cuando la columna ya se borró. `dotnet ef database update` no lo detecta porque ejecuta sentencia por sentencia. Lo mismo vale para el SQL propio que nombra columnas que una migración posterior va a borrar (como `Token` en `StopUsingPlainRefreshTokenValues`): el script idempotente compila todos los lotes, también los que salta.
- Una columna que lee la versión desplegada se borra en dos entregas. La primera deja de mapearla, con una migración que solo sincroniza el snapshot (y, si hace falta, mueve sus datos). La segunda, cuando ya no haya que volver a la versión anterior, la borra.

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

Los de la Fase 9 se probaron igual (SQL Server 2022, Docker), desde una copia de la base de pruebas de la Fase 8, con refresh tokens en texto (uno vigente y uno reemplazado), tokens de la Fase 8 guardados como hash y filas sin hash ni valor que dejó un rollback:
- el `verify.sql` de `20261005175643_StopUsingPlainRefreshTokenValues` mostró los valores en texto;
- el `apply.sql`, con el `sqlcmd` clásico, les dio su hash, marcó como rotado el reemplazado y vació los valores sin cambiar tokens, sesiones ni vigentes; repetirlo no hizo nada;
- con la versión nueva, el token en texto que el script convirtió renovó su sesión (el hash de SQL es el de .NET) y el reemplazado se rechazó como reuso;
- con la Fase 8 sobre esa base, como al volver a ella, renovar una sesión de la versión nueva (con `Token` nulo), cambiar la contraseña y cerrar sesión funcionaron sin errores en el log;
- el `rollback.sql` solo quitó la migración del historial, otra vez no hizo nada, y aplicar de nuevo no cambió datos.

También se simuló la fase que borrará `Token` y `ReplacedByToken`: con las columnas borradas y la caché de planes vacía, el `apply.sql` acumulado siguió compilando y corriendo.

Los de la Fase 10 se probaron igual (SQL Server 2022, Docker), desde la copia de la base de pruebas de la Fase 8 con el `apply.sql` de la Fase 9 aplicado, como producción, y una estadística automática sobre `ReplacedByToken`:
- con la Fase 9 corriendo, el `verify.sql` de `20261006054141_DropPlainRefreshTokenColumns` mostró solo el índice y esa estadística, y valores en texto en 0;
- el `apply.sql`, con el `sqlcmd` clásico, borró las columnas y el índice sin cambiar tokens ni sesiones, y la Fase 9 siguió renovando, iniciando y cerrando sesión, con `/health/schema` sano y sin errores en el log; repetirlo no hizo nada;
- la versión nueva renovó la sesión de la Fase 9, y de vuelta en la Fase 9 se renovó la de la versión nueva;
- el `rollback.sql` creó otra vez las columnas con nulos y el índice, sin cambiar sesiones, y otra vez no hizo nada; la Fase 8 renovó una sesión con `Token` nulo, inició sesión sin escribir valores en texto y sin errores;
- aplicar de nuevo borró las columnas, y la versión nueva renovó la sesión que abrió la Fase 8.

En CI lo prueba también `RefreshTokenSqlServerTests.Migrations_ShouldHashThePlainValuesLeft_ThenDropTheirColumns_WhileTheSessionsKeepWorking`: migra a la Fase 8, siembra tokens en texto, aplica la Fase 9 y la 10 con las sesiones funcionando, revierte a la Fase 8 y vuelve a aplicar.

Los de la Fase 12 se probaron igual (SQL Server 2022, Docker), desde una copia de la base de pruebas en la Fase 10, como producción, con un producto con existencia capturada que no controla existencias, otro con existencia capturada que ya las controla y dos perfiles de cliente, uno con puntos escritos a mano:
- el `verify.sql` de `20261006101452_StopMappingStockAndLoyaltyColumns` mostró las cuatro columnas, con el valor por omisión 0 de `LoyaltyPoints`, y lo capturado;
- el `apply.sql`, con el `sqlcmd` clásico, solo registró la migración y el `verify.sql` mostró las columnas y los conteos sin cambios; repetirlo no hizo nada;
- con la versión nueva, `/health/schema` sano, el catálogo y la tarjeta de existencias sin esos campos, un producto editado conservó lo capturado y uno nuevo quedó con NULL;
- con la Fase 11 sobre esa base, como al volver a ella, `/health/schema` sano: leyó el producto de la versión nueva, siguió proponiendo lo capturado y creó productos;
- el `rollback.sql` solo quitó la migración del historial y el `verify.sql` quedó como antes de aplicar; otra vez no hizo nada, y aplicar de nuevo dejó lo mismo.

Los de la Fase 13 se probaron igual, desde una copia de esa base con la Fase 12 aplicada, como producción, y con estadísticas automáticas sobre `StockQuantity` y `LoyaltyTier`:
- el `verify.sql` de `20261006201255_DropStockAndLoyaltyColumns` mostró como única dependencia la restricción del valor por omisión de `LoyaltyPoints`, las dos estadísticas automáticas y lo capturado que se iba a perder;
- el `apply.sql`, con el `sqlcmd` clásico, borró las cuatro columnas, la restricción y las estadísticas sin cambiar productos ni perfiles; repetirlo con la caché de planes vacía no hizo nada;
- la versión nueva y la Fase 12 corrieron sobre esa base con `/health/schema` sano: catálogo, tarjeta de existencias, editar y crear productos;
- el `rollback.sql` creó otra vez las columnas vacías y la restricción, sin cambiar filas, y otra vez no hizo nada; aplicar de nuevo dejó lo mismo que la primera vez.

En CI lo prueba `UnmappedColumnsSqlServerTests`: con la base en la Fase 12, las filas que guarda esta versión dejan las columnas en NULL y en 0 y lo capturado sobrevive a una edición; después la Fase 13 las borra sin afectar las filas, revertirla las devuelve vacías y aplicarla otra vez las borra de nuevo.

Los de `20261009041644_StopMappingRequireOpenCashSession` se probaron igual (SQL Server 2022, Docker), desde una base en `20261006201255_DropStockAndLoyaltyColumns`, como producción, con tres negocios, dos con la política activada:
- el `verify.sql` mostró la columna (`bit`, sin nulos, 0 por omisión) y los dos negocios con la política;
- el `apply.sql`, con el `sqlcmd` clásico, solo registró la migración, con la columna y sus valores sin cambios; repetirlo con la caché de planes vacía no hizo nada;
- la versión nueva y la desplegada (`master`) corrieron sobre esa base con `/health/schema` sano y leyeron la configuración del negocio (su menú en línea) sin errores en el log;
- el `rollback.sql` solo quitó la migración del historial; otra vez no hizo nada, y aplicar de nuevo dejó lo mismo.

Los de `20261009071957_DropRequireOpenCashSession` se probaron igual (SQL Server 2022, Docker), desde una copia de esa base con el `apply.sql` de la Fase 16 aplicado, como producción después del merge de las Fases 14 a 16, y una estadística automática sobre la columna:
- el `verify.sql` mostró como única dependencia la restricción del valor por omisión y esa estadística, y los dos negocios con la política activada que se iban a perder;
- el `apply.sql`, con el `sqlcmd` clásico, borró la columna, la restricción y la estadística sin cambiar las configuraciones; repetirlo con la caché de planes vacía no hizo nada. Sobre la base en la PR #47, sin las Fases 15 y 16, aplicó las cuatro migraciones que faltaban y dejó el mismo esquema;
- la versión nueva, la de `master` (Fases 14 a 16) y la de la PR #47 corrieron sobre esa base con `/health/schema` sano: leyeron la configuración (el menú en línea) y registraron un negocio sin errores de esquema (el log solo tuvo el correo que no salió, sin servidor SMTP en la prueba); la anterior a la PR #47 falló con `Invalid column name 'RequireOpenCashSession'`;
- el `rollback.sql` creó otra vez la columna (`bit`, sin nulos, 0 por omisión), con la política en 0 en todos los negocios y sin cambiar filas, y con ella la versión anterior a la PR #47 leyó la configuración y registró un negocio; otra vez no hizo nada, y aplicar de nuevo dejó lo mismo que la primera vez.

En CI lo prueba `UnmappedColumnsSqlServerTests.RequireOpenCashSession_ShouldStayWhileUnmapped_ThenGo_AndComeBackAtItsDefaultOnRollback`: con la base en `AddTabItemCancellations`, la última que la mapea, la configuración que guarda esta versión deja la columna en 0 y la que activó una versión anterior conserva su valor; las migraciones que dejan de mapearla no cambian la base; después el borrado no afecta las configuraciones, revertirlo devuelve la columna en 0 (también en lo que esta versión guarda mientras tanto) y aplicarlo otra vez la borra de nuevo.

Las reparaciones de datos sin cambio de esquema van en `database/production/data-repair/<fecha>_<Nombre>/`, con un `verify.sql` de solo lectura y un `README.md` con el procedimiento. Cuando la corrección debe pasar por las reglas del dominio, como compensar inventario, no hay `apply.sql`: se registra desde la aplicación. Cuando basta un cambio de datos acotado, lleva además un `repair.sql` idempotente que lista lo que cambia, como `20261005_StorageUrlsOutsideTheirFolder`.

## Deuda actual

- `PendingModelChangesWarning` está ignorado en la configuración del DbContext; el chequeo del CI lo compensa, pero no en ejecución local.
- El CI no prueba migraciones con datos ni ejecuta los `apply.sql`/`rollback.sql` versionados: regenera el script, así que un archivo que quedó desactualizado frente a su migración no se detecta.
- La guarda 50005 de `20261004165450_AddOfflineSaleIdempotency` nombra `CapturedOfflineAt` fuera de `EXEC`: si su `rollback.sql` se ejecuta otra vez, ya revertido, falla con `Invalid column name` (no cambia nada). La migración está aplicada en producción y no se edita.
- Falta el proceso productivo: generar `migrations script --idempotent` como artefacto del pipeline, revisarlo y aplicarlo en un paso controlado antes de desplegar.
