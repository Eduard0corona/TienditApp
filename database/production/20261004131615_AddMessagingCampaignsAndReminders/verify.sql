-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. La más reciente debe ser
--    20261004125133_AddOrderRefunds; si es anterior, apply.sql también aplicará las que
--    falten (revisa primero el verify.sql de cada carpeta anterior).
SELECT TOP (8) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Antes y después: tablas y columnas nuevas. Antes de aplicar no devuelve filas; después,
--    siete (vacío otra vez después de rollback.sql).
SELECT N'Tabla' AS [Tipo], [name] AS [Nombre]
FROM sys.tables
WHERE [name] IN (N'MessageTemplates', N'MessageCampaigns', N'OutgoingMessages', N'MessagingOptOuts')
UNION ALL
SELECT N'Columna', N'TenantSettings.' + c.[name]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings')
  AND c.[name] IN (N'AppointmentRemindersEnabled', N'AppointmentReminderHoursBefore', N'AppointmentReminderTemplateId');

-- 3. Después: justo después de aplicar, todos los negocios tienen los recordatorios apagados y
--    24 horas de anticipación.
IF COL_LENGTH(N'dbo.TenantSettings', N'AppointmentRemindersEnabled') IS NOT NULL
    EXEC(N'SELECT [AppointmentRemindersEnabled], [AppointmentReminderHoursBefore], COUNT(*) AS [Negocios]
FROM [TenantSettings]
GROUP BY [AppointmentRemindersEnabled], [AppointmentReminderHoursBefore];');

-- 4. Antes de rollback.sql: exporta las bajas vigentes (quién pidió no recibir mensajes); el
--    rollback las borra.
IF OBJECT_ID(N'dbo.MessagingOptOuts') IS NOT NULL
    EXEC(N'SELECT [TenantId], [Phone], [Source], [OptedOutAt]
FROM [MessagingOptOuts]
WHERE [ResubscribedAt] IS NULL
ORDER BY [TenantId], [OptedOutAt];');

-- 5. Con el uso: estado de la cola de mensajes. [Pendientes] con [NextAttemptAt] de hace más de
--    15 minutos indica que el worker no está corriendo (el health check también lo reporta).
IF OBJECT_ID(N'dbo.OutgoingMessages') IS NOT NULL
    EXEC(N'SELECT [Status], COUNT(*) AS [Mensajes], MIN([NextAttemptAt]) AS [ProximoIntento]
FROM [OutgoingMessages]
GROUP BY [Status]
ORDER BY [Status];');

-- 6. Después: la migración quedó registrada (vacío después de rollback.sql).
SELECT [MigrationId]
FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261004131615_AddMessagingCampaignsAndReminders';
