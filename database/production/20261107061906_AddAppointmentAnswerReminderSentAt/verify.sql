-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29T), la más reciente
--    es 20261106060148_AddTenantUserQuoteReplyEmails. Si es anterior, revisa primero el verify.sql
--    de su carpeta: apply.sql también aplica las que falten. Después:
--    20261107061906_AddAppointmentAnswerReminderSentAt.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [Appointments].[AnswerReminderSentAt] (datetimeoffset), que admite NULL
--    y no tiene valor por omisión, junto a [RespondedAt] de la Fase 29J. Antes de aplicar y después
--    de rollback.sql solo devuelve [RespondedAt].
SELECT c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[is_nullable] AS [Nulo],
    OBJECT_DEFINITION(c.[default_object_id]) AS [PorOmision]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] IN (N'RespondedAt', N'AnswerReminderSentAt')
ORDER BY c.[name];

-- 3. Antes y después: las solicitudes en línea que siguen por confirmar ([Channel] = 1, [Status] = 5),
--    y las que llegaron en las últimas 24 horas, que son las que el barrido revisa. Ya aplicada: a
--    cuántas se les mandó su recordatorio (justo después, a ninguna). rollback.sql borra la columna.
SELECT COUNT(*) AS [PorConfirmar],
    SUM(CASE WHEN [CreatedAt] >= DATEADD(day, -1, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END) AS [UltimoDia]
FROM [Appointments]
WHERE [Channel] = 1 AND [Status] = 5;

IF COL_LENGTH(N'dbo.Appointments', N'AnswerReminderSentAt') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Recordadas], MAX([AnswerReminderSentAt]) AS [Ultimo]
    FROM [Appointments]
    WHERE [AnswerReminderSentAt] IS NOT NULL');
