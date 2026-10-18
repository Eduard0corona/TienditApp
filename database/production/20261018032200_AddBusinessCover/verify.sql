-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 24D-1), la más reciente
--    es 20261017225015_AddOnlineBooking. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261018032200_AddBusinessCover.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna nueva ([Tenants].[CoverImageUrl] nvarchar(500) con nulos). [Largo] va en
--    bytes (nvarchar(500) es 1000). Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [AceptaNulos]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.Tenants') AND c.[name] = N'CoverImageUrl';

-- 3. Después: cuántos negocios subieron su portada (rollback.sql se detiene con 50020 si hay
--    alguno) y cuántos no. Justo después de aplicar: todos sin portada. Antes de aplicar y después
--    de rollback.sql la columna no existe y la consulta no corre.
IF COL_LENGTH(N'dbo.Tenants', N'CoverImageUrl') IS NOT NULL
    EXEC(N'SELECT
        SUM(CASE WHEN [CoverImageUrl] IS NOT NULL THEN 1 ELSE 0 END) AS [ConPortada],
        SUM(CASE WHEN [CoverImageUrl] IS NULL THEN 1 ELSE 0 END) AS [SinPortada]
    FROM [Tenants];');
