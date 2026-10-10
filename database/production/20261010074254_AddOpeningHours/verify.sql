-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 18), la más reciente es
--    20261009071957_DropRequireOpenCashSession. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: [TenantSettings].[OpeningHours], nvarchar(1000) ([LongitudMaxima] 2000, en bytes), con
--    nulos y sin valor por omisión. Antes de aplicar y después de rollback.sql: ninguna fila.
SELECT
    c.[name] AS [Columna],
    t.[name] AS [Tipo],
    c.[max_length] AS [LongitudMaxima],
    c.[is_nullable] AS [AceptaNulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'OpeningHours';

-- 3. Después: negocios con horario. Justo después de aplicar, [ConHorario] es 0: nadie se ve cerrado
--    hasta que su dueño captura el horario. Antes de rollback.sql: los horarios que se perderían.
IF COL_LENGTH(N'dbo.TenantSettings', N'OpeningHours') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Negocios], COUNT([OpeningHours]) AS [ConHorario]
FROM [TenantSettings];');

-- 4. Después: quedó registrada la migración. Después de rollback.sql: ninguna fila.
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010074254_AddOpeningHours';
