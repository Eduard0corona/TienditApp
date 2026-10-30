-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 28M), la más reciente es
--    20261030181829_AddCfdiWhatsApp. Si es anterior, revisa primero el verify.sql de su carpeta:
--    apply.sql también aplica las que falten. Después: 20261030215308_AddCfdiCancellationNotices.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [CfdiEmails].[Kind] (int, no nula), su valor por omisión (0) y la
--    restricción [CK_CfdiEmails_Kind]. Antes de aplicar y después de rollback.sql no hay nada.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc
    ON dc.[parent_object_id] = c.[object_id] AND dc.[parent_column_id] = c.[column_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.CfdiEmails') AND c.[name] = N'Kind';

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_CfdiEmails_Kind';

-- 3. Después: los avisos de cancelación por estado (enviándose, enviados, no se enviaron).
--    rollback.sql se detiene con 50047 si hay alguno. Justo después de aplicar: todo en cero. Antes
--    de aplicar y después de rollback.sql la columna no existe y la consulta no corre.
IF COL_LENGTH(N'dbo.CfdiEmails', N'Kind') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [CfdiEmails] WHERE [Kind] = 1 AND [SentAt] IS NULL AND [FailedAt] IS NULL) AS [AvisosEnviandose],
        (SELECT COUNT(*) FROM [CfdiEmails] WHERE [Kind] = 1 AND [SentAt] IS NOT NULL) AS [AvisosEnviados],
        (SELECT COUNT(*) FROM [CfdiEmails] WHERE [Kind] = 1 AND [FailedAt] IS NOT NULL) AS [AvisosNoEnviados];');

-- 4. Antes y después: los correos de CFDI por tipo de CFDI (0 factura, 1 complemento, 2 nota) y
--    cuántos siguen enviándose. La migración no los cambia: todos quedan con [Kind] = 0.
IF OBJECT_ID(N'dbo.CfdiEmails') IS NOT NULL
    EXEC(N'SELECT [Document] AS [Tipo], COUNT(*) AS [Correos],
            SUM(CASE WHEN [SentAt] IS NULL AND [FailedAt] IS NULL THEN 1 ELSE 0 END) AS [Enviandose]
        FROM [CfdiEmails] GROUP BY [Document] ORDER BY [Document];');
