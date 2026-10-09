-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261006201255_DropStockAndLoyaltyColumns (Fase 13); si es anterior, apply.sql también
--    aplicará las que falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: la columna que la versión nueva deja de usar sigue ahí, igual que antes, al
--    aplicar y al revertir: [RequireOpenCashSession] (bit, sin nulos, con el valor por omisión 0
--    que reciben las configuraciones nuevas). La siguiente entrega la borra.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[is_nullable] AS [AceptaNulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'RequireOpenCashSession';

-- 3. Antes y después: cuántos negocios tenían activada la política. No cambia al aplicar ni al
--    revertir. Su valor ya no cambia nada: desde el PR #40 todo cobro y reembolso exige caja, la
--    tuvieran activada o no.
IF COL_LENGTH(N'dbo.TenantSettings', N'RequireOpenCashSession') IS NOT NULL
    EXEC(N'SELECT
    COUNT(*) AS [Negocios],
    COUNT(CASE WHEN [RequireOpenCashSession] = 1 THEN 1 END) AS [ConPoliticaActivada]
FROM [TenantSettings];');

-- 4. Después: la migración quedó registrada (vacío antes de aplicar y después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261009041644_StopMappingRequireOpenCashSession';
