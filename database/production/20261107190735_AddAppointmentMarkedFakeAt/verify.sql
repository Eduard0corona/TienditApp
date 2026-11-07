-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29U), la más reciente
--    es 20261107061906_AddAppointmentAnswerReminderSentAt. Si es anterior, revisa primero el
--    verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261107190735_AddAppointmentMarkedFakeAt.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [Appointments].[MarkedFakeAt] (datetimeoffset), que admite NULL y no
--    tiene valor por omisión, junto a [AnswerReminderSentAt] de la Fase 29U. Antes de aplicar y
--    después de rollback.sql solo devuelve [AnswerReminderSentAt].
SELECT c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[is_nullable] AS [Nulo],
    OBJECT_DEFINITION(c.[default_object_id]) AS [PorOmision]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] IN (N'AnswerReminderSentAt', N'MarkedFakeAt')
ORDER BY c.[name];

-- 3. Antes y después: las solicitudes en línea ([Channel] = 1) de los últimos 90 días, cuántas
--    siguen por confirmar ([Status] = 5) y cuántas se cancelaron ([Status] = 99). Ya aplicada: cuántas
--    se marcaron como falsas en esos días (justo después, ninguna). rollback.sql borra la columna.
SELECT COUNT(*) AS [EnLinea90Dias],
    SUM(CASE WHEN [Status] = 5 THEN 1 ELSE 0 END) AS [PorConfirmar],
    SUM(CASE WHEN [Status] = 99 THEN 1 ELSE 0 END) AS [Canceladas]
FROM [Appointments]
WHERE [Channel] = 1 AND [CreatedAt] >= DATEADD(day, -90, SYSDATETIMEOFFSET());

IF COL_LENGTH(N'dbo.Appointments', N'MarkedFakeAt') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Falsas], MAX([MarkedFakeAt]) AS [Ultima]
    FROM [Appointments]
    WHERE [MarkedFakeAt] IS NOT NULL');
