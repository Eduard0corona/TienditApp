-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24D-2, que no trajo
--    migración), la más reciente es 20261018032200_AddBusinessCover. Si es anterior, revisa primero
--    el verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261018081803_AddFavoriteBusinesses.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla nueva con sus índices (el único por cuenta y negocio, y el de la llave
--    hacia [Tenants]). Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.FavoriteBusinesses') AND i.[name] IS NOT NULL
ORDER BY i.[name];

-- 3. Después: cuántos favoritos guardaron las cuentas y cuántas cuentas tienen alguno
--    (rollback.sql se detiene con 50021 si hay uno). Justo después de aplicar: ninguno. Antes de
--    aplicar y después de rollback.sql la tabla no existe y la consulta no corre.
IF OBJECT_ID(N'dbo.FavoriteBusinesses') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Favoritos], COUNT(DISTINCT [UserId]) AS [Cuentas] FROM [FavoriteBusinesses];');
