-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004163845_AddPlansAndSubscriptions; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columnas nuevas. Antes de aplicar no devuelve filas; después, dos (vacío
--    otra vez después de rollback.sql).
SELECT c.[name] AS [Columna]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Orders') AND c.[name] IN (N'ClientRequestId', N'CapturedOfflineAt')
ORDER BY c.[name];

-- 3. Después: el índice único filtrado por clave de venta (vacío después de rollback.sql).
SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Orders') AND i.[name] = N'IX_Orders_TenantId_ClientRequestId';

-- 4. Después: ventas con clave y capturadas sin conexión. Justo después de aplicar, ambas en 0.
--    Antes de rollback.sql, [CapturadasSinConexion] debe ser 0 (si no, el script se detiene sin
--    cambiar nada, error 50005).
IF COL_LENGTH(N'dbo.Orders', N'ClientRequestId') IS NOT NULL
    EXEC(N'SELECT
    COUNT([ClientRequestId]) AS [VentasConClave],
    COUNT([CapturedOfflineAt]) AS [CapturadasSinConexion]
FROM [Orders];');

-- 5. Después: migraciones de la Fase 3. Deben aparecer las seis.
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] IN (
    N'20261004142352_AddDecimalQuantitiesAndCfdiData',
    N'20261004150717_AddProductOptionsAndOpenTabs',
    N'20261004153003_AddOnlineMenu',
    N'20261004160303_AddBranchesAndDailySalesSnapshots',
    N'20261004163845_AddPlansAndSubscriptions',
    N'20261004165450_AddOfflineSaleIdempotency')
ORDER BY [MigrationId];
