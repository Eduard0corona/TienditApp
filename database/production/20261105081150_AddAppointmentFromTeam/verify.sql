-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29L, sin migración), la
--    más reciente es 20261103170924_AddAppointmentRespondedAt. Si es anterior, revisa primero el
--    verify.sql de su carpeta: apply.sql también aplica las que falten. Después:
--    20261105081150_AddAppointmentFromTeam.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: las columnas [Appointments].[FromTeam] (bit) y [RequestPhone] (nvarchar(50)), sin
--    NULL y con su valor por omisión, y el índice [IX_Appointments_OnlineRequests_Customer],
--    filtrado a las solicitudes en línea. Antes de aplicar y después de rollback.sql no devuelven
--    filas.
SELECT c.[name] AS [Columna], TYPE_NAME(c.[user_type_id]) AS [Tipo], c.[is_nullable] AS [Nulo],
    OBJECT_DEFINITION(c.[default_object_id]) AS [PorOmision]
FROM sys.columns c
WHERE c.[object_id] = OBJECT_ID(N'dbo.Appointments') AND c.[name] IN (N'FromTeam', N'RequestPhone')
ORDER BY c.[name];

SELECT i.[name] AS [Indice], i.[has_filter] AS [Filtrado], i.[filter_definition] AS [Filtro]
FROM sys.indexes i
WHERE i.[object_id] = OBJECT_ID(N'dbo.Appointments') AND i.[name] = N'IX_Appointments_OnlineRequests_Customer';

-- 3. Antes y después: las solicitudes en línea de los últimos 90 días ([Channel] = 1) y de cuántos
--    clientes (teléfonos distintos por negocio). Ya aplicada: cuántas vinieron del equipo (justo
--    después, cero: las de antes no lo guardan), cuántos clientes cuenta Explorar por la clave de
--    su teléfono (igual o menos que antes: un celular escrito +521 y +52 es uno) y cuántas no
--    tienen clave (justo después, cero; luego, solo las que guardó la versión anterior mientras se
--    publicaba esta). rollback.sql borra las dos columnas.
SELECT
    COUNT(*) AS [Solicitudes],
    COUNT(DISTINCT CONCAT(CONVERT(nvarchar(36), [TenantId]), N'|', [CustomerPhone])) AS [Clientes]
FROM [Appointments]
WHERE [Channel] = 1 AND [CreatedAt] >= DATEADD(DAY, -90, SYSDATETIMEOFFSET());

IF COL_LENGTH(N'dbo.Appointments', N'FromTeam') IS NOT NULL AND COL_LENGTH(N'dbo.Appointments', N'RequestPhone') IS NOT NULL
    EXEC(N'SELECT
        SUM(CASE WHEN [FromTeam] = 1 THEN 1 ELSE 0 END) AS [DelEquipo],
        COUNT(DISTINCT CONCAT(CONVERT(nvarchar(36), [TenantId]), N''|'', [RequestPhone])) AS [ClientesPorClave],
        SUM(CASE WHEN [RequestPhone] = N'''' THEN 1 ELSE 0 END) AS [SinClave]
    FROM [Appointments]
    WHERE [Channel] = 1 AND [CreatedAt] >= DATEADD(DAY, -90, SYSDATETIMEOFFSET())');
