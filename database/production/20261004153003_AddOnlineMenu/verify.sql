-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004150717_AddProductOptionsAndOpenTabs; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: columna nueva. Antes de aplicar no devuelve filas; después, una (vacío
--    otra vez después de rollback.sql).
SELECT c.[name] AS [Columna]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'OnlineMenuMode';

-- 3. Después: negocios por modo del menú en línea (0 = apagado, 1 = solo menú, 2 = menú con
--    pedidos). Justo después de aplicar, todos en 0.
IF COL_LENGTH(N'dbo.TenantSettings', N'OnlineMenuMode') IS NOT NULL
    EXEC(N'SELECT [OnlineMenuMode], COUNT(*) AS [Negocios] FROM [TenantSettings] GROUP BY [OnlineMenuMode] ORDER BY [OnlineMenuMode];');

-- 4. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004153003_AddOnlineMenu';
