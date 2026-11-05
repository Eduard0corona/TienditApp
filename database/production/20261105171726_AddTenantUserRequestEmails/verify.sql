-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29M), la más reciente
--    es 20261105081150_AddAppointmentFromTeam. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después:
--    20261105171726_AddTenantUserRequestEmails.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [TenantUsers].[RequestEmails] (bit), que admite NULL y no tiene valor
--    por omisión. Antes de aplicar y después de rollback.sql no devuelve filas.
SELECT c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[is_nullable] AS [Nulo],
    OBJECT_DEFINITION(c.[default_object_id]) AS [PorOmision]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantUsers') AND c.[name] = N'RequestEmails';

-- 3. Antes y después: los miembros activos de los negocios por rol (0 dueño, 1 administrador,
--    2 empleado). Ya aplicada: cuántos eligieron recibir los correos de las solicitudes, cuántos
--    los apagaron y cuántos no han elegido (justo después, todos: a los dueños les llegan así).
--    rollback.sql borra la columna, con lo que cada quien eligió.
SELECT [Role] AS [Rol], COUNT(*) AS [Miembros]
FROM [TenantUsers]
WHERE [Status] = 1
GROUP BY [Role]
ORDER BY [Role];

IF COL_LENGTH(N'dbo.TenantUsers', N'RequestEmails') IS NOT NULL
    EXEC(N'SELECT [Role] AS [Rol],
        SUM(CASE WHEN [RequestEmails] = 1 THEN 1 ELSE 0 END) AS [Prendidos],
        SUM(CASE WHEN [RequestEmails] = 0 THEN 1 ELSE 0 END) AS [Apagados],
        SUM(CASE WHEN [RequestEmails] IS NULL THEN 1 ELSE 0 END) AS [SinElegir]
    FROM [TenantUsers]
    WHERE [Status] = 1
    GROUP BY [Role]
    ORDER BY [Role]');
