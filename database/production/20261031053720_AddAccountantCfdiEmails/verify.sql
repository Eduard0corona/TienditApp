-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28N), la más reciente es
--    20261030215308_AddCfdiCancellationNotices. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261031053720_AddAccountantCfdiEmails.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas [InvoicingProfiles].[AccountantEmail] (nvarchar(254), nula) y
--    [SendsMonthToAccountant] (bit, no nula, valor por omisión 0), la restricción
--    [CK_InvoicingProfiles_Accountant], la tabla [CfdiMonthEmails] con sus restricciones y el
--    índice único filtrado [UX_CfdiMonthEmails_Monthly]. Antes de aplicar y después de
--    rollback.sql no hay nada.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[max_length] AS [Largo],
    c.[is_nullable] AS [Nulo],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc
    ON dc.[parent_object_id] = c.[object_id] AND dc.[parent_column_id] = c.[column_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.InvoicingProfiles')
    AND c.[name] IN (N'AccountantEmail', N'SendsMonthToAccountant')
ORDER BY c.[name];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_InvoicingProfiles_Accountant'
    OR cc.[parent_object_id] = OBJECT_ID(N'dbo.CfdiMonthEmails')
ORDER BY cc.[name];

SELECT i.[name] AS [Indice], i.[is_unique] AS [Unico], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.CfdiMonthEmails') AND i.[name] IS NOT NULL
ORDER BY i.[name];

-- 3. Después: los envíos al contador por estado (mandándose, mandados, no se mandaron) y cuántos
--    fueron los de cada mes, solos. rollback.sql se detiene con 50048 si hay alguno. Justo después
--    de aplicar: todo en cero. Antes de aplicar y después de rollback.sql la tabla no existe y la
--    consulta no corre.
IF OBJECT_ID(N'dbo.CfdiMonthEmails') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [CfdiMonthEmails] WHERE [SentAt] IS NULL AND [FailedAt] IS NULL) AS [Mandandose],
        (SELECT COUNT(*) FROM [CfdiMonthEmails] WHERE [SentAt] IS NOT NULL) AS [Mandados],
        (SELECT COUNT(*) FROM [CfdiMonthEmails] WHERE [FailedAt] IS NOT NULL) AS [NoSeMandaron],
        (SELECT COUNT(*) FROM [CfdiMonthEmails] WHERE [RequestedByUserId] IS NULL) AS [CadaMes];');

-- 4. Después: cuántos negocios tienen el correo del contador y cuántos le mandan cada mes. Justo
--    después de aplicar: los dos en cero.
IF COL_LENGTH(N'dbo.InvoicingProfiles', N'AccountantEmail') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [InvoicingProfiles] WHERE [AccountantEmail] IS NOT NULL) AS [ConContador],
        (SELECT COUNT(*) FROM [InvoicingProfiles] WHERE [SendsMonthToAccountant] = 1) AS [CadaMes];');
