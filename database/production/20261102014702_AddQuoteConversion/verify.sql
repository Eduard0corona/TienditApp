-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29D), la más reciente es
--    20261101181148_AddQuoteDeliveries. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261102014702_AddQuoteConversion.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas [Quotes].[OrderId] (uniqueidentifier), [ConvertedAt] (datetimeoffset) y
--    [AppliedVisitCredit] (decimal), las tres nulas, el índice IX_Quotes_OrderId, la llave a
--    [Orders] y CK_Quotes_Conversion. Antes de aplicar y después de rollback.sql no existen.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Quotes')
    AND c.[name] IN (N'OrderId', N'ConvertedAt', N'AppliedVisitCredit')
ORDER BY c.[name];

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Quotes') AND i.[name] = N'IX_Quotes_OrderId';

SELECT fk.[name] AS [Llave], OBJECT_NAME(fk.[referenced_object_id]) AS [Referencia]
FROM sys.foreign_keys fk
WHERE fk.[name] = N'FK_Quotes_Orders_OrderId';

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_Quotes_Conversion';

-- 3. Después: lo que rollback.sql perdería; se detiene con 50053 si no es cero. Justo después de
--    aplicar: cero. Antes de aplicar y después de rollback.sql las columnas no existen y esa parte
--    no corre.
IF COL_LENGTH(N'dbo.Quotes', N'OrderId') IS NOT NULL
    AND COL_LENGTH(N'dbo.Quotes', N'ConvertedAt') IS NOT NULL
    AND COL_LENGTH(N'dbo.Quotes', N'AppliedVisitCredit') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [Quotes] WHERE [OrderId] IS NOT NULL OR [ConvertedAt] IS NOT NULL OR [AppliedVisitCredit] IS NOT NULL) AS [CotizacionesConvertidas],
        (SELECT COUNT(*) FROM [Quotes] WHERE [AppliedVisitCredit] IS NOT NULL) AS [ConVisitaAplicada];');
