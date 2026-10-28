-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas; nunca
-- muestran datos fiscales de los clientes.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28G), la más reciente es
--    20261026145713_AddInvoiceSubstitution. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261026201123_AddIssuedCfdiDateIndexes.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: un índice [IX_{tabla}_TenantId_IssuedAt] en cada una de las cuatro tablas de CFDI,
--    no único y sin filtro, sobre ([TenantId], [IssuedAt]). Antes de aplicar y después de
--    rollback.sql: ninguno.
SELECT
    OBJECT_NAME(i.[object_id]) AS [Tabla],
    i.[name] AS [Indice],
    i.[is_unique] AS [Unico],
    i.[has_filter] AS [ConFiltro],
    STUFF((
        SELECT N', ' + c.[name]
        FROM sys.index_columns ic
        INNER JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
        WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
        ORDER BY ic.[key_ordinal]
        FOR XML PATH(N'')), 1, 2, N'') AS [Columnas]
FROM sys.indexes i
WHERE i.[name] IN (
    N'IX_OrderInvoices_TenantId_IssuedAt', N'IX_GlobalInvoices_TenantId_IssuedAt',
    N'IX_PaymentComplements_TenantId_IssuedAt', N'IX_CreditNotes_TenantId_IssuedAt')
ORDER BY [Tabla];

-- 3. Antes y después: cuántos CFDI hay en cada tabla, para estimar lo que tarda cada índice. La
--    migración no cambia ninguno.
IF OBJECT_ID(N'dbo.CreditNotes') IS NOT NULL
    EXEC(N'SELECT N''OrderInvoices'' AS [Tabla], COUNT(*) AS [Cfdi] FROM [OrderInvoices]
        UNION ALL SELECT N''GlobalInvoices'', COUNT(*) FROM [GlobalInvoices]
        UNION ALL SELECT N''PaymentComplements'', COUNT(*) FROM [PaymentComplements]
        UNION ALL SELECT N''CreditNotes'', COUNT(*) FROM [CreditNotes];');
