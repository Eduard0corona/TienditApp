-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29V y las fases sin
--    migración que siguen), la más reciente es 20261107190735_AddAppointmentMarkedFakeAt. Si es anterior,
--    revisa primero el verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261108150147_AddOrderCancellationEmailsAndAnswerReminder.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas nuevas [TenantUsers].[OrderCancellationEmails] (bit, admite NULL, sin
--    valor por omisión) y [TenantSettings].[AnswerReminderMinutes] (int, no admite NULL, 60 por
--    omisión). Antes de aplicar y después de rollback.sql no devuelve filas.
SELECT OBJECT_NAME(c.[object_id]) AS [Tabla], c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo], OBJECT_DEFINITION(c.[default_object_id]) AS [PorOmision]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantUsers') AND c.[name] = N'OrderCancellationEmails')
    OR (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'AnswerReminderMinutes')
ORDER BY [Tabla], [Columna];

-- 3. Antes y después: cuántas membresías y negocios hay. Ya aplicada: cuántas personas eligieron
--    recibir los pedidos cancelados (justo después, ninguna) y cuántos negocios eligieron cada
--    anticipación del recordatorio (justo después, todos en 60). rollback.sql borra las columnas.
SELECT (SELECT COUNT(*) FROM [TenantUsers]) AS [Membresias], (SELECT COUNT(*) FROM [TenantSettings]) AS [Negocios];

IF COL_LENGTH(N'dbo.TenantUsers', N'OrderCancellationEmails') IS NOT NULL
    EXEC(N'SELECT ISNULL(SUM(CASE WHEN [OrderCancellationEmails] = 1 THEN 1 ELSE 0 END), 0) AS [PedidosCanceladosSi],
        ISNULL(SUM(CASE WHEN [OrderCancellationEmails] = 0 THEN 1 ELSE 0 END), 0) AS [PedidosCanceladosNo]
    FROM [TenantUsers]');

IF COL_LENGTH(N'dbo.TenantSettings', N'AnswerReminderMinutes') IS NOT NULL
    EXEC(N'SELECT [AnswerReminderMinutes], COUNT(*) AS [Negocios]
    FROM [TenantSettings]
    GROUP BY [AnswerReminderMinutes]
    ORDER BY [AnswerReminderMinutes]');
