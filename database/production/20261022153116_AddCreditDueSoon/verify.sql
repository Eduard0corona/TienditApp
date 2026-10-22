-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 22J), la más reciente es
--    20261022033401_AddPlatformWithholdingCertificates. Si es anterior, revisa primero el verify.sql
--    de su carpeta: apply.sql también aplica las que falten. Después: 20261022153116_AddCreditDueSoon.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([TenantSettings].[CreditDueSoonEnabled] bit y
--    [CreditDueSoonDaysBefore] int, sin nulos y con sus valores por omisión;
--    [CreditDueSoonTemplateId] uniqueidentifier con nulos) y la restricción
--    [CK_TenantSettings_CreditDueSoonDaysBefore]. Antes de aplicar y después de rollback.sql:
--    ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings')
    AND c.[name] IN (N'CreditDueSoonEnabled', N'CreditDueSoonDaysBefore', N'CreditDueSoonTemplateId')
ORDER BY [Columna];

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_TenantSettings_CreditDueSoonDaysBefore';

-- 3. Después: cuántos negocios encendieron el aviso antes de vencer (o eligieron su plantilla), y
--    cuántas plantillas y avisos hay con ese propósito (4). rollback.sql se detiene con 50034 si
--    alguno no es cero. Justo después de aplicar: todos en cero. Antes de aplicar y después de
--    rollback.sql las columnas no existen y la consulta no corre.
IF COL_LENGTH(N'dbo.TenantSettings', N'CreditDueSoonEnabled') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [CreditDueSoonEnabled] = 1) AS [NegociosConAviso],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [CreditDueSoonTemplateId] IS NOT NULL) AS [NegociosConPlantilla],
        (SELECT COUNT(*) FROM [MessageTemplates] WHERE [Purpose] = 4) AS [Plantillas],
        (SELECT COUNT(*) FROM [OutgoingMessages] WHERE [Purpose] = 4) AS [Avisos];');

-- 4. Antes y después de aplicar, igual: los recordatorios de lo vencido (Fase 26C) de cada negocio
--    y los mensajes que ya mandaron. La migración no los cambia.
SELECT
    COUNT(*) AS [Negocios],
    SUM(CASE WHEN [CreditRemindersEnabled] = 1 THEN 1 ELSE 0 END) AS [ConRecordatorios],
    SUM(CASE WHEN [CreditTermDays] IS NOT NULL THEN 1 ELSE 0 END) AS [ConPlazo]
FROM [TenantSettings];

SELECT [Status] AS [Estado], COUNT(*) AS [Recordatorios]
FROM [OutgoingMessages]
WHERE [Purpose] = 3
GROUP BY [Status]
ORDER BY [Status];
