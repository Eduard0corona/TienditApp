-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 25), la más reciente es
--    20261017183622_AddServiceOrderPhotos. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261017225015_AddOnlineBooking.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las 2 columnas nuevas de [TenantSettings] y las 5 de [Appointments], las restricciones
--    CK_TenantSettings_OnlineBookingCapacity, CK_Appointments_Channel y CK_Appointments_TrackingToken,
--    la llave de [Appointments].[CustomerUserId] a [AspNetUsers] y los 3 índices nuevos. Antes de
--    aplicar y después de rollback.sql: ninguna fila.
SELECT N'COLUMN' AS [Tipo], OBJECT_NAME(c.[object_id]) + N'.' + c.[name] AS [Nombre],
    TYPE_NAME(c.[user_type_id]) AS [Definicion]
FROM sys.columns c
WHERE (c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] IN (N'OnlineBookingEnabled', N'OnlineBookingCapacity'))
    OR (c.[object_id] = OBJECT_ID(N'dbo.Appointments')
        AND c.[name] IN (N'Channel', N'CustomerUserId', N'ClientRequestId', N'TrackingToken', N'CancelledByCustomerAt'))
UNION ALL
SELECT N'CHECK', [name], [definition] COLLATE DATABASE_DEFAULT
FROM sys.check_constraints
WHERE [name] IN (N'CK_TenantSettings_OnlineBookingCapacity', N'CK_Appointments_Channel', N'CK_Appointments_TrackingToken')
UNION ALL
SELECT N'FOREIGN KEY', [name], [delete_referential_action_desc] COLLATE DATABASE_DEFAULT
FROM sys.foreign_keys
WHERE [name] = N'FK_Appointments_AspNetUsers_CustomerUserId'
UNION ALL
SELECT N'INDEX', [name], [filter_definition] COLLATE DATABASE_DEFAULT
FROM sys.indexes
WHERE [object_id] = OBJECT_ID(N'dbo.Appointments')
    AND [name] IN (N'IX_Appointments_CustomerUserId_StartAt', N'IX_Appointments_TenantId_ClientRequestId', N'IX_Appointments_TrackingToken')
ORDER BY [Tipo], [Nombre];

-- 3. Después de desplegar, y antes de rollback.sql: los negocios que prendieron las citas en línea y
--    las citas que agendaron sus clientes, por estado (5 por confirmar, 0 agendada, 1 confirmada,
--    3 completada, 4 no asistió, 99 cancelada). Recién aplicado: ninguna fila. Con una sola cita en
--    línea, rollback.sql se detiene (THROW 50019).
IF COL_LENGTH(N'dbo.TenantSettings', N'OnlineBookingEnabled') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [NegociosConCitasEnLinea]
FROM [TenantSettings]
WHERE [OnlineBookingEnabled] = 1;');
IF COL_LENGTH(N'dbo.Appointments', N'Channel') IS NOT NULL
    EXEC(N'SELECT [Status] AS [Estado], COUNT(*) AS [Citas], COUNT(DISTINCT [TenantId]) AS [Negocios]
FROM [Appointments]
WHERE [Channel] = 1
GROUP BY [Status]
ORDER BY [Status];');

-- 4. Antes de volver a la versión anterior sin rollback.sql: las solicitudes que siguen por confirmar
--    ([Status] 5). Esa versión no sabe aceptarlas ni rechazarlas: decídelas antes de volver. 0 al
--    aplicar.
IF COL_LENGTH(N'dbo.Appointments', N'Channel') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [PorConfirmar]
FROM [Appointments]
WHERE [Status] = 5;');

-- 5. Después: consistencia. Cada consulta debe dar 0 filas:
--    a. citas en línea sin enlace, o citas del negocio con enlace, cuenta, llave o cancelación del cliente;
--    b. citas por confirmar que no se agendaron en línea.
--    (El negocio sí puede mover una cita en línea a domicilio o cambiarle el servicio.)
IF COL_LENGTH(N'dbo.Appointments', N'Channel') IS NOT NULL
BEGIN
    EXEC(N'SELECT [Id], [TenantId], [Channel]
FROM [Appointments]
WHERE ([Channel] = 1 AND [TrackingToken] IS NULL)
    OR ([Channel] = 0 AND ([TrackingToken] IS NOT NULL OR [CustomerUserId] IS NOT NULL
        OR [ClientRequestId] IS NOT NULL OR [CancelledByCustomerAt] IS NOT NULL));');
    EXEC(N'SELECT [Id], [TenantId], [Status]
FROM [Appointments]
WHERE [Status] = 5 AND [Channel] <> 1;');
END;
