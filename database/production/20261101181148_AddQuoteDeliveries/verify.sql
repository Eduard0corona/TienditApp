-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29C), la más reciente es
--    20261101055834_AddQuotes. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261101181148_AddQuoteDeliveries.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la tabla [QuoteEmails], las columnas [OutgoingMessages].[QuoteId] y
--    [TenantSettings].[QuoteWhatsAppTemplateId] (uniqueidentifier, nulas) y las restricciones.
--    Antes de aplicar y después de rollback.sql la tabla, las columnas y CK_OutgoingMessages_Quote
--    no existen (CK_OutgoingMessages_Cfdi sí, con su regla anterior).
SELECT t.[name] AS [Tabla]
FROM sys.tables t
WHERE t.[name] = N'QuoteEmails';

SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.OutgoingMessages') AND c.[name] = N'QuoteId')
    OR (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'QuoteWhatsAppTemplateId')
ORDER BY [Tabla];

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[name] LIKE N'CK[_]QuoteEmails[_]%' OR cc.[name] IN (N'CK_OutgoingMessages_Cfdi', N'CK_OutgoingMessages_Quote')
ORDER BY cc.[name];

-- 3. Después: lo que rollback.sql perdería; se detiene con 50052 si alguna no es cero. Justo después
--    de aplicar: todo en cero. Antes de aplicar y después de rollback.sql la tabla y las columnas no
--    existen y esa parte no corre.
IF OBJECT_ID(N'dbo.QuoteEmails') IS NOT NULL
    AND COL_LENGTH(N'dbo.OutgoingMessages', N'QuoteId') IS NOT NULL
    AND COL_LENGTH(N'dbo.TenantSettings', N'QuoteWhatsAppTemplateId') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [QuoteEmails]) AS [CorreosDeCotizaciones],
        (SELECT COUNT(*) FROM [OutgoingMessages] WHERE [QuoteId] IS NOT NULL OR [Purpose] = 6) AS [WhatsAppsDeCotizaciones],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [QuoteWhatsAppTemplateId] IS NOT NULL) AS [NegociosConPlantilla];');
