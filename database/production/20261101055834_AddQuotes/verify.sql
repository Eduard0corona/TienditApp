-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29B), la más reciente es
--    20261101002207_AddHomeVisitBooking. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261101055834_AddQuotes.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las tablas [Quotes] y [QuoteLines], la columna [TenantSettings].[QuoteValidityDays]
--    (int, no nula) y sus restricciones. Antes de aplicar y después de rollback.sql no existen.
SELECT t.[name] AS [Tabla]
FROM sys.tables t
WHERE t.[name] IN (N'Quotes', N'QuoteLines')
ORDER BY t.[name];

SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'QuoteValidityDays';

SELECT cc.[name] AS [Restriccion]
FROM sys.check_constraints cc
WHERE cc.[name] LIKE N'CK[_]Quote%' OR cc.[name] = N'CK_TenantSettings_QuoteValidityDays'
ORDER BY cc.[name];

-- 3. Después: lo que rollback.sql perdería; se detiene con 50051 si alguna no es cero. Justo después
--    de aplicar: todo en cero. Antes de aplicar y después de rollback.sql la tabla y la columna no
--    existen y esa parte no corre.
IF OBJECT_ID(N'dbo.Quotes') IS NOT NULL
    AND COL_LENGTH(N'dbo.TenantSettings', N'QuoteValidityDays') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [Quotes]) AS [Cotizaciones],
        (SELECT COUNT(*) FROM [Quotes] WHERE [Status] = 0) AS [Borradores],
        (SELECT COUNT(*) FROM [QuoteLines]) AS [Renglones],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [QuoteValidityDays] <> 15) AS [NegociosConOtraVigencia];');
