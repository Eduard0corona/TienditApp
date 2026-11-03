-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29I, sin migración), la
--    más reciente es 20261102205836_AddAppointmentProblemPhotos. Si es anterior, revisa primero el
--    verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261103155115_AddAppointmentRespondedAt.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [Appointments].[RespondedAt] (datetimeoffset, admite NULL) y el índice
--    [IX_Appointments_OnlineRequests_CreatedAt], filtrado a las solicitudes en línea. Antes de
--    aplicar y después de rollback.sql no devuelven filas.
SELECT c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[is_nullable] AS [Nulo]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] = N'RespondedAt';

SELECT i.[name] AS [Indice], i.[has_filter] AS [Filtrado], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Appointments') AND i.[name] = N'IX_Appointments_OnlineRequests_CreatedAt';

-- 3. Antes y después: las solicitudes en línea de los últimos 90 días ([Channel] = 1), cuántas
--    esperan respuesta ([Status] = 5, por confirmar) y, ya aplicada, cuántas tienen la hora en
--    que se respondieron. Justo después de aplicar, [Respondidas] es cero: las de antes no la
--    tienen. rollback.sql la borra.
SELECT
    COUNT(*) AS [Solicitudes],
    SUM(CASE WHEN [Status] = 5 THEN 1 ELSE 0 END) AS [PorConfirmar]
FROM [Appointments]
WHERE [Channel] = 1 AND [CreatedAt] >= DATEADD(DAY, -90, SYSDATETIMEOFFSET());

IF COL_LENGTH(N'dbo.Appointments', N'RespondedAt') IS NOT NULL
    EXEC(N'SELECT COUNT(*) AS [Respondidas]
    FROM [Appointments]
    WHERE [Channel] = 1 AND [RespondedAt] IS NOT NULL;');
