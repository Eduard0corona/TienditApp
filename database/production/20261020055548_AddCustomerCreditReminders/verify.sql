-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 26B), la más reciente es
--    20261020003342_AddCustomerWriteOffs. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después:
--    20261020055548_AddCustomerCreditReminders.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas ([Customers].[CreditTermDays] int con nulos;
--    [TenantSettings].[CreditRemindersEnabled] bit y [CreditReminderEveryDays] int, sin nulos y
--    con sus valores por omisión; [TenantSettings].[CreditReminderTemplateId] uniqueidentifier con
--    nulos) y las restricciones [CK_Customers_CreditTermDays] y
--    [CK_TenantSettings_CreditReminderEveryDays]. Antes de aplicar y después de rollback.sql:
--    ninguna fila.
SELECT
    OBJECT_NAME(c.[object_id]) AS [Tabla],
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulos],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE (c.[object_id] = OBJECT_ID(N'dbo.Customers') AND c.[name] = N'CreditTermDays')
    OR (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings')
        AND c.[name] IN (N'CreditRemindersEnabled', N'CreditReminderEveryDays', N'CreditReminderTemplateId'))
ORDER BY [Tabla], [Columna];

SELECT
    OBJECT_NAME(cc.[parent_object_id]) AS [Tabla],
    cc.[name] AS [Restriccion],
    cc.[definition] AS [Definicion]
FROM sys.check_constraints cc
WHERE cc.[name] IN (N'CK_Customers_CreditTermDays', N'CK_TenantSettings_CreditReminderEveryDays')
ORDER BY [Tabla];

-- 3. Después: cuántos clientes tienen plazo propio, cuántos negocios encendieron los recordatorios
--    del fiado (o eligieron su plantilla), y cuántas plantillas y mensajes de recordatorio del
--    fiado hay (propósito 3). rollback.sql se detiene con 50028 si alguno no es cero. Justo
--    después de aplicar: todos en cero. Antes de aplicar y después de rollback.sql las columnas no
--    existen y la consulta no corre.
IF COL_LENGTH(N'dbo.Customers', N'CreditTermDays') IS NOT NULL
    AND COL_LENGTH(N'dbo.TenantSettings', N'CreditRemindersEnabled') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [Customers] WHERE [CreditTermDays] IS NOT NULL) AS [ClientesConPlazo],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [CreditRemindersEnabled] = 1) AS [NegociosConRecordatorios],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [CreditReminderTemplateId] IS NOT NULL) AS [NegociosConPlantilla],
        (SELECT COUNT(*) FROM [MessageTemplates] WHERE [Purpose] = 3) AS [Plantillas],
        (SELECT COUNT(*) FROM [OutgoingMessages] WHERE [Purpose] = 3) AS [Recordatorios];');

-- 4. Antes y después de aplicar, igual: lo que los clientes deben por la regla de la Fase 26A,
--    menos lo condonado (Fase 26B), y cuántos negocios tienen plazo. La migración no cambia ningún
--    saldo ni plazo del negocio. Es una referencia para comparar antes y después, no el total de
--    Clientes › Por cobrar: ese no cuenta lo que un repartidor cobra en efectivo (Fase 22D).
SELECT
    CASE WHEN [SoldOnCredit] = 1 THEN N'Fiados' ELSE N'Entregados sin cobrar' END AS [Pedidos],
    COUNT(*) AS [ConSaldo],
    COUNT(DISTINCT CONCAT([TenantId], N'|', [CustomerPhone])) AS [Clientes],
    SUM([Total] - [PaidAmount] - [WrittenOffAmount]) AS [Saldo]
FROM [Orders]
WHERE [Status] <> 99 AND [Total] > [PaidAmount] + [WrittenOffAmount]
    AND [CustomerPhone] IS NOT NULL AND [CustomerPhone] <> N''
    AND ([SoldOnCredit] = 1 OR [Status] IN (6, 7))
GROUP BY CASE WHEN [SoldOnCredit] = 1 THEN N'Fiados' ELSE N'Entregados sin cobrar' END
ORDER BY [Pedidos];

SELECT COUNT(*) AS [NegociosConPlazo]
FROM [TenantSettings]
WHERE [CreditTermDays] IS NOT NULL;
