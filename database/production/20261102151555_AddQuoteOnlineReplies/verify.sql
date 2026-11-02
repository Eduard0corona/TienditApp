-- =====================================================================================
-- TienditApp (FoodParkDigital): consultas de solo lectura para antes y después de
-- apply.sql (o de rollback.sql). No modifican nada y solo cuentan o listan filas.
--     sqlcmd -S <servidor> -d <base> <autenticación> -W -i verify.sql
-- =====================================================================================
SET NOCOUNT ON;

-- 1. Antes: últimas migraciones aplicadas. Con master desplegado (la Fase 29F), la más reciente es
--    20261102073115_AddAppointmentWorkOrder. Si es anterior, revisa primero el verify.sql de su
--    carpeta: apply.sql también aplica las que falten. Después: 20261102151555_AddQuoteOnlineReplies.
SELECT TOP (5) [MigrationId], [ProductVersion]
FROM [__EFMigrationsHistory]
ORDER BY [MigrationId] DESC;

-- 2. Después: la columna [TenantSettings].[QuoteOnlineReplies] (bit, no nula) con su valor por
--    omisión ((1)), y [CK_Quotes_Reply] admite [ReplyChannel] 4. Antes de aplicar y después de
--    rollback.sql la columna no existe y la restricción llega hasta 3.
SELECT
    c.[name] AS [Columna],
    TYPE_NAME(c.[user_type_id]) AS [Tipo],
    c.[is_nullable] AS [Nulo],
    dc.[definition] AS [PorOmision]
FROM sys.columns c
LEFT JOIN sys.default_constraints dc ON dc.[object_id] = c.[default_object_id]
WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'QuoteOnlineReplies';

SELECT cc.[name] AS [Restriccion], cc.[definition] AS [Regla]
FROM sys.check_constraints cc
WHERE cc.[name] = N'CK_Quotes_Reply';

-- 3. Después: cuántos negocios tienen la opción encendida y cuántos la apagaron (rollback.sql se
--    detiene con 50055 si hay alguno apagado). Justo después de aplicar: todos encendidos. Antes de
--    aplicar y después de rollback.sql la columna no existe y esa parte no corre.
IF COL_LENGTH(N'dbo.TenantSettings', N'QuoteOnlineReplies') IS NOT NULL
    EXEC(N'SELECT
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [QuoteOnlineReplies] = 1) AS [Encendida],
        (SELECT COUNT(*) FROM [TenantSettings] WHERE [QuoteOnlineReplies] = 0) AS [Apagada];');

-- 4. Las cotizaciones que el cliente respondió desde su enlace ([ReplyChannel] = 4), aceptadas
--    ([Status] = 2) y rechazadas o reemplazadas después (3 y 5). Antes de desplegar la Fase 29G:
--    cero. rollback.sql se detiene con 50055 si no es cero.
SELECT
    (SELECT COUNT(*) FROM [Quotes] WHERE [ReplyChannel] = 4 AND [Status] = 2) AS [AceptadasDesdeSuEnlace],
    (SELECT COUNT(*) FROM [Quotes] WHERE [ReplyChannel] = 4 AND [Status] IN (3, 5)) AS [RechazadasDesdeSuEnlace];
